// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using VDF.Core;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public enum RelocateConfidence {
		Prefix,
		SizeOnly,
		SizeAndModified,
		SizeModifiedDuration,
		Ambiguous,
		NotFound
	}
	public class RelocateCandidate : ReactiveObject {
		public FileEntry Entry { get; }
		public string OldPath => Entry.Path;

		public string? NewPath {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		public bool Selected {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		public RelocateConfidence Confidence {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		public string ConfidenceString => Confidence.ToString();

		public string Note {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = string.Empty;

		public RelocateCandidate(FileEntry e) {
			Entry = e;
		}
	}
	internal class RelocateFilesDialogVM : ReactiveObject {
		readonly Window _owner;
		readonly HashSet<FileEntry> _entries;
		readonly string TempDatabaseFile;

		readonly DatabaseWrapper DbWrapper;
		// Only forwarded to ScanEngine.ExportDataBaseToJson (which serializes through
		// its own typed metadata); local (de)serialization uses CoreJsonContext directly.
		static readonly JsonSerializerOptions serializerOptions = new() {
			IncludeFields = true,
		};
		public RelocateFilesDialogVM(Window owner) {
			_owner = owner;
			TempDatabaseFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
			ScanEngine.ExportDataBaseToJson(TempDatabaseFile, serializerOptions);
			DbWrapper = JsonSerializer.Deserialize(File.ReadAllBytes(TempDatabaseFile), VDF.Core.Utils.CoreJsonContext.Default.DatabaseWrapper)!;
			_entries = [.. DbWrapper.Entries];
		}

		// Mode toggles
		public bool IsModePrefix {
			get;
			set {
				this.RaiseAndSetIfChanged(ref field, value);
				if (value) IsModeRescan = false;
			}
		} = true;

		public bool IsModeRescan {
			get;
			set {
				this.RaiseAndSetIfChanged(ref field, value);
				if (value) IsModePrefix = false;
			}
		}

		// Inputs for A
		public string OldPrefix { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;

		public string NewPrefix { get; set => this.RaiseAndSetIfChanged(ref field, value); } = string.Empty;

		// Inputs for B
		public ObservableCollection<string> ScanRoots { get; } = new();
		public bool UseModifiedTime { get; set => this.RaiseAndSetIfChanged(ref field, value); } = true;

		public bool UseDuration { get; set => this.RaiseAndSetIfChanged(ref field, value); } = false;
		public bool IsLoading { get; set => this.RaiseAndSetIfChanged(ref field, value); } = false;

		// Preview
		public AvaloniaList<RelocateCandidate> Preview { get; } = new();

		public ReactiveCommand<Unit, Unit> BrowseOldPrefix => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					Title = App.Lang["Dialog.SelectFolder"]
				});

			if (result == null || result.Count == 0) return;
			if (!string.IsNullOrWhiteSpace(result[0])) OldPrefix = result[0];
		});
		public ReactiveCommand<Unit, Unit> BrowseNewPrefix => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					Title = App.Lang["Dialog.SelectFolder"]
				});

			if (result == null || result.Count == 0) return;
			if (!string.IsNullOrWhiteSpace(result[0])) NewPrefix = result[0];
		});
		public ReactiveCommand<Unit, Unit> AddScanRoot => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
					new FolderPickerOpenOptions() {
						Title = App.Lang["Dialog.SelectFolder"]
					});

			if (result == null || result.Count == 0) return;
			if (!string.IsNullOrWhiteSpace(result[0])) ScanRoots.Add(result[0]);
		});
		public ReactiveCommand<Unit, Unit> RemoveScanRoot => ReactiveCommand.Create(() => {
			if (ScanRoots.Any()) ScanRoots.RemoveAt(ScanRoots.Count - 1);
		});
		public ReactiveCommand<Unit, Unit> BuildPreview => ReactiveCommand.Create(BuildPreviewImpl);
		public ReactiveCommand<Unit, Unit> Apply => ReactiveCommand.Create(ApplyImpl, CanApplyObservable);

		IObservable<bool> CanApplyObservable {
			[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = MainWindowVM.WhenAnyValueTrimJustification)]
			get => this.WhenAnyValue(x => x.CanApply);
		}
		public ReactiveCommand<Unit, Unit> Cancel => ReactiveCommand.Create(() => _owner.Close());
		public ReactiveCommand<Unit, Unit> CheckAllResults => ReactiveCommand.Create(() => {
			foreach (var item in Preview) {
				item.Selected = true;
			}
		});
		public ReactiveCommand<Unit, Unit> UncheckAllResults => ReactiveCommand.Create(() => {
			foreach (var item in Preview) {
				item.Selected = false;
			}
		});

		public bool CanApply {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		// --- Core preview builders ---

		async void BuildPreviewImpl() {
			Preview.Clear();
			CanApply = false;
			if (IsModePrefix) {
				BuildPreviewPrefix();
			}
			else {
				IsLoading = true;
				await Task.Run(BuildPreviewRescan);
				IsLoading = false;
			}

			// Auto-enable apply if there is at least one selected row
			CanApply = true;
		}

		void BuildPreviewPrefix() {
			// Normalize
			string oldP = PathRelocator.NormalizePrefixPublic(OldPrefix);
			string newP = PathRelocator.NormalizePrefixPublic(NewPrefix);
			if (string.IsNullOrWhiteSpace(oldP) || string.IsNullOrWhiteSpace(newP)) return;

			var comparison = (CoreUtils.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

			List<RelocateCandidate> candidates = new();

			foreach (var e in _entries) {
				var full = Path.GetFullPath(e.Path);
				if (full.StartsWith(oldP, comparison)) {
					var suffix = full.Substring(oldP.Length);
					var newPath = Path.Combine(newP, suffix);

					var cand = new RelocateCandidate(e) {
						NewPath = newPath,
						Confidence = RelocateConfidence.Prefix,
						Note = "Prefix replace",
						Selected = true
					};
					candidates.Add(cand);
				}
			}
			Preview.AddRange(candidates);
		}

		void BuildPreviewRescan() {
			if (!ScanRoots.Any()) return;
			// 1) Build list of missing entries (files that no longer exist at their recorded path)
			var missing = _entries.Where(x => !File.Exists(x.Path)).ToList();

			// 2) Build an index of all files in scan roots by size
			var bySize = new Dictionary<long, List<string>>();
			foreach (var root in ScanRoots) {
				if (!Directory.Exists(root)) continue;
				foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
					try {
						var fi = new FileInfo(path);
						if (!bySize.TryGetValue(fi.Length, out var list)) {
							list = new List<string>(1);
							bySize[fi.Length] = list;
						}
						list.Add(fi.FullName);
					}
					catch { /* ignore IO issues */ }
				}
			}

			List<RelocateCandidate> candidatesNew = new();

			// 3) For each missing entry, try to match by size -> refine by modified time -> refine by duration
			foreach (var e in missing) {
				var cand = new RelocateCandidate(e);

				if (!bySize.TryGetValue(e.FileSize, out var candidates) || candidates.Count == 0) {
					cand.Confidence = RelocateConfidence.NotFound;
					cand.Note = "No same-size file found in scan roots";
					cand.Selected = false;
					candidatesNew.Add(cand);
					continue;
				}

				// Unique by size
				if (candidates.Count == 1) {
					cand.NewPath = candidates[0];
					cand.Confidence = RelocateConfidence.SizeOnly;
					cand.Note = "Unique by size";
					cand.Selected = true;
					candidatesNew.Add(cand);
					continue;
				}

				// Refine by LastWriteTimeUtc within tolerance
				IEnumerable<string> filtered = candidates;
				if (UseModifiedTime) {
					const int tolSeconds = 2;
					filtered = filtered.Where(p => {
						try {
							var fi = new FileInfo(p);
							var dt = fi.LastWriteTimeUtc;
							var delta = (dt - e.DateModified).Duration();
							return delta <= TimeSpan.FromSeconds(tolSeconds);
						}
						catch { return false; }
					}).ToList();
				}

				// If still many and duration available, refine by duration seconds
				if (UseDuration && e.mediaInfo != null) {
					var durSec = Math.Round(e.mediaInfo.Duration.TotalSeconds, 2);
					filtered = filtered.Where(p => {
						var durationSec = QuickMeta.TryRead(p);
						if (durationSec == null) return false;
						return Math.Abs(durationSec.Value - durSec) <= 0.5; // half-second tolerance
					}).ToList();
				}

				var filteredList = filtered.ToList();
				if (filteredList.Count == 1) {
					cand.NewPath = filteredList[0];
					cand.Selected = true;
					cand.Confidence = UseDuration ? RelocateConfidence.SizeModifiedDuration :
									  (UseModifiedTime ? RelocateConfidence.SizeAndModified : RelocateConfidence.SizeOnly);
					cand.Note = $"Resolved with {(UseDuration ? "duration" : (UseModifiedTime ? "modified time" : "size only"))}";
				}
				else if (filteredList.Count > 1) {
					cand.NewPath = null;
					cand.Selected = false;
					cand.Confidence = RelocateConfidence.Ambiguous;
					cand.Note = $"{filteredList.Count} candidates remain (ambiguous)";
				}
				else {
					cand.NewPath = null;
					cand.Selected = false;
					cand.Confidence = RelocateConfidence.NotFound;
					cand.Note = "No candidate after refinements";
				}

				candidatesNew.Add(cand);
			}
			Preview.AddRange(candidatesNew);
			return;
		}

		void ApplyImpl() {
			foreach (var row in Preview.Where(p => p.Selected && !string.IsNullOrWhiteSpace(p.NewPath))) {
				if (string.IsNullOrEmpty(row.NewPath))
					continue;
				_entries.Remove(row.Entry);
				row.Entry.Path = Path.GetFullPath(row.NewPath!);
				_entries.Add(row.Entry);
			}

			DbWrapper.Entries = [.. _entries];
			try {
				File.WriteAllBytes(TempDatabaseFile, JsonSerializer.SerializeToUtf8Bytes(DbWrapper, VDF.Core.Utils.CoreJsonContext.Default.DatabaseWrapper));
			}
			catch (Exception e) {
				Logger.Instance.Error($"Failed to save changes to database file, because of {e}");
				return;
			}
			ScanEngine.ImportDataBaseFromJson(TempDatabaseFile, serializerOptions);
			ScanEngine.SaveDatabase();
			try {
				File.Delete(TempDatabaseFile);
			}
			catch (Exception e) {
				Logger.Instance.Warn($"Failed to delete temporarily database file '{TempDatabaseFile}', because of {e}");
			}
			_owner.Close();
		}
	}

	public static class QuickMeta {
		public static double? TryRead(string path) {
			try {
				var info = FFProbeEngine.GetMediaInfo(path, false);
				return info?.Duration.TotalSeconds ?? null;
			}
			catch {
				return null;
			}
		}
	}
}
