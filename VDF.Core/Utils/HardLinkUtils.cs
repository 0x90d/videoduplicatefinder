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

using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VDF.Core.Utils {
	/// <summary>
	/// Credits to David-Maisonave for his original windows implementation
	/// </summary>
	internal static partial class HardLinkUtils {
		[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		private static partial IntPtr FindFirstFileNameW(string lpFileName, uint dwFlags, ref int StringLength, char[] LinkName);

		[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool FindNextFileNameW(IntPtr hFindStream, ref int StringLength, char[] LinkName);

		[LibraryImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool FindClose(IntPtr hFindFile);

		[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetVolumePathNameW(string lpszFileName, [Out] char[] lpszVolumePathName, int cchBufferLength);


		[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
		private static partial SafeFileHandle CreateFileW(
			string lpFileName,
			uint dwDesiredAccess,
			FileShare dwShareMode,
			IntPtr lpSecurityAttributes,
			FileMode dwCreationDisposition,
			uint dwFlagsAndAttributes,
			IntPtr hTemplateFile);

		[LibraryImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

		[StructLayout(LayoutKind.Sequential)]
		private struct BY_HANDLE_FILE_INFORMATION {
			public uint FileAttributes;
			public FILETIME CreationTime;
			public FILETIME LastAccessTime;
			public FILETIME LastWriteTime;
			public uint VolumeSerialNumber;
			public uint FileSizeHigh;
			public uint FileSizeLow;
			public uint NumberOfLinks;
			public uint FileIndexHigh;
			public uint FileIndexLow;
		}
		[StructLayout(LayoutKind.Sequential)]
		private struct FILETIME {
			public uint dwLowDateTime;
			public uint dwHighDateTime;
		}

		const IntPtr INVALID_HANDLE_VALUE = -1;
		const int ERROR_MORE_DATA = 234;
		const uint FILE_READ_ATTRIBUTES = 0x0080;
		const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

		public static bool AreSameFile(string filepath, string otherFilepath) {
			if (CoreUtils.IsWindows)
				return AreSameFileWindows(filepath, otherFilepath);
			return AreSameFilePosix(filepath, otherFilepath);
		}

		static bool AreSameFilePosix(string filepath, string otherFilepath) {
			if (Mono.Unix.Native.Syscall.stat(filepath, out var statA) != 0)
				return false;
			if (Mono.Unix.Native.Syscall.stat(otherFilepath, out var statB) != 0)
				return false;
			return statA.st_ino == statB.st_ino && statA.st_dev == statB.st_dev;
		}

		static bool AreSameFileWindows(string filepath, string otherFilepath) {
			if (!TryGetFileIdWindows(filepath, out var fileA)) {
				return FallbackWindows(filepath, otherFilepath);
			}
			if ( !TryGetFileIdWindows(otherFilepath, out var fileB)) {
				return FallbackWindows(filepath, otherFilepath);
			}

			// Heuristic: some providers may return zeros; treat as unknown
			if ((fileA.VolumeSerial, fileA.FileIndexHigh, fileA.FileIndexLow) == (0u, 0u, 0u))
				return FallbackWindows(filepath, otherFilepath);
			if ((fileB.VolumeSerial, fileB.FileIndexHigh, fileB.FileIndexLow) == (0u, 0u, 0u))
				return FallbackWindows(filepath, otherFilepath);

			return fileA.Equals(fileB);
		}
		static bool FallbackWindows(string filepath, string otherFilepath) {
			foreach (var link in GetHardLinksWindows(filepath))
				if (otherFilepath == link) {
					return true;
				}
			return false;
		}

		static bool TryGetFileIdWindows(string filepath, out (uint VolumeSerial, uint FileIndexHigh, uint FileIndexLow) fileId) {
			fileId = default;
			try {
				using SafeFileHandle handle = CreateFileW(
					filepath,
					FILE_READ_ATTRIBUTES,
					FileShare.ReadWrite | FileShare.Delete,
					IntPtr.Zero,
					FileMode.Open,
					FILE_FLAG_BACKUP_SEMANTICS,
					IntPtr.Zero);
				if (handle.IsInvalid)
					return false;
				if (!GetFileInformationByHandle(handle, out var info))
					return false;
				fileId = (info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
				return true;
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Failed getting file id for file: {filepath}, reason: {ex.Message}");
				return false;
			}
		}

		static IEnumerable<string> GetHardLinksWindows(string filepath) {
			char[] buffer = ArrayPool<char>.Shared.Rent(512);
			try {
				int stringLength = buffer.Length;
				buffer.AsSpan().Clear();

				if (!GetVolumePathNameW(filepath, buffer, stringLength))
					return Array.Empty<string>();

				Span<char> volume = buffer.AsSpan().TrimEnd('\0').TrimEnd('\\');
				List<string> links = new();

				while (true) {
					buffer.AsSpan().Clear();
					IntPtr findHandle = FindFirstFileNameW(filepath, 0, ref stringLength, buffer);
					if (findHandle == INVALID_HANDLE_VALUE) {
						if (Marshal.GetLastPInvokeError() == ERROR_MORE_DATA) {
							ArrayPool<char>.Shared.Return(buffer);
							buffer = ArrayPool<char>.Shared.Rent(stringLength);
							continue;
						}
						else {
							break;
						}
					}
					links.Add(string.Concat(volume, buffer.AsSpan().TrimEnd('\0')));
					while (true) {
						buffer.AsSpan().Clear();
						bool success = FindNextFileNameW(findHandle, ref stringLength, buffer);

						if (!success) {
							if (Marshal.GetLastPInvokeError() == ERROR_MORE_DATA) {
								ArrayPool<char>.Shared.Return(buffer);
								buffer = ArrayPool<char>.Shared.Rent(stringLength);
								continue;
							}
							else {
								FindClose(findHandle);
								break;
							}
						}
						links.Add(string.Concat(volume, buffer.AsSpan().TrimEnd('\0')));
					}
					break;
				}

				return links;
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Failed getting hard links of file: {filepath}, reason: {ex.Message}");
				return Array.Empty<string>();
			}
			finally {
				ArrayPool<char>.Shared.Return(buffer);
			}
		}
	}
}
