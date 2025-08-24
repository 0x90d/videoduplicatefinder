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

using Avalonia.Platform.Storage;

namespace VDF.GUI.Utils {
	internal static class PickerDialogUtils {

		private static string GetLocalPath(Uri uriPath) {
			if (uriPath.IsFile) {
				// The `LocalPath` member of the `Uri` object we got
				// from `TryGetUri()` might be wrong if the original
				// path contains the '#' character, or if escape
				// sequences (e.g. '%61') occur in the path.
				//
				// Thus, it's better to use the `OriginalString` member
				// instead, which contains the correct data on both
				// Linux and Windows.  However, the `file://` scheme
				// might be prefixed (e.g. on Linux), so it needs to be
				// removed if present.
				// See https://github.com/0x90d/videoduplicatefinder/pull/400
				string scheme = uriPath.Scheme + Uri.SchemeDelimiter;
				if (uriPath.OriginalString.StartsWith(scheme)) {
					return uriPath.OriginalString.Substring(scheme.Length);
				}
				else {
					return uriPath.OriginalString;
				}
			}
			return uriPath.LocalPath;
		}

		internal static async Task<string?> OpenFilePicker(FilePickerOpenOptions options) {
			var paths = await ApplicationHelpers.MainWindow.StorageProvider.OpenFilePickerAsync(options);

			if (paths != null &&
				paths.Count > 0 &&
				paths[0].TryGetLocalPath() is string uriPath)
				return uriPath;

			return null;
		}
		internal static async Task<string?> SaveFilePicker(FilePickerSaveOptions options) {
			var path = await ApplicationHelpers.MainWindow.StorageProvider.SaveFilePickerAsync(options);

			if (path != null && path.TryGetLocalPath() is string uriPath)
				return uriPath;

			return null;
		}
		internal static async Task<List<string>?> OpenDialogPicker(FolderPickerOpenOptions options) {
			var paths = await ApplicationHelpers.MainWindow.StorageProvider.OpenFolderPickerAsync(options);

			if (paths == null || paths.Count == 0)
				return null;

			List<string> results = new();
			foreach (var item in paths) {
				if (item.TryGetLocalPath() is string uriPath)
					results.Add(uriPath);
			}
			return results;
		}
	}
}
