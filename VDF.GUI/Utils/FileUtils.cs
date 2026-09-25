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

using System.Linq;
using VDF.Core.Utils;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Utils {
	static class FileUtils {
		/// <summary>
		/// Copies or moves the files into <paramref name="pDest"/>. Successful operations are
		/// reported through <paramref name="renames"/> (item + its new path) instead of mutating
		/// <see cref="DuplicateItemVM.ItemInfo"/> directly — this runs on a background thread and
		/// the Path property is bound to the UI, so the caller applies the renames on the UI thread.
		/// <paramref name="onProgress"/> gets the finished file count, the total, and the
		/// fraction of all bytes transferred so far (0..1), so a progress bar can move while a
		/// single large file is still being copied (#879).
		/// Returns the number of failed files.
		/// </summary>
		public static int CopyFile(IReadOnlyList<DuplicateItemVM> pSource, string pDest, bool pOverwriteDest, bool pMove,
				List<(DuplicateItemVM Item, string NewPath)> renames, Action<int, int, double>? onProgress = null) {
			Directory.CreateDirectory(pDest);
			int errors = 0;
			long totalBytes = 0;
			foreach (var s in pSource)
				totalBytes += Math.Max(0, s.ItemInfo.SizeLong);
			long doneBytes = 0;
			var throttle = System.Diagnostics.Stopwatch.StartNew();
			double Fraction(long bytes) => totalBytes > 0 ? Math.Min(1.0, (double)bytes / totalBytes) : 0;

			for (int i = 0; i < pSource.Count; i++) {
				var s = pSource[i];
				long fileStart = doneBytes;
				int finished = i;
				try {
					var name = Path.GetFileNameWithoutExtension(s.ItemInfo.Path);
					var ext = Path.GetExtension(s.ItemInfo.Path);
					string temppath = Path.Combine(pDest, name + ext);
					var counter = 0;
					while (File.Exists(temppath)) {
						temppath = Path.Combine(pDest, name + '_' + counter + ext);
						counter++;
					}

					TransferFile(s.ItemInfo.Path, temppath, pMove, pOverwriteDest, copied => {
						if (throttle.ElapsedMilliseconds < 100) return;
						throttle.Restart();
						onProgress?.Invoke(finished, pSource.Count, Fraction(fileStart + copied));
					});
					renames.Add((s, temppath));
				}
				catch (Exception e) {
					Logger.Instance.Error($"Failed to {(pMove ? "move" : "copy")} '{s.ItemInfo.Path}' to '{pDest}', reason: {e.Message}");
					errors++;
				}
				doneBytes = fileStart + Math.Max(0, s.ItemInfo.SizeLong);
				onProgress?.Invoke(i + 1, pSource.Count, pSource.Count == i + 1 ? 1.0 : Fraction(doneBytes));
			}
			return errors;
		}

		/// <summary>
		/// Moves or copies one file. A move within one volume stays a rename (instant, nothing
		/// to report). Everything else is copied in chunks so <paramref name="onBytesCopied"/>
		/// can report progress: File.Move across volumes and File.Copy are single calls that
		/// block until the whole file is done, which left the busy bar with nothing to show
		/// but an animation (#879). Timestamps and attributes are carried over; a failed copy
		/// removes its partial target and leaves the source untouched, and a move deletes the
		/// source only after the copy is complete.
		/// </summary>
		internal static void TransferFile(string source, string target, bool move, bool overwrite, Action<long>? onBytesCopied,
				Func<string, string, bool>? isSameVolume = null) {
			if (move && (isSameVolume ?? IsSameVolume)(source, target)) {
				File.Move(source, target, overwrite);
				return;
			}

			var info = new FileInfo(source);
			try {
				using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
				using (var output = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 1)) {
					if (info.Length > 0)
						output.SetLength(info.Length);
					byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1 << 20);
					try {
						long copied = 0;
						int read;
						while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
							output.Write(buffer, 0, read);
							copied += read;
							onBytesCopied?.Invoke(copied);
						}
						output.SetLength(copied);
					}
					finally {
						System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
					}
				}
				File.SetCreationTimeUtc(target, info.CreationTimeUtc);
				File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
				File.SetAttributes(target, info.Attributes & ~FileAttributes.ReadOnly);
			}
			catch {
				try { File.Delete(target); } catch { }
				throw;
			}

			if (move) {
				if ((info.Attributes & FileAttributes.ReadOnly) != 0)
					File.SetAttributes(source, info.Attributes & ~FileAttributes.ReadOnly);
				File.Delete(source);
			}
			if ((info.Attributes & FileAttributes.ReadOnly) != 0)
				File.SetAttributes(target, info.Attributes);
		}

		/// <summary>
		/// Whether a move from <paramref name="source"/> to <paramref name="target"/> is a
		/// rename. Windows: same path root (drive letter or share). Elsewhere: same mount
		/// point, the longest one that contains the path. When in doubt the answer is yes,
		/// which keeps the plain File.Move (correct, only without byte progress).
		/// </summary>
		internal static bool IsSameVolume(string source, string target) {
			try {
				string fullSource = Path.GetFullPath(source);
				string fullTarget = Path.GetFullPath(target);
				if (OperatingSystem.IsWindows())
					return string.Equals(Path.GetPathRoot(fullSource), Path.GetPathRoot(fullTarget), StringComparison.OrdinalIgnoreCase);
				var mounts = DriveInfo.GetDrives().Select(d => d.RootDirectory.FullName).ToList();
				return string.Equals(MountPointOf(fullSource, mounts), MountPointOf(fullTarget, mounts), StringComparison.Ordinal);
			}
			catch {
				return true;
			}
		}

		internal static string? MountPointOf(string fullPath, IEnumerable<string> mountPoints) {
			string? best = null;
			foreach (string mount in mountPoints) {
				string withSlash = mount.EndsWith('/') ? mount : mount + "/";
				bool contains = fullPath == mount || fullPath.StartsWith(withSlash, StringComparison.Ordinal);
				if (contains && (best == null || mount.Length > best.Length))
					best = mount;
			}
			return best;
		}
	}
}
