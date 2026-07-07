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

using System.Linq;
using System.Reactive;
using System.Text.Json;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;

namespace VDF.GUI.ViewModels {
	/// <summary>One database entry row: path is editable (writes through to the entry),
	/// the exclude flag toggles, everything else is display-only.</summary>
	internal sealed class DatabaseEntryVM {
		public DatabaseEntryVM(FileEntry entry) => Entry = entry;
		public FileEntry Entry { get; }

		public string Path {
			get => Entry.Path;
			set {
				if (!string.IsNullOrWhiteSpace(value))
					Entry.Path = value;
			}
		}
		public bool IsManuallyExcluded {
			get => Entry.IsManuallyExcluded;
			// FileEntry's property setter is protected; the flags field is public.
			set => Entry.Flags.Set(EntryFlags.ManuallyExcluded, value);
		}
		public string SizeText => Entry.FileSize.BytesToString();
		public string DateCreatedText => Entry.DateCreated.ToLocalTime().ToString("g");
		public string FlagsText {
			get {
				var flags = new List<string>();
				if (Entry.HasMetadataError) flags.Add("metadata error");
				if (Entry.HasThubmanilError) flags.Add("thumbnail error");
				if (Entry.IsTooDark) flags.Add("too dark");
				return string.Join(", ", flags);
			}
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
			ApplyFilter();
		}

		IReadOnlyList<DatabaseEntryVM> _DatabaseFilesView = Array.Empty<DatabaseEntryVM>();
		public IReadOnlyList<DatabaseEntryVM> DatabaseFilesView {
			get => _DatabaseFilesView;
			private set => this.RaiseAndSetIfChanged(ref _DatabaseFilesView, value);
		}

		public string EntryCountText => allEntries.Count == DatabaseFilesView.Count
			? $"{allEntries.Count:N0}"
			: $"{DatabaseFilesView.Count:N0} / {allEntries.Count:N0}";

		void ApplyFilter() {
			DatabaseFilesView = string.IsNullOrEmpty(SearchText)
				? allEntries.ToList()
				: allEntries.Where(e => e.Entry.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
			this.RaisePropertyChanged(nameof(EntryCountText));
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

		// Wired by the view: returns the list box's current selection.
		internal Func<IEnumerable<DatabaseEntryVM>>? SelectionProvider;

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
			var selected = SelectionProvider?.Invoke().ToList();
			if (selected == null || selected.Count == 0) return;
			foreach (var item in selected)
				allEntries.Remove(item);
			ApplyFilter();
		});
	}
}
