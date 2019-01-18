using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DuplicateFinderEngine {
	public static class FileHelper {

		public static readonly string[] ImageExtensions = new[] { "jpg", "jpeg", "png", "gif", "bmp", "tiff" };
		public static readonly string[] VideoExtensions = new[] { "mp4", "wmv", "avi", "mkv", "flv", "mov", "mpg", "mpeg", "m4v", "asf", "f4v", "webm", "divx", "m2t", "m2ts", "vob", "ts" };
		public static readonly string[] AllExtensions = VideoExtensions.Concat(ImageExtensions).ToArray();

		// '' <summary>
		// '' This method starts at the specified directory.
		// '' It traverses all subdirectories.
		// '' It returns a List of those directories.
		// '' </summary>
		public static List<string> GetFilesRecursive(string initial, bool ignoreReadonly, bool recursive, bool includeImages, List<string> excludeFolders) {
			try {
				var files = Directory.EnumerateFiles(initial).Where(f => (includeImages ? AllExtensions : VideoExtensions).Any(x => f.EndsWith(x, StringComparison.OrdinalIgnoreCase)));
				if (recursive)
					files = files.Concat(Directory.EnumerateDirectories(initial)
						.Where(d => !excludeFolders.Any(x => d.Equals(x, StringComparison.OrdinalIgnoreCase)))
						.Where(d => !ignoreReadonly || (new DirectoryInfo(d).Attributes & FileAttributes.ReadOnly) == 0)
						.SelectMany(d => GetFilesRecursive(d, ignoreReadonly, recursive, includeImages, excludeFolders)));
				return files.ToList();
			}
			catch (Exception ex) {
				// ReSharper disable once RedundantStringFormatCall
				Logger.Instance.Info(string.Format(Properties.Resources.SkippedErrorReason, initial, ex.Message));
				return new List<string>();
			}
		}


		/// <summary>
		/// Copies file or folder to target destination and remain the folder structure
		/// </summary>
		/// <param name="pSource"></param>
		/// <param name="pDest"></param>
		/// <param name="pOverwriteDest"></param>
		/// <param name="pMove"></param>
		/// <param name="errors"></param>
		public static void CopyFile(IEnumerable<string> pSource, string pDest, bool pOverwriteDest, bool pMove, out int errors) {
			string destDirectory = Path.GetDirectoryName(pDest);
			Directory.CreateDirectory(destDirectory);
			errors = 0;
			foreach (var s in pSource) {
				try {
					var name = Path.GetFileNameWithoutExtension(s);
					var ext = Path.GetExtension(s);
					string temppath = Path.Combine(pDest, name + ext);
					var counter = 0;
					while (File.Exists(temppath)) {
						temppath = Path.Combine(pDest, name + '_' + counter + ext);
						counter++;
					}

					if (pMove) {
						if (pOverwriteDest && File.Exists(temppath)) {
							File.Copy(s, temppath, true);
							File.Delete(s);
						}
						else
							File.Move(s, temppath);
					}
					else
						File.Copy(s, temppath, pOverwriteDest);
				}
				catch (Exception e) {
					Logger.Instance.Info(string.Format(Properties.Resources.FailedToCopyToReason, pSource, pDest, e.Message));
					errors++;
				}
			}

		}
	}
}

