// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using Avalonia.Platform.Storage;

namespace VDF.GUI.Utils {
	internal static class PickerDialogUtils {

		internal static async Task<string?> OpenFilePicker(FilePickerOpenOptions options) {
			var paths = await ApplicationHelpers.MainWindow.StorageProvider.OpenFilePickerAsync(options);

			if (paths != null &&
				paths.Count > 0 &&
				paths[0].TryGetUri(out Uri? uriPath))
				return uriPath.LocalPath;

			return null;
		}
		internal static async Task<string?> SaveFilePicker(FilePickerSaveOptions options) {
			var path = await ApplicationHelpers.MainWindow.StorageProvider.SaveFilePickerAsync(options);

			if (path != null && path.TryGetUri(out Uri? uriPath))
				return uriPath.LocalPath;

			return null;
		}
		internal static async Task<List<string>?> OpenDialogPicker(FolderPickerOpenOptions options) {
			var paths = await ApplicationHelpers.MainWindow.StorageProvider.OpenFolderPickerAsync(options);

			if (paths == null || paths.Count == 0)
				return null;

			List<string> results = new();
			foreach (var item in paths) {
				if (item.TryGetUri(out Uri? uriPath))
					results.Add(uriPath.LocalPath);
			}
			return results;
		}
	}
}
