// /*
//     Copyright (C) 2025 0x90d
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
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
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

		private string? _newPath;
		public string? NewPath {
			get => _newPath;
			set => this.RaiseAndSetIfChanged(ref _newPath, value);
		}

		private bool _selected;
		public bool Selected {
			get => _selected;
			set => this.RaiseAndSetIfChanged(ref _selected, value);
		}

		private RelocateConfidence _confidence;
		public RelocateConfidence Confidence {
			get => _confidence;
			set => this.RaiseAndSetIfChanged(ref _confidence, value);
		}
		public string ConfidenceString => Confidence.ToString();

		private string _note = string.Empty;
		public string Note {
			get => _note;
			set => this.RaiseAndSetIfChanged(ref _note, value);
		}

		public RelocateCandidate(FileEntry e) {
			Entry = e;
		}
	}
	internal class RelocateFilesDialogVM : ReactiveObject {
		private readonly Window _owner;
		private readonly HashSet<FileEntry> _entries;
		private readonly string TempDatabaseFile;
		private readonly DatabaseWrapper DbWrapper;
		static readonly JsonSerializerOptions serializerOptions = new() {
			IncludeFields = true,
		};
		public FlatTreeDataGridSource<RelocateCandidate> TreeSource { get; }

		public RelocateFilesDialogVM(Window owner) {
			_owner = owner;
			TempDatabaseFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
			ScanEngine.ExportDataBaseToJson(TempDatabaseFile, serializerOptions);
			DbWrapper = JsonSerializer.Deserialize<DatabaseWrapper>(File.ReadAllBytes(TempDatabaseFile), serializerOptions)!;
			_entries = [.. DbWrapper.Entries];


			TreeSource = new FlatTreeDataGridSource<RelocateCandidate>(Preview) {
				Columns =
					{
					new CheckBoxColumn<RelocateCandidate>(
						header: App.Lang["Apply"],
						getter: n => n.Selected,
						setter: (n,v) => n.Selected = v
					),
					// 1) Old Path
					new TextColumn<RelocateCandidate, string>(
						header: App.Lang["Path"],
						getter: n => n.OldPath ?? string.Empty
					),
					// 2) New Path
					new TextColumn<RelocateCandidate, string>(
						header: App.Lang["NewPath"],
						getter: n => n.NewPath ?? string.Empty
					),
					// 3) Confidence
					new TextColumn<RelocateCandidate, string>(
						header: App.Lang["Confidence"],
						getter: n => n.ConfidenceString ?? string.Empty
					),
					// 4) Notes
					new TextColumn<RelocateCandidate, string>(
						header: App.Lang["Note"],
						getter: n => n.Note ?? string.Empty
					),
				}
			};
			TreeSource.RowSelection!.SingleSelect = false;
		}

		// Mode toggles
		bool _isModePrefix = true;
		public bool IsModePrefix {
			get => _isModePrefix;
			set {
				this.RaiseAndSetIfChanged(ref _isModePrefix, value);
				if (value) IsModeRescan = false;
			}
		}

		bool _isModeRescan;
		public bool IsModeRescan {
			get => _isModeRescan;
			set {
				this.RaiseAndSetIfChanged(ref _isModeRescan, value);
				if (value) IsModePrefix = false;
			}
		}

		// Inputs for A
		string _oldPrefix = string.Empty;
		public string OldPrefix { get => _oldPrefix; set => this.RaiseAndSetIfChanged(ref _oldPrefix, value); }

		string _newPrefix = string.Empty;
		public string NewPrefix { get => _newPrefix; set => this.RaiseAndSetIfChanged(ref _newPrefix, value); }

		// Inputs for B
		public ObservableCollection<string> ScanRoots { get; } = new();
		bool _useModifiedTime = true;
		public bool UseModifiedTime { get => _useModifiedTime; set => this.RaiseAndSetIfChanged(ref _useModifiedTime, value); }

		bool _useDuration = false;
		public bool UseDuration { get => _useDuration; set => this.RaiseAndSetIfChanged(ref _useDuration, value); }
		bool _IsLoading = false;
		public bool IsLoading { get => _IsLoading; set => this.RaiseAndSetIfChanged(ref _IsLoading, value); }

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
		public ReactiveCommand<Unit, Unit> Apply => ReactiveCommand.Create(ApplyImpl, this.WhenAnyValue(x => x.CanApply));
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

		bool _canApply;
		public bool CanApply {
			get => _canApply;
			set => this.RaiseAndSetIfChanged(ref _canApply, value);
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
				File.WriteAllBytes(TempDatabaseFile, JsonSerializer.SerializeToUtf8Bytes(DbWrapper, serializerOptions));
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed to save changes to database file, because of {e}");
				return;
			}
			ScanEngine.ImportDataBaseFromJson(TempDatabaseFile, serializerOptions);
			ScanEngine.SaveDatabase();
			try {
				File.Delete(TempDatabaseFile);
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed to delete temporarily database file '{TempDatabaseFile}', because of {e}");
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
