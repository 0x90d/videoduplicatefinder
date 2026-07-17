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

using System.Diagnostics;
using System.Linq;

namespace VDF.Core.Utils {

	/// <summary>Storage-speed classification of a drive for scan concurrency purposes.</summary>
	internal enum DriveSpeedClass {
		/// <summary>SSD/NVMe — random reads are cheap, gets a share of the global CPU budget.</summary>
		Fast,
		/// <summary>Spindle HDD, network share or unclassifiable — parallel reads seek-thrash, gets the low HDD cap.</summary>
		Slow,
	}

	/// <summary>One drive's slice of the scan: its entries plus the concurrency it runs at.</summary>
	internal sealed class DriveScanGroup {
		public DriveScanGroup(string root) => Root = root;
		public string Root { get; }
		public List<FileEntry> Entries { get; } = new();
		public DriveSpeedClass SpeedClass { get; set; } = DriveSpeedClass.Slow;
		public int DegreeOfParallelism { get; set; } = 1;
		/// <summary>How the class was decided (override / probe / network / no candidate) — for the scan log.</summary>
		public string ClassSource { get; set; } = string.Empty;
	}

	/// <summary>
	/// Thread-safe per-drive done/total accounting for the analysis phase, snapshotted into
	/// <see cref="ScanProgressChangedEventArgs.Drives"/>. Totals count only entries that will
	/// actually report progress (in scan scope), so a drive that only holds out-of-scope
	/// history can never show a bar stuck below 100%.
	/// </summary>
	internal sealed class DriveProgressTracker {
		internal sealed class Counter {
			internal Counter(string root, bool? isFast) { Root = root; IsFast = isFast; }
			internal readonly string Root;
			internal readonly bool? IsFast;
			internal long TotalBytes;
			internal int TotalFiles;
			internal long doneBytes;
			internal int doneFiles;
			internal void Complete(long fileSize) {
				Interlocked.Add(ref doneBytes, fileSize);
				Interlocked.Increment(ref doneFiles);
			}
		}

		readonly Counter[] counters;

		/// <param name="classified">false for the strictly-serial path, where drives are never probed — speed class is then unknown.</param>
		internal DriveProgressTracker(IReadOnlyList<DriveScanGroup> groups, Func<FileEntry, bool> countsTowardProgress, bool classified) {
			counters = new Counter[groups.Count];
			for (int i = 0; i < groups.Count; i++) {
				var counter = new Counter(groups[i].Root, classified ? groups[i].SpeedClass == DriveSpeedClass.Fast : null);
				foreach (FileEntry entry in groups[i].Entries) {
					if (!countsTowardProgress(entry))
						continue;
					counter.TotalFiles++;
					counter.TotalBytes += entry.FileSize;
				}
				counters[i] = counter;
			}
		}

		internal Counter CounterFor(int groupIndex) => counters[groupIndex];

		/// <summary>Current state of every drive that has work in this scan, in group order.</summary>
		internal DriveProgress[] Snapshot() {
			int withWork = 0;
			for (int i = 0; i < counters.Length; i++)
				if (counters[i].TotalFiles > 0)
					withWork++;
			var snapshot = new DriveProgress[withWork];
			int next = 0;
			for (int i = 0; i < counters.Length; i++) {
				Counter counter = counters[i];
				if (counter.TotalFiles == 0)
					continue;
				snapshot[next++] = new DriveProgress {
					Root = counter.Root,
					TotalBytes = counter.TotalBytes,
					DoneBytes = Interlocked.Read(ref counter.doneBytes),
					TotalFiles = counter.TotalFiles,
					DoneFiles = Volatile.Read(ref counter.doneFiles),
					IsFastDrive = counter.IsFast,
				};
			}
			return snapshot;
		}
	}

	/// <summary>
	/// Plans per-drive scan concurrency: partitions the database by drive, classifies each
	/// drive's storage speed (user override → network heuristic → seek-latency probe) and
	/// assigns each drive a degree of parallelism under the global
	/// <see cref="Settings.MaxDegreeOfParallelism"/> budget. A single spinning disk delivers a
	/// fraction of its sequential throughput when many files are read at once (seek thrash),
	/// while an SSD needs high queue depth to saturate — one global setting cannot fit both.
	/// </summary>
	internal static class DriveScanPlanner {
		// Median random-read latency splitting SSD/NVMe from spindle HDDs. SSDs answer in
		// well under a millisecond, spindles need a head seek (~8–15 ms).
		internal const double SeekLatencyThresholdMs = 3.0;

		/// <summary>
		/// Groups entries by the drive (mount) they live on. Mount-point aware so that on
		/// Linux/macOS <c>/mnt/usb</c> groups separately from <c>/</c>; paths not under any
		/// known mount fall back to <see cref="Path.GetPathRoot(string?)"/> (UNC shares group
		/// by <c>\\server\share</c>). Groups are returned in stable root order.
		/// </summary>
		internal static List<DriveScanGroup> PartitionByDrive(IEnumerable<FileEntry> entries, IReadOnlyList<string>? mountRoots = null) {
			mountRoots ??= SnapshotMountRoots();
			var groups = new Dictionary<string, DriveScanGroup>(StringComparer.OrdinalIgnoreCase);
			// Entries arrive folder-sorted in practice; caching folder → group skips the
			// per-path root resolution for all but the first file of each folder.
			var folderCache = new Dictionary<string, DriveScanGroup>(StringComparer.OrdinalIgnoreCase);
			foreach (FileEntry entry in entries) {
				DriveScanGroup? group;
				if (entry.Folder.Length == 0 || !folderCache.TryGetValue(entry.Folder, out group)) {
					string root = GetRootKey(entry.Path, mountRoots);
					if (!groups.TryGetValue(root, out group)) {
						group = new DriveScanGroup(root);
						groups.Add(root, group);
					}
					if (entry.Folder.Length != 0)
						folderCache[entry.Folder] = group;
				}
				group.Entries.Add(entry);
			}
			return groups.Values.OrderBy(g => g.Root, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>
		/// Maps each group's entries back to their indices in <paramref name="entries"/>
		/// (entries are unique by path). Lets an index-addressed pipeline (e.g. the AI
		/// dense-sampling pass writing into one result slot per video) run per-drive
		/// loops while keeping its original indexing.
		/// </summary>
		internal static int[][] MapEntryIndexes(IReadOnlyList<FileEntry> entries, IReadOnlyList<DriveScanGroup> groups) {
			var indexByEntry = new Dictionary<FileEntry, int>(entries.Count);
			for (int i = 0; i < entries.Count; i++)
				indexByEntry[entries[i]] = i;
			var result = new int[groups.Count][];
			for (int g = 0; g < groups.Count; g++) {
				List<FileEntry> groupEntries = groups[g].Entries;
				var indexes = new int[groupEntries.Count];
				for (int k = 0; k < groupEntries.Count; k++)
					indexes[k] = indexByEntry[groupEntries[k]];
				result[g] = indexes;
			}
			return result;
		}

		/// <summary>Current mount roots, longest first so nested mounts win the prefix match.</summary>
		internal static List<string> SnapshotMountRoots() {
			var roots = new List<string>();
			try {
				foreach (DriveInfo drive in DriveInfo.GetDrives()) {
					try {
						if (drive.IsReady)
							roots.Add(drive.RootDirectory.FullName);
					}
					catch { /* a dying drive must not kill the scan */ }
				}
			}
			catch { /* no mount table — GetPathRoot fallback still groups by drive letter */ }
			roots.Sort((a, b) => b.Length.CompareTo(a.Length));
			return roots;
		}

		/// <summary>Longest mount root the path lives under, else <see cref="Path.GetPathRoot(string?)"/>.</summary>
		internal static string GetRootKey(string path, IReadOnlyList<string> mountRootsLongestFirst) {
			for (int i = 0; i < mountRootsLongestFirst.Count; i++) {
				if (PathStartsAtMount(path, mountRootsLongestFirst[i]))
					return mountRootsLongestFirst[i];
			}
			try {
				string? root = Path.GetPathRoot(path);
				return string.IsNullOrEmpty(root) ? "?" : root;
			}
			catch {
				return "?";
			}
		}

		static bool PathStartsAtMount(string path, string mount) {
			if (mount.Length == 0 || !path.StartsWith(mount, StringComparison.OrdinalIgnoreCase))
				return false;
			// Prefix must end on a segment boundary: "/mnt/usb" may not claim "/mnt/usb2/x".
			if (path.Length == mount.Length)
				return true;
			char last = mount[^1];
			if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
				return true;
			char next = path[mount.Length];
			return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
		}

		/// <summary>
		/// Decides each group's <see cref="DriveSpeedClass"/>. Precedence: user override map
		/// (drive root → "SSD"/"HDD") → network shares are Slow → seek-latency probe. A group
		/// that cannot be probed (no readable candidate file) is conservatively Slow.
		/// </summary>
		internal static void ClassifyGroups(IReadOnlyList<DriveScanGroup> groups,
											IReadOnlyDictionary<string, string> overrides,
											Func<string, bool> isNetworkRoot,
											Func<DriveScanGroup, double?> probeSeekLatencyMs) {
			foreach (DriveScanGroup group in groups) {
				if (TryGetOverride(overrides, group.Root, out DriveSpeedClass overridden)) {
					group.SpeedClass = overridden;
					group.ClassSource = "user override";
					continue;
				}
				if (isNetworkRoot(group.Root)) {
					group.SpeedClass = DriveSpeedClass.Slow;
					group.ClassSource = "network share";
					continue;
				}
				double? latency = probeSeekLatencyMs(group);
				if (latency == null) {
					group.SpeedClass = DriveSpeedClass.Slow;
					group.ClassSource = "no probe candidate";
				}
				else {
					group.SpeedClass = latency.Value < SeekLatencyThresholdMs ? DriveSpeedClass.Fast : DriveSpeedClass.Slow;
					group.ClassSource = $"seek probe {latency.Value:0.0} ms";
				}
			}
		}

		static bool TryGetOverride(IReadOnlyDictionary<string, string> overrides, string root, out DriveSpeedClass speedClass) {
			speedClass = DriveSpeedClass.Slow;
			if (overrides.Count == 0)
				return false;
			string normalizedRoot = NormalizeRoot(root);
			foreach (KeyValuePair<string, string> entry in overrides) {
				if (!string.Equals(NormalizeRoot(entry.Key), normalizedRoot, StringComparison.OrdinalIgnoreCase))
					continue;
				if (string.Equals(entry.Value, "SSD", StringComparison.OrdinalIgnoreCase)) {
					speedClass = DriveSpeedClass.Fast;
					return true;
				}
				if (string.Equals(entry.Value, "HDD", StringComparison.OrdinalIgnoreCase)) {
					speedClass = DriveSpeedClass.Slow;
					return true;
				}
				Logger.Instance.Info($"Ignoring drive type override '{entry.Key}' = '{entry.Value}' — expected 'SSD' or 'HDD'.");
				return false; // unusable value → automatic classification
			}
			return false;
		}

		// "D:", "D:\" and "/mnt/usb/" all address the same root as reported by the mount snapshot.
		static string NormalizeRoot(string root) =>
			root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		/// <summary>
		/// Assigns each group its degree of parallelism.
		/// <see cref="Settings.MaxDegreeOfParallelism"/> stays the global umbrella: it is the
		/// CPU budget fast drives share (≤ 0 = processor count), and no single drive may exceed
		/// it. Slow drives get <see cref="Settings.HddMaxDegreeOfParallelism"/> each — they are
		/// seek-bound, not CPU-bound, so they ride on top of the budget rather than consuming
		/// it. A global setting of exactly 1 keeps the documented "strictly one file at a time"
		/// promise: every group serial (the engine then also runs the groups sequentially).
		/// </summary>
		internal static void AssignParallelism(IReadOnlyList<DriveScanGroup> groups, int maxDegreeOfParallelism, int hddMaxDegreeOfParallelism, int processorCount) {
			if (maxDegreeOfParallelism == 1) {
				foreach (DriveScanGroup group in groups)
					group.DegreeOfParallelism = 1;
				return;
			}
			int budget = Math.Max(1, maxDegreeOfParallelism <= 0 ? processorCount : maxDegreeOfParallelism);
			int hddCap = hddMaxDegreeOfParallelism > 0 ? hddMaxDegreeOfParallelism : 2;
			int fastCount = 0;
			for (int i = 0; i < groups.Count; i++) {
				if (groups[i].SpeedClass == DriveSpeedClass.Fast)
					fastCount++;
			}
			int fastShare = fastCount > 0 ? Math.Max(1, budget / fastCount) : budget;
			foreach (DriveScanGroup group in groups)
				group.DegreeOfParallelism = group.SpeedClass == DriveSpeedClass.Fast ? fastShare : Math.Min(hddCap, budget);
		}

		/// <summary>UNC paths and drives the OS reports as network shares.</summary>
		internal static bool IsNetworkRoot(string root) {
			if (root.StartsWith(@"\\", StringComparison.Ordinal))
				return true;
			try {
				return new DriveInfo(root).DriveType == DriveType.Network;
			}
			catch {
				return false;
			}
		}

		/// <summary>
		/// Median latency of a few random 64 KB reads from one representative file in the
		/// group — random offsets make it seek-bound, so a spindle HDD separates cleanly from
		/// SSD/NVMe. Returns null when the group has no readable candidate. Known limitation:
		/// a file sitting in the OS page cache probes fast regardless of the disk underneath;
		/// the <see cref="Settings.DriveTypeOverrides"/> map exists to correct such drives.
		/// </summary>
		internal static double? ProbeSeekLatencyMs(IEnumerable<FileEntry> candidates) {
			const int block = 64 * 1024;
			const int reads = 6;
			string? path = null;
			foreach (FileEntry candidate in candidates) {
				if (candidate.FileSize > (1 << 20) && File.Exists(candidate.Path)) {
					path = candidate.Path;
					break;
				}
			}
			if (path == null)
				return null;
			try {
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, block, FileOptions.None);
				long length = fs.Length;
				if (length <= block)
					return null;
				var buffer = new byte[block];
				var times = new List<double>(reads);
				var random = new Random(0x5eed); // fixed seed — probing must be deterministic
				var stopwatch = new Stopwatch();
				for (int i = 0; i < reads; i++) {
					long offset = (long)(random.NextDouble() * (length - block)) & ~4095L;
					fs.Seek(offset, SeekOrigin.Begin);
					stopwatch.Restart();
					int read = fs.Read(buffer, 0, block);
					stopwatch.Stop();
					if (read > 0)
						times.Add(stopwatch.Elapsed.TotalMilliseconds);
				}
				if (times.Count == 0)
					return null;
				times.Sort();
				return times[times.Count / 2];
			}
			catch {
				return null;
			}
		}
	}
}
