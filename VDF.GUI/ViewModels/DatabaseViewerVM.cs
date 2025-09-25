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
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using Avalonia.Collections;
using Avalonia.Controls;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;

namespace VDF.GUI.ViewModels {
	internal class DatabaseViewerVM : ReactiveObject {
		public ObservableCollection<FileEntry> DatabaseFiles { get; } = new();
		private DatabaseWrapper DbWrapper;
		public DataGridCollectionView DatabaseFilesView { get; }
		readonly string TempDatabaseFile;
		static readonly JsonSerializerOptions serializerOptions = new() {
			IncludeFields = true,
		};
		readonly DataGrid GetDataGrid;

		public DatabaseViewerVM(DataGrid dataGrid) {
			GetDataGrid = dataGrid;
			TempDatabaseFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
			ScanEngine.ExportDataBaseToJson(TempDatabaseFile, serializerOptions);
			 DbWrapper = JsonSerializer.Deserialize<DatabaseWrapper>(File.ReadAllBytes(TempDatabaseFile), serializerOptions)!;
			DatabaseFiles = new ObservableCollection<FileEntry>(DbWrapper.Entries);
			DatabaseFilesView = new DataGridCollectionView(DatabaseFiles);
			DatabaseFilesView.Filter += TextFilter;
			GetDataGrid.BeginningEdit += GetDataGrid_BeginningEdit;
			GetDataGrid.CellEditEnded += GetDataGrid_CellEditEnded;
			GetDataGrid.RowEditEnded += GetDataGrid_RowEditEnded;
		}

		private void GetDataGrid_RowEditEnded(object? sender, DataGridRowEditEndedEventArgs e) => canDeleteRows = true;

		private void GetDataGrid_CellEditEnded(object? sender, DataGridCellEditEndedEventArgs e) => canDeleteRows = true;

		private void GetDataGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e) => canDeleteRows = false;

		bool canDeleteRows = true;

		bool TextFilter(object obj) {
			if (obj is not FileEntry data) return false;
			bool success = true;
			if (!string.IsNullOrEmpty(SearchText)) {
				success = data.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
			}
			return success;
		}

		string _SearchText = string.Empty;
		public string SearchText {
			get => _SearchText;
			set {
				if (value == _SearchText) return;
				_SearchText = value;
				this.RaisePropertyChanged(nameof(SearchText));
				DatabaseFilesView?.Refresh();
			}
		}
		public void Save() {
			DbWrapper.Entries = new HashSet<FileEntry>(DatabaseFiles);
			File.WriteAllBytes(TempDatabaseFile, JsonSerializer.SerializeToUtf8Bytes(DbWrapper, serializerOptions));
			ScanEngine.ImportDataBaseFromJson(TempDatabaseFile, serializerOptions);
			ScanEngine.SaveDatabase();
			try {
				File.Delete(TempDatabaseFile);
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed to delete temporarily database file '{TempDatabaseFile}', because of {e}");
			}
		}
		public ReactiveCommand<Unit, Unit> DeleteSelectedEntries => ReactiveCommand.Create(() => {
			if (!canDeleteRows) return;
			foreach (var item in GetDataGrid.SelectedItems.OfType<FileEntry>().ToList()) {
				DatabaseFiles.Remove(item);
			}
		});
	}
}
