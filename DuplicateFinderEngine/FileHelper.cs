using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DuplicateFinderEngine {
	public static class FileHelper {

		public static readonly List<string> ImageExtensions = new List<string>() {
			".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff"
		};

		// '' <summary>
		// '' This method starts at the specified directory.
		// '' It traverses all subdirectories.
		// '' It returns a List of those directories.
		// '' </summary>
		public static List<string> GetFilesRecursive(string initial, bool ignoreReadonly, bool recursive, bool includeImages, List<string> excludeFolders) {
			var result = new List<string>();
			var stack = new System.Collections.Concurrent.ConcurrentStack<string>();
			stack.Push(initial);
			while (stack.Count > 0) {
				stack.TryPop(out var dir);
				try {
					var skip = false;
					var DirInfo = new DirectoryInfo(dir);

					if (DirInfo.Attributes.HasFlag(FileAttributes.ReadOnly) && ignoreReadonly) {
						Logger.Instance.Info(string.Format(Properties.Resources.SkippedReadonly, dir));
						skip = true;
					}

					for (var i = 0; i < excludeFolders.Count; i++) {
						if (!dir.Contains(excludeFolders[i])) continue;
						Logger.Instance.Info(string.Format(Properties.Resources.SkippedBlacklist, dir));
						skip = true;
						break;
					}

					if (!skip) {
						result.AddRange(Directory.GetFiles(dir, "*.avi"));
						result.AddRange(Directory.GetFiles(dir, "*.mkv"));
						result.AddRange(Directory.GetFiles(dir, "*.flv"));
						result.AddRange(Directory.GetFiles(dir, "*.mov"));
						result.AddRange(Directory.GetFiles(dir, "*.mpg"));
						result.AddRange(Directory.GetFiles(dir, "*.mpeg"));
						result.AddRange(Directory.GetFiles(dir, "*.wmv"));
						result.AddRange(Directory.GetFiles(dir, "*.mp4"));
						result.AddRange(Directory.GetFiles(dir, "*.m4v"));
						result.AddRange(Directory.GetFiles(dir, "*.asf"));
						result.AddRange(Directory.GetFiles(dir, "*.f4v"));
						result.AddRange(Directory.GetFiles(dir, "*.webm"));
						result.AddRange(Directory.GetFiles(dir, "*.divx"));
						result.AddRange(Directory.GetFiles(dir, "*.m2t"));
						result.AddRange(Directory.GetFiles(dir, "*.m2ts"));
						result.AddRange(Directory.GetFiles(dir, "*.vob"));
						if (includeImages)
							foreach (var s in ImageExtensions)
								result.AddRange(Directory.GetFiles(dir, $"*{s}"));
					}

					if (!skip && recursive) {
						Parallel.ForEach(
						   Directory.GetDirectories(dir), directoryname => stack.Push(directoryname)
						   );
					}

				}
				catch (Exception ex) {
					// ReSharper disable once RedundantStringFormatCall
					System.Diagnostics.Trace.TraceError(string.Format(Properties.Resources.SkippedErrorReason, dir, ex.Message));
				}

			}

			return result;
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

