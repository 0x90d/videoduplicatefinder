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

using VDF.Core.Utils;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Utils {
	static class FileUtils {
		/// <summary>
		/// Copies file or folder to target destination and remain the folder structure
		/// </summary>
		/// <param name="pSource"></param>
		/// <param name="pDest"></param>
		/// <param name="pOverwriteDest"></param>
		/// <param name="pMove"></param>
		/// <param name="errors"></param>
		public static void CopyFile(IEnumerable<DuplicateItemVM> pSource, string pDest, bool pOverwriteDest, bool pMove, out int errors) {
			string destDirectory = Path.GetDirectoryName(pDest) ?? string.Empty;
			Directory.CreateDirectory(destDirectory);
			errors = 0;
			foreach (var s in pSource) {
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
					s.ItemInfo.Path = temppath;
				}
				catch (Exception e) {
					Logger.Instance.Info($"Failed to copy '{pSource}' to '{pDest}', reason: {e.Message}");
					errors++;
				}
			}

		}
	}
}
