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

using VDF.Core.Utils;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Utils {
	static class FileUtils {
		/// <summary>
		/// Copies or moves the files into <paramref name="pDest"/>. Successful operations are
		/// reported through <paramref name="renames"/> (item + its new path) instead of mutating
		/// <see cref="DuplicateItemVM.ItemInfo"/> directly — this runs on a background thread and
		/// the Path property is bound to the UI, so the caller applies the renames on the UI thread.
		/// Returns the number of failed files.
		/// </summary>
		public static int CopyFile(IReadOnlyList<DuplicateItemVM> pSource, string pDest, bool pOverwriteDest, bool pMove,
				List<(DuplicateItemVM Item, string NewPath)> renames, Action<int, int>? onProgress = null) {
			Directory.CreateDirectory(pDest);
			int errors = 0;
			for (int i = 0; i < pSource.Count; i++) {
				var s = pSource[i];
				try {
					var name = Path.GetFileNameWithoutExtension(s.ItemInfo.Path);
					var ext = Path.GetExtension(s.ItemInfo.Path);
					string temppath = Path.Combine(pDest, name + ext);
					var counter = 0;
					while (File.Exists(temppath)) {
						temppath = Path.Combine(pDest, name + '_' + counter + ext);
						counter++;
					}

					if (pMove)
						File.Move(s.ItemInfo.Path, temppath, pOverwriteDest);
					else
						File.Copy(s.ItemInfo.Path, temppath, pOverwriteDest);
					renames.Add((s, temppath));
				}
				catch (Exception e) {
					Logger.Instance.Info($"Failed to {(pMove ? "move" : "copy")} '{s.ItemInfo.Path}' to '{pDest}', reason: {e.Message}");
					errors++;
				}
				onProgress?.Invoke(i + 1, pSource.Count);
			}
			return errors;
		}
	}
}
