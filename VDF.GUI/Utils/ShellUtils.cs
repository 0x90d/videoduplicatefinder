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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VDF.GUI.Utils;

static partial class ShellUtils {
	[SupportedOSPlatform("windows")]
	public static void ShowInExplorer(string filePath) {
		IntPtr pidlFolder = IntPtr.Zero;
		IntPtr pidlFile = IntPtr.Zero;
		try {
			int hr = SHParseDisplayName(Path.GetDirectoryName(filePath)!, IntPtr.Zero, out pidlFolder, 0, out _);
			if (hr != 0)
				throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException($"SHParseDisplayName failed for folder (0x{hr:X8})");

			hr = SHParseDisplayName(filePath, IntPtr.Zero, out pidlFile, 0, out _);
			if (hr != 0)
				throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException($"SHParseDisplayName failed for file (0x{hr:X8})");

			hr = SHOpenFolderAndSelectItems(pidlFolder, 1, [pidlFile], 0);
			if (hr != 0)
				throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException($"SHOpenFolderAndSelectItems failed (0x{hr:X8})");
		}
		finally {
			if (pidlFolder != IntPtr.Zero) Marshal.FreeCoTaskMem(pidlFolder);
			if (pidlFile != IntPtr.Zero) Marshal.FreeCoTaskMem(pidlFile);
		}
	}

	[LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int SHParseDisplayName(
		string pszName,
		IntPtr pbc,
		out IntPtr ppidl,
		uint sfgaoIn,
		out uint psfgaoOut);

	[LibraryImport("shell32.dll")]
	private static partial int SHOpenFolderAndSelectItems(
		IntPtr pidlFolder,
		uint cidl,
		[MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
		uint dwFlags);
}
