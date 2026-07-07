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
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using Avalonia.Input.Platform;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Utils;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {

	/// <summary>One flag badge on a database row; clicking it filters by that flag.</summary>
	internal sealed record DbFlagChip(EntryFlags Flag, string Label, string Kind);

	/// <summary>One database entry row: path is editable via an explicit edit mode (✎/F2),
	/// everything else is display-only; media columns come from the stored MediaInfo.</summary>
	internal sealed class DatabaseEntryVM : ReactiveObject {
		public DatabaseEntryVM(FileEntry entry) => Entry = entry;
		public FileEntry Entry { get; }

		string? pathBeforeEdit;

		public string Path {
			get => Entry.Path;
			set {
				if (!string.IsNullOrWhiteSpace(value))
					Entry.Path = value;
			}
		}

		bool _IsEditingPath;
		public bool IsEditingPath {
			get => _IsEditingPath;
			private set => this.RaiseAndSetIfChanged(ref _IsEditingPath, value);
		}
		public void BeginPathEdit() {
			pathBeforeEdit = Entry.Path;
			IsEditingPath = true;
		}
		public void CommitPathEdit() {
			pathBeforeEdit = null;
			IsEditingPath = false;
			this.RaisePropertyChanged(nameof(Path));
			this.RaisePropertyChanged(nameof(FileName));
			this.RaisePropertyChanged(nameof(FolderText));
		}
		public void CancelPathEdit() {
			if (pathBeforeEdit != null)
				Entry.Path = pathBeforeEdit;
			CommitPathEdit();
		}

		public string FileName => System.IO.Path.GetFileName(Entry.Path);
		public string FolderText => Entry.Folder;
		public string SizeText => Entry.FileSize.BytesToString();
		public string DateCreatedText => Entry.DateCreated.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
		public string DateModifiedText => Entry.DateModified.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
		public bool IsImage => Entry.Flags.Any(EntryFlags.IsImage);

		public string DurationText {
			get {
				if (IsImage) return "—";
				var duration = DatabaseEditorRules.GetDuration(Entry);
				return duration <= TimeSpan.Zero ? string.Empty : duration.ToString("hh\\:mm\\:ss");
			}
		}
		public string ResolutionText {
			get {
				var stream = DatabaseEditorRules.GetVideoStream(Entry);
				if (stream == null || stream.Width <= 0) return string.Empty;
				string res = $"{stream.Width}×{stream.Height}";
				return !IsImage && stream.FrameRate > 0
					? res + " · " + stream.FrameRate.ToString("0.##", CultureInfo.CurrentCulture)
					: res;
			}
		}
		public string CodecText {
			get {
				var stream = DatabaseEditorRules.GetVideoStream(Entry);
				if (stream == null) return string.Empty;
				string bitrate = ResultsBadgeRules.FormatBitrate(DatabaseEditorRules.GetBitRate(Entry), CultureInfo.CurrentCulture);
				return ResultsBadgeRules.JoinParts(" · ", stream.CodecName, bitrate);
			}
		}

		public IReadOnlyList<DbFlagChip> FlagChips =>
			DatabaseEditorRules.ChipFlags
				.Where(f => Entry.Flags.Any(f))
				.Select(f => new DbFlagChip(f, App.Lang[$"DatabaseViewer.Flag.{f}"], DatabaseEditorRules.ChipKind(f)))
				.ToList();

		/// <summary>Re-evaluates the computed columns after a bulk operation mutated the entry.</summary>
		public void RefreshDisplay() {
			this.RaisePropertyChanged(nameof(FlagChips));
			this.RaisePropertyChanged(nameof(DurationText));
			this.RaisePropertyChanged(nameof(ResolutionText));
			this.RaisePropertyChanged(nameof(CodecText));
		}
	}

	internal class DatabaseViewerVM : ReactiveObject {
		readonly List<DatabaseEntryVM> allEntries;
		private DatabaseWrapper DbWrapper;
		readonly string TempDatabaseFile;
		// Only forwarded to ScanEngine.ExportDataBaseToJson (which serializes through
		// its own typed metadata); local (de)serialization uses CoreJsonContext directly.
		static readonly JsonSerializerOptions serializerOptions = new() {
			IncludeFields = true,
		};

		public DatabaseViewerVM() {
			TempDatabaseFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
			ScanEngine.ExportDataBaseToJson(TempDatabaseFile, serializerOptions);
			DbWrapper = JsonSerializer.Deserialize(File.ReadAllBytes(TempDatabaseFile), VDF.Core.Utils.CoreJsonContext.Default.DatabaseWrapper)!;
			allEntries = DbWrapper.Entries.Select(e => new DatabaseEntryVM(e)).ToList();
			RecomputeCounts();
			ApplyFilter();
		}

		IReadOnlyList<DatabaseEntryVM> _DatabaseFilesView = Array.Empty<DatabaseEntryVM>();
		public IReadOnlyList<DatabaseEntryVM> DatabaseFilesView {
			get => _DatabaseFilesView;
			private set => this.RaiseAndSetIfChanged(ref _DatabaseFilesView, value);
		}

		// ---------- filter state (chips are single-select via command, like the log view) ----------
		DbTypeFilter typeFilter = DbTypeFilter.All;
		bool errorsOnly;
		EntryFlags? flagFilter;

		public bool IsTypeAll => typeFilter == DbTypeFilter.All && !errorsOnly && flagFilter == null;
		public bool IsTypeVideos => typeFilter == DbTypeFilter.Videos;
		public bool IsTypeImages => typeFilter == DbTypeFilter.Images;
		public bool IsErrorsOnly => errorsOnly;
		public bool HasFlagFilter => flagFilter != null;
		public string FlagFilterLabel => flagFilter is { } f ? App.Lang[$"DatabaseViewer.Flag.{f}"] : string.Empty;

		public ReactiveCommand<string, Unit> SetTypeFilterCommand => ReactiveCommand.Create<string>(which => {
			switch (which) {
				case "Videos": typeFilter = DbTypeFilter.Videos; break;
				case "Images": typeFilter = DbTypeFilter.Images; break;
				default: typeFilter = DbTypeFilter.All; errorsOnly = false; flagFilter = null; break;
			}
			FilterChanged();
		});

		public ReactiveCommand<Unit, Unit> ToggleErrorsOnlyCommand => ReactiveCommand.Create(() => {
			errorsOnly = !errorsOnly;
			FilterChanged();
		});

		public ReactiveCommand<string, Unit> SetFlagFilterCommand => ReactiveCommand.Create<string>(name => {
			flagFilter = name.Length == 0 ? null : Enum.Parse<EntryFlags>(name);
			FilterChanged();
		});

		public ReactiveCommand<DbFlagChip, Unit> FilterByChipCommand => ReactiveCommand.Create<DbFlagChip>(chip => {
			if (chip == null) return;
			flagFilter = chip.Flag;
			FilterChanged();
		});

		void FilterChanged() {
			this.RaisePropertyChanged(nameof(IsTypeAll));
			this.RaisePropertyChanged(nameof(IsTypeVideos));
			this.RaisePropertyChanged(nameof(IsTypeImages));
			this.RaisePropertyChanged(nameof(IsErrorsOnly));
			this.RaisePropertyChanged(nameof(HasFlagFilter));
			this.RaisePropertyChanged(nameof(FlagFilterLabel));
			ApplyFilter();
		}

		// ---------- sort ----------
		DbSortMode sortMode = DbSortMode.Path;
		EntryFlags? sortFlagFirst;

		public string SortLabel => sortFlagFirst is { } f
			? App.Lang[$"DatabaseViewer.Flag.{f}"]
			: App.Lang[$"DatabaseViewer.Sort.{sortMode}"];

		public ReactiveCommand<string, Unit> SetSortCommand => ReactiveCommand.Create<string>(name => {
			sortMode = Enum.Parse<DbSortMode>(name);
			sortFlagFirst = null;
			this.RaisePropertyChanged(nameof(SortLabel));
			ApplyFilter();
		});

		public ReactiveCommand<string, Unit> SetFlagSortCommand => ReactiveCommand.Create<string>(name => {
			sortFlagFirst = Enum.Parse<EntryFlags>(name);
			this.RaisePropertyChanged(nameof(SortLabel));
			ApplyFilter();
		});

		// ---------- counts ----------
		Dictionary<EntryFlags, int> flagCounts = new();
		int errorCount;
		public string ErrorsChipText => string.Format(CultureInfo.CurrentCulture,
			"{0} · {1:N0}", App.Lang["DatabaseViewer.FilterErrors"], errorCount);
		public string FlagMenuCount(EntryFlags flag) =>
			flagCounts.TryGetValue(flag, out int n) ? n.ToString("N0", CultureInfo.CurrentCulture) : "0";
		// Per-flag menu captions (bindable)
		public string CountExcluded => FlagMenuCount(EntryFlags.ManuallyExcluded);
		public string CountThumbError => FlagMenuCount(EntryFlags.ThumbnailError);
		public string CountMetadataError => FlagMenuCount(EntryFlags.MetadataError);
		public string CountTooDark => FlagMenuCount(EntryFlags.TooDark);
		public string CountNoAudio => FlagMenuCount(EntryFlags.NoAudioTrack);
		public string CountSilent => FlagMenuCount(EntryFlags.SilentAudioTrack);
		public string CountReparse => FlagMenuCount(EntryFlags.ReparsePoint);

		void RecomputeCounts() {
			(flagCounts, errorCount) = DatabaseEditorRules.CountFlags(allEntries.Select(e => e.Entry));
			this.RaisePropertyChanged(nameof(ErrorsChipText));
			this.RaisePropertyChanged(nameof(CountExcluded));
			this.RaisePropertyChanged(nameof(CountThumbError));
			this.RaisePropertyChanged(nameof(CountMetadataError));
			this.RaisePropertyChanged(nameof(CountTooDark));
			this.RaisePropertyChanged(nameof(CountNoAudio));
			this.RaisePropertyChanged(nameof(CountSilent));
			this.RaisePropertyChanged(nameof(CountReparse));
		}

		// ---------- column visibility (session-only; Modified starts hidden) ----------
		bool _ShowSizeColumn = true;
		public bool ShowSizeColumn { get => _ShowSizeColumn; set => this.RaiseAndSetIfChanged(ref _ShowSizeColumn, value); }
		bool _ShowCreatedColumn = true;
		public bool ShowCreatedColumn { get => _ShowCreatedColumn; set => this.RaiseAndSetIfChanged(ref _ShowCreatedColumn, value); }
		bool _ShowModifiedColumn;
		public bool ShowModifiedColumn { get => _ShowModifiedColumn; set => this.RaiseAndSetIfChanged(ref _ShowModifiedColumn, value); }
		bool _ShowDurationColumn = true;
		public bool ShowDurationColumn { get => _ShowDurationColumn; set => this.RaiseAndSetIfChanged(ref _ShowDurationColumn, value); }
		bool _ShowResolutionColumn = true;
		public bool ShowResolutionColumn { get => _ShowResolutionColumn; set => this.RaiseAndSetIfChanged(ref _ShowResolutionColumn, value); }
		bool _ShowCodecColumn = true;
		public bool ShowCodecColumn { get => _ShowCodecColumn; set => this.RaiseAndSetIfChanged(ref _ShowCodecColumn, value); }
		bool _ShowFlagsColumn = true;
		public bool ShowFlagsColumn { get => _ShowFlagsColumn; set => this.RaiseAndSetIfChanged(ref _ShowFlagsColumn, value); }

		// ---------- status ----------
		public string EntryCountText => string.Format(CultureInfo.CurrentCulture, "{0:N0}", allEntries.Count);
		public string ShownCountText => string.Format(CultureInfo.CurrentCulture, "{0:N0}", DatabaseFilesView.Count);
		public bool IsFiltered => DatabaseFilesView.Count != allEntries.Count;
		public string DatabaseLocationText {
			get {
				try {
					string folder = CoreUtils.ResolveDatabaseFolder(SettingsFile.Instance.CustomDatabaseFolder);
					var file = new FileInfo(VDF.Core.Utils.FileUtils.SafePathCombine(folder, "ScannedFiles.db"));
					return file.Exists ? $"{file.Length.BytesToString()} · {folder}" : folder;
				}
				catch (Exception) {
					return string.Empty;
				}
			}
		}

		int _SelectedCount;
		public int SelectedCount {
			get => _SelectedCount;
			set => this.RaiseAndSetIfChanged(ref _SelectedCount, value);
		}

		string _SearchText = string.Empty;
		public string SearchText {
			get => _SearchText;
			set {
				if (value == _SearchText) return;
				_SearchText = value;
				this.RaisePropertyChanged(nameof(SearchText));
				ApplyFilter();
			}
		}

		void ApplyFilter() {
			var filter = new DbFilterState {
				Type = typeFilter,
				ErrorsOnly = errorsOnly,
				Flag = flagFilter,
				Search = SearchText ?? string.Empty,
			};
			var view = allEntries.Where(e => DatabaseEditorRules.Matches(e.Entry, filter)).ToList();
			var comparison = DatabaseEditorRules.BuildComparison(sortMode, sortFlagFirst);
			view.Sort((a, b) => comparison(a.Entry, b.Entry));
			DatabaseFilesView = view;
			this.RaisePropertyChanged(nameof(EntryCountText));
			this.RaisePropertyChanged(nameof(ShownCountText));
			this.RaisePropertyChanged(nameof(IsFiltered));
		}

		// Wired by the view: returns the list box's current selection.
		internal Func<IEnumerable<DatabaseEntryVM>>? SelectionProvider;

		List<DatabaseEntryVM> Selected() => SelectionProvider?.Invoke().ToList() ?? new List<DatabaseEntryVM>();

		public void Save() {
			DbWrapper.Entries = new HashSet<FileEntry>(allEntries.Select(e => e.Entry));
			File.WriteAllBytes(TempDatabaseFile, JsonSerializer.SerializeToUtf8Bytes(DbWrapper, VDF.Core.Utils.CoreJsonContext.Default.DatabaseWrapper));
			ScanEngine.ImportDataBaseFromJson(TempDatabaseFile, serializerOptions);
			ScanEngine.SaveDatabase();
			try {
				File.Delete(TempDatabaseFile);
			}
			catch (Exception e) {
				Logger.Instance.Warn($"Failed to delete temporarily database file '{TempDatabaseFile}', because of {e}");
			}
		}

		public ReactiveCommand<Unit, Unit> DeleteSelectedEntries => ReactiveCommand.Create(() => {
			var selected = Selected();
			if (selected.Count == 0) return;
			foreach (var item in selected)
				allEntries.Remove(item);
			RecomputeCounts();
			ApplyFilter();
		});

		/// <summary>Removes every entry the current filter shows (confirmed) — turns any
		/// flag/error filter into a bulk cleanup.</summary>
		public ReactiveCommand<Unit, Unit> RemoveShownEntriesCommand => ReactiveCommand.CreateFromTask(async () => {
			var shown = DatabaseFilesView.ToList();
			if (shown.Count == 0) return;
			var confirm = await MessageBoxService.Show(
				string.Format(CultureInfo.CurrentCulture, App.Lang["DatabaseViewer.RemoveShownConfirm"], shown.Count),
				Data.MessageBoxButtons.Yes | Data.MessageBoxButtons.No);
			if (confirm != Data.MessageBoxButtons.Yes) return;
			foreach (var item in shown)
				allEntries.Remove(item);
			RecomputeCounts();
			ApplyFilter();
		});

		/// <summary>Excludes the selection from scans; if everything selected is already
		/// excluded, includes it again (toggle).</summary>
		public ReactiveCommand<Unit, Unit> ToggleExcludeSelectedCommand => ReactiveCommand.Create(() => {
			var selected = Selected();
			if (selected.Count == 0) return;
			bool exclude = selected.Any(s => !s.Entry.Flags.Any(EntryFlags.ManuallyExcluded));
			foreach (var item in selected) {
				item.Entry.Flags.Set(EntryFlags.ManuallyExcluded, exclude);
				item.RefreshDisplay();
			}
			RecomputeCounts();
		});

		/// <summary>Drops cached media data (hashes, fingerprints, probe info) so the next
		/// scan re-extracts the selection — the repair action for bad thumbnails/metadata.</summary>
		public ReactiveCommand<Unit, Unit> ClearHashesSelectedCommand => ReactiveCommand.Create(() => {
			var selected = Selected();
			foreach (var item in selected) {
				item.Entry.ClearCachedMediaData();
				item.RefreshDisplay();
			}
			RecomputeCounts();
		});

		public ReactiveCommand<Unit, Unit> OpenSelectedInFolderCommand => ReactiveCommand.Create(() => {
			var first = Selected().FirstOrDefault();
			if (first == null) return;
			try {
				if (CoreUtils.IsWindows)
					ShellUtils.ShowInExplorer(first.Entry.Path);
				else
					Process.Start(new ProcessStartInfo {
						FileName = Path.GetDirectoryName(first.Entry.Path) ?? first.Entry.Path,
						UseShellExecute = true,
					});
			}
			catch (Exception ex) {
				Logger.Instance.Warn($"Failed to open '{first.Entry.Path}' in folder: {ex.Message}");
			}
		});

		public ReactiveCommand<Unit, Unit> CopySelectedPathsCommand => ReactiveCommand.CreateFromTask(async () => {
			var selected = Selected();
			if (selected.Count == 0) return;
			if (ApplicationHelpers.MainWindow.Clipboard is { } clipboard)
				await clipboard.SetTextAsync(string.Join(Environment.NewLine, selected.Select(s => s.Entry.Path)));
		});

		public ReactiveCommand<DatabaseEntryVM, Unit> BeginPathEditCommand => ReactiveCommand.Create<DatabaseEntryVM>(entry => {
			entry?.BeginPathEdit();
		});
	}
}
