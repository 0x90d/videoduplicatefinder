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

using System.Linq;
using System.Runtime.InteropServices;

namespace VDF.Core.Utils {
	public static class FileUtils {
		public static readonly string[] ImageExtensions = {
			".jpg",
			".jpeg",
			".png",
			".gif",
			".bmp",
			".tiff"};
		static readonly string[] VideoExtensions = {
			".mp4",
			".wmv",
			".avi",
			".mkv",
			".flv",
			".mov",
			".mpg",
			".mpeg",
			".m4v",
			".asf",
			".f4v",
			".webm",
			".divx",
			".m2t",
			".m2ts",
			".vob",
			".ts"
		};
		static readonly string[] AllExtensions = VideoExtensions.Concat(ImageExtensions).ToArray();

		/// <summary>
		/// Gets a list of files that meet the criteria of the arguments
		/// </summary>
		/// <param name="initial"></param>
		/// <param name="ignoreReadonly"></param>
		/// <param name="ignoreHardLinks"></param>
		/// <param name="recursive"></param>
		/// <param name="includeImages"></param>
		/// <param name="excludeFolders"></param>
		/// <param name="includeFileTypes"></param>
		/// <param name="minimumFileSize"></param>
		/// <returns></returns>
		public static IEnumerable<string> GetFiles(string initial, bool ignoreReadonly, bool ignoreHardLinks, bool recursive,
			bool includeImages, List<string> excludeFolders, List<string> includeFileTypes, long minimumFileSize) {
			var enumerationOptions = new EnumerationOptions {
				IgnoreInaccessible = true,
				AttributesToSkip = FileAttributes.System
			};

			if (ignoreReadonly)
				enumerationOptions.AttributesToSkip |= FileAttributes.ReadOnly;
			if (ignoreHardLinks)
				enumerationOptions.AttributesToSkip |= FileAttributes.ReparsePoint;

			if (includeFileTypes.Any()) {
				includeFileTypes = includeImages ? AllExtensions.Intersect(includeFileTypes).ToList() : VideoExtensions.Intersect(includeFileTypes).ToList();
			}
			else if (includeImages) {
				includeFileTypes = AllExtensions.ToList();
			}
			else {
				includeFileTypes = VideoExtensions.ToList();
			}

			var pending = new Queue<string>();
			pending.Enqueue(initial);
			while (pending.Count > 0) {
				initial = pending.Dequeue();
				string[] tmp;
				try {
					tmp = Directory.GetFiles(initial, "*", enumerationOptions)
						.Where(f => includeFileTypes.ToArray()
							.Any(x => {
								var fileInfo = new FileInfo(f);
								return string.Equals(fileInfo.Extension, x) && (!(minimumFileSize > 0) ||
									fileInfo.Length / 1048576 > minimumFileSize);
							}))
						.ToArray();
				}
				catch (DirectoryNotFoundException) {
					continue;
				}
				catch (UnauthorizedAccessException) {
					continue;
				}

				foreach (var t in tmp) {
					yield return t;
				}

				if (!recursive) continue;
				{
					tmp = Directory.GetDirectories(initial);

					foreach (var t in tmp) {
						if (!excludeFolders.Contains(t))
							pending.Enqueue(t);
					}
				}
			}
		}

		/// <summary>
		/// Get safe path on all systems ignoring slashes
		/// </summary>
		/// <param name="path1"></param>
		/// <param name="path2"></param>
		/// <returns></returns>
		public static string SafePathCombine(string path1, string path2) {
			if (!Path.IsPathRooted(path2))
				Path.Combine(path1, path2);

			path2 = path2.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return Path.Combine(path1, path2);
		}

		/// <summary>
		/// Possible flags for the SHFileOperation method.
		/// </summary>
		[Flags]
		public enum FileOperationFlags : ushort {
			/// <summary>
			/// Do not show a dialog during the process
			/// </summary>
			FOF_SILENT = 0x0004,
			/// <summary>
			/// Do not ask the user to confirm selection
			/// </summary>
			FOF_NOCONFIRMATION = 0x0010,
			/// <summary>
			/// Delete the file to the recycle bin.  (Required flag to send a file to the bin
			/// </summary>
			FOF_ALLOWUNDO = 0x0040,
			/// <summary>
			/// Do not show the names of the files or folders that are being recycled.
			/// </summary>
			FOF_SIMPLEPROGRESS = 0x0100,
			/// <summary>
			/// Surpress errors, if any occur during the process.
			/// </summary>
			FOF_NOERRORUI = 0x0400,
			/// <summary>
			/// Warn if files are too big to fit in the recycle bin and will need
			/// to be deleted completely.
			/// </summary>
			FOF_WANTNUKEWARNING = 0x4000,
		}

		/// <summary>
		/// File Operation Function Type for SHFileOperation
		/// </summary>
		public enum FileOperationType : uint {
			/// <summary>
			/// Move the objects
			/// </summary>
			FO_MOVE = 0x0001,
			/// <summary>
			/// Copy the objects
			/// </summary>
			FO_COPY = 0x0002,
			/// <summary>
			/// Delete (or recycle) the objects
			/// </summary>
			FO_DELETE = 0x0003,
			/// <summary>
			/// Rename the object(s)
			/// </summary>
			FO_RENAME = 0x0004,
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct SHFILEOPSTRUCT {

			public IntPtr hwnd;
			[MarshalAs(UnmanagedType.U4)]
			public FileOperationType wFunc;
			public string pFrom;
			public string pTo;
			public FileOperationFlags fFlags;
			[MarshalAs(UnmanagedType.Bool)]
			public bool fAnyOperationsAborted;
			public IntPtr hNameMappings;
			public string lpszProgressTitle;
		}

		[DllImport("shell32.dll", CharSet = CharSet.Auto)]
		public static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);
	}
}
