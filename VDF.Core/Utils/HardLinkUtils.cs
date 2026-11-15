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

		[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16, EntryPoint = "CreateHardLinkW")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

		/// <summary>
		/// Creates a hard link at <paramref name="linkPath"/> referencing the same data as
		/// <paramref name="existingFilePath"/>. Throws <see cref="IOException"/> on failure
		/// (most commonly: the two paths are on different volumes, or the filesystem
		/// doesn't support hard links).
		/// </summary>
		public static void CreateHardLink(string linkPath, string existingFilePath) {
			if (CoreUtils.IsWindows) {
				if (!CreateHardLinkW(linkPath, existingFilePath, IntPtr.Zero))
					throw new IOException($"Failed to create hard link '{linkPath}' -> '{existingFilePath}' (Win32 error {Marshal.GetLastPInvokeError()}). Note: hard links require both paths to be on the same volume.");
				return;
			}
			int result = OperatingSystem.IsMacOS() ? LinkMac(existingFilePath, linkPath) : LinkLinux(existingFilePath, linkPath);
			if (result != 0) {
				int errno = Marshal.GetLastPInvokeError();
				throw new IOException($"Failed to create hard link '{linkPath}' -> '{existingFilePath}' ({Marshal.GetPInvokeErrorMessage(errno)}, errno {errno}). Note: hard links require both paths to be on the same filesystem.");
			}
		}

		// POSIX: link(2) makes the hard link, and two paths are one file when device and inode
		// number match. struct stat differs per OS, architecture and glibc version (glibc before
		// 2.33 does not even export a "stat" symbol), which is why Mono.Posix went through a native
		// helper library of its own, one without an arm64 build for macOS. Linux uses statx(2)
		// instead, whose struct is the same on every architecture (glibc 2.28+, musl 1.2.5+), and
		// macOS its 64-bit-inode stat, which is the same on x64 and arm64.
		const string LinuxLibc = "libc";
		const string MacLibc = "/usr/lib/libc.dylib";

		[LibraryImport(LinuxLibc, EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
		private static partial int LinkLinux(string existingPath, string newPath);

		[LibraryImport(MacLibc, EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
		private static partial int LinkMac(string existingPath, string newPath);

		[LibraryImport(LinuxLibc, EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
		private static partial int Statx(int dirFd, string path, int flags, uint mask, out StatxBuffer buffer);

		// x64 macOS keeps the old 32-bit-inode struct under the plain name; arm64 only has the 64-bit one.
		[LibraryImport(MacLibc, EntryPoint = "stat$INODE64", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
		private static partial int StatMacX64(string path, out DarwinStat buffer);

		[LibraryImport(MacLibc, EntryPoint = "stat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
		private static partial int StatMacArm64(string path, out DarwinStat buffer);

		const int AT_FDCWD = -100;
		const uint STATX_INO = 0x100;

		/// <summary>struct statx (linux/stat.h): 256 bytes on every architecture; device is always filled.</summary>
		[StructLayout(LayoutKind.Explicit, Size = 256)]
		struct StatxBuffer {
			[FieldOffset(0)] public uint Mask;
			[FieldOffset(32)] public ulong Ino;
			[FieldOffset(136)] public uint DevMajor;
			[FieldOffset(140)] public uint DevMinor;
		}

		/// <summary>macOS struct stat with 64-bit inodes (sys/stat.h): 144 bytes, sized up as headroom.</summary>
		[StructLayout(LayoutKind.Explicit, Size = 256)]
		struct DarwinStat {
			[FieldOffset(0)] public int Dev;
			[FieldOffset(8)] public ulong Ino;
		}

		/// <summary>glibc's makedev, so <see cref="TryGetFileIdPosix"/> reports the st_dev that stat(1) prints.</summary>
		static ulong MakeDev(uint major, uint minor) =>
			((ulong)(major & 0xfffff000u) << 32) | ((ulong)(major & 0xfffu) << 8) |
			((ulong)(minor & 0xffffff00u) << 12) | (minor & 0xffu);

		static bool posixFileIdUnavailableLogged;

		/// <summary>
		/// The device and inode number of <paramref name="path"/> (following symlinks, like stat), or
		/// false when the file cannot be read or the C library lacks the call.
		/// </summary>
		internal static bool TryGetFileIdPosix(string path, out (ulong Device, ulong Inode) id) {
			id = default;
			try {
				if (OperatingSystem.IsMacOS()) {
					DarwinStat st;
					int result = RuntimeInformation.ProcessArchitecture == Architecture.X64
						? StatMacX64(path, out st)
						: StatMacArm64(path, out st);
					if (result != 0)
						return false;
					id = ((uint)st.Dev, st.Ino);
					return true;
				}
				if (Statx(AT_FDCWD, path, 0, STATX_INO, out var stx) != 0 || (stx.Mask & STATX_INO) == 0)
					return false;
				id = (MakeDev(stx.DevMajor, stx.DevMinor), stx.Ino);
				return true;
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
				if (!posixFileIdUnavailableLogged) {
					posixFileIdUnavailableLogged = true;
					Logger.Instance.Warn($"Hard links cannot be recognized on this system, treating them as separate files: {ex.Message}");
				}
				return false;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct BY_HANDLE_FILE_INFORMATION {
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
		struct FILETIME {
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

		static bool AreSameFilePosix(string filepath, string otherFilepath) =>
			TryGetFileIdPosix(filepath, out var fileA) &&
			TryGetFileIdPosix(otherFilepath, out var fileB) &&
			fileA == fileB;

		static bool AreSameFileWindows(string filepath, string otherFilepath) {
			if (!TryGetFileIdWindows(filepath, out var fileA)) {
				return FallbackWindows(filepath, otherFilepath);
			}
			if (!TryGetFileIdWindows(otherFilepath, out var fileB)) {
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
				Logger.Instance.Warn($"Failed getting file id for file: {filepath}, reason: {ex.Message}");
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
				Logger.Instance.Warn($"Failed getting hard links of file: {filepath}, reason: {ex.Message}");
				return Array.Empty<string>();
			}
			finally {
				ArrayPool<char>.Shared.Return(buffer);
			}
		}
	}
}
