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

using System.Threading;

namespace VDF.GUI.Utils {
	/// <summary>Progressive per-folder statistics for the Setup screen.</summary>
	internal sealed record FolderCountProgress(int FileCount, long TotalBytes, bool Completed, bool Failed = false);

	/// <summary>
	/// Background media-file counting for the Setup screen's folder list. Counts are
	/// purely informational and must NEVER block scanning (locked design decision 7):
	/// walks run on background threads, report throttled progress, are cancelable, and
	/// duplicate requests for a folder already being counted are ignored. Network
	/// locations are not walked unless the caller explicitly asks (opt-in "count now").
	/// </summary>
	internal sealed class FolderCountingService {
		readonly Dictionary<string, CancellationTokenSource> active = new(StringComparer.OrdinalIgnoreCase);
		readonly object gate = new();
		readonly TimeSpan progressInterval;

		internal FolderCountingService(TimeSpan? progressInterval = null) =>
			this.progressInterval = progressInterval ?? TimeSpan.FromMilliseconds(400);

		/// <summary>
		/// True for locations that may be slow/metered to enumerate: UNC paths and
		/// drives Windows reports as network drives.
		/// </summary>
		internal static bool IsNetworkPath(string path) {
			if (string.IsNullOrWhiteSpace(path)) return false;
			if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
				return true;
			try {
				string? root = Path.GetPathRoot(Path.GetFullPath(path));
				if (string.IsNullOrEmpty(root)) return false;
				return new DriveInfo(root).DriveType == DriveType.Network;
			}
			catch (Exception) {
				return false;
			}
		}

		/// <summary>
		/// Starts counting media files under <paramref name="folderPath"/>. Progress is
		/// raised on a worker thread (marshal in the callback if needed). Returns false
		/// when a walk for this folder is already running (deduplicated).
		/// </summary>
		internal bool StartCounting(string folderPath, Action<FolderCountProgress> onProgress) {
			CancellationTokenSource cts;
			lock (gate) {
				if (active.ContainsKey(folderPath))
					return false;
				cts = new CancellationTokenSource();
				active[folderPath] = cts;
			}

			Task.Run(() => {
				int count = 0;
				long bytes = 0;
				var lastReport = System.Diagnostics.Stopwatch.StartNew();
				try {
					var options = new EnumerationOptions {
						IgnoreInaccessible = true,
						RecurseSubdirectories = true,
						AttributesToSkip = FileAttributes.System,
					};
					foreach (var file in Directory.EnumerateFiles(folderPath, "*", options)) {
						cts.Token.ThrowIfCancellationRequested();
						if (!VDF.Core.Utils.FileUtils.IsMediaExtension(Path.GetExtension(file)))
							continue;
						count++;
						try { bytes += new FileInfo(file).Length; } catch (Exception) { /* raced deletion */ }
						if (lastReport.Elapsed >= progressInterval) {
							lastReport.Restart();
							onProgress(new FolderCountProgress(count, bytes, Completed: false));
						}
					}
					onProgress(new FolderCountProgress(count, bytes, Completed: true));
				}
				catch (OperationCanceledException) {
					// canceled walks report nothing further
				}
				catch (Exception) {
					onProgress(new FolderCountProgress(count, bytes, Completed: true, Failed: true));
				}
				finally {
					lock (gate) {
						active.Remove(folderPath);
					}
					cts.Dispose();
				}
			}, CancellationToken.None);
			return true;
		}

		internal void Cancel(string folderPath) {
			lock (gate) {
				if (active.TryGetValue(folderPath, out var cts))
					cts.Cancel();
			}
		}

		internal void CancelAll() {
			lock (gate) {
				foreach (var cts in active.Values)
					cts.Cancel();
			}
		}

		internal bool IsCounting(string folderPath) {
			lock (gate) {
				return active.ContainsKey(folderPath);
			}
		}
	}
}
