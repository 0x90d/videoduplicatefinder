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

using System.Runtime.InteropServices;

namespace VDF.GUI.Utils {
	internal static partial class ShellUtils {
		/// <summary>
		/// Opens the parent folder in Explorer and selects the specified file
		/// </summary>
		/// <param name="filePath">The path of the file to show in Explorer</param>
		internal static void ShowInExplorer(string filePath) {
			IntPtr pidl = IntPtr.Zero;
			try {
				int result = SHParseDisplayName(filePath, IntPtr.Zero, out pidl, 0, out _);
				Marshal.ThrowExceptionForHR(result);

				result = SHOpenFolderAndSelectItems(pidl, 0, null, 0);
				Marshal.ThrowExceptionForHR(result);
			}
			finally {
				if (pidl != IntPtr.Zero)
					Marshal.FreeCoTaskMem(pidl);
			}
		}

		[LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
		private static partial int SHParseDisplayName(
			string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

		[LibraryImport("shell32.dll")]
		private static partial int SHOpenFolderAndSelectItems(
			IntPtr pidlFolder, uint cidl, [In] IntPtr[]? apidl, uint dwFlags);
	}
}
