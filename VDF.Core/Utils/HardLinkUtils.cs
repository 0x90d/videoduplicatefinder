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

using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

		const IntPtr INVALID_HANDLE_VALUE = -1;
		const int ERROR_MORE_DATA = 234;

		/// <summary>
		//// Returns enumeration of hard links for the given *file* as full file paths
		/// </summary>
		public static IEnumerable<string> GetHardLinks(string filepath) {
			if (CoreUtils.IsWindows)
				return GetHardLinksWindows(filepath);
			else
				return GetHardLinksPosix(filepath);
		}

		static IEnumerable<string> GetHardLinksPosix(string filepath) {
			const int timeout = 30_000;
			int success = Mono.Unix.Native.Syscall.stat(filepath, out var stat);
			if (success == 0 && stat.st_nlink <= 1)
				return Array.Empty<string>();

			Process process = new() {
				StartInfo = {
					FileName = "find",
					Arguments = $" {Path.GetPathRoot(filepath)} -samefile \"{filepath}\"",
					RedirectStandardOutput = true,
					/*
					 * Do not redirect error output, this makes the process run
					 * much longer due to all the 'Permission denied' errors
					 */
					WindowStyle = ProcessWindowStyle.Hidden,
					UseShellExecute = false
				}
			};
			try {
				process.Start();
				process.WaitForExit(timeout);
				if (!process.HasExited) {
					process.Kill();
					throw new TimeoutException("timed out");
				}
				List<string> files = new(process.StandardOutput.ReadToEnd().Split(Environment.NewLine));
				return files;
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Failed getting hard links of file: {filepath}, reason: {ex.Message}");
				return Array.Empty<string>();
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
					links.Add(string.Concat(volume, new string(buffer.AsSpan().TrimEnd('\0'))));
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
						links.Add(string.Concat(volume, new string(buffer.AsSpan().TrimEnd('\0'))));
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
