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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;
using ReactiveUI;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public sealed class CustomSelectionVM : ReactiveObject {
		[JsonIgnore]
		readonly CustomSelectionView? host;
		public CustomSelectionVM(CustomSelectionView customSelectionView) {
			host = customSelectionView;
		}
		CustomSelectionData _Data = new();
		public CustomSelectionData Data {
			get => _Data;
			set => this.RaiseAndSetIfChanged(ref _Data, value);
		}

		[JsonIgnore]
		public ReactiveCommand<Unit, Unit> SelectCommand => ReactiveCommand.Create(() => {
			ApplicationHelpers.MainWindowDataContext.RunCustomSelection(Data);
		});
		[JsonIgnore]
		public ReactiveCommand<Unit, Unit> CancelCommand => ReactiveCommand.Create(() => {
			host?.Close(MessageBoxButtons.Cancel);
		});
		[JsonIgnore]
		public ReactiveCommand<ListBox, Action> AddFilePathContainsTextToListCommand => ReactiveCommand.CreateFromTask<ListBox, Action>(async lbox => {
			var result = await InputBoxService.Show("New Entry");
			if (string.IsNullOrEmpty(result)) return null!;
			if (!Data.PathContains.Contains(result))
				Data.PathContains.Add(result);
			return null!;
		});
		[JsonIgnore]
		public ReactiveCommand<ListBox, Action> RemoveFilePathContainsTextFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems?.Count > 0)
				Data.PathContains.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});
		[JsonIgnore]
		public ReactiveCommand<ListBox, Action> AddFilePathNotContainsTextToListCommand => ReactiveCommand.CreateFromTask<ListBox, Action>(async lbox => {
			var result = await InputBoxService.Show("New Entry");
			if (string.IsNullOrEmpty(result)) return null!;
			if (!Data.PathNotContains.Contains(result))
				Data.PathNotContains.Add(result);
			return null!;
		});
		[JsonIgnore]
		public ReactiveCommand<ListBox, Action> RemoveFilePathNotContainsTextFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems?.Count > 0)
				Data.PathNotContains.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});
		[JsonIgnore]
		public ReactiveCommand<Unit, Unit> SaveCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				DefaultExtension = ".vdfselection",
				FileTypeChoices = new FilePickerFileType[] {
					 new FilePickerFileType("Selection File") { Patterns = new string[] { "*.vdfselection" }}}
			});
			if (string.IsNullOrEmpty(result)) return;

			try {
				File.WriteAllText(result, JsonSerializer.Serialize(Data));
			}
			catch (Exception ex) {
				await MessageBoxService.Show($"Saving to file has failed: {ex.Message}");
			}
		});
		[JsonIgnore]
		public ReactiveCommand<Unit, Unit> LoadCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = new FilePickerFileType[] {
					 new FilePickerFileType("Selection File") { Patterns = new string[] { "*.vdfselection" }}}
			});
			if (string.IsNullOrEmpty(result)) return;

			try {
				Data = JsonSerializer.Deserialize<CustomSelectionData>(File.ReadAllText(result))!;

			}
			catch (Exception ex) {
				await MessageBoxService.Show($"Loading from file has failed: {ex.Message}");
			}
		});

	}
}
