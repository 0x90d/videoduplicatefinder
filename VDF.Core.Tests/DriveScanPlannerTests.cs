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

using System.Text.Json;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

// Per-drive scan concurrency: the planner decides how many files are read at once from each
// drive. Wrong math here either thrashes a spindle HDD (too many) or wastes an SSD (too few),
// and a broken "1 = strictly serial" promise would surprise users who rely on it.
public class DriveScanPlannerTests {

	static FileEntry Entry(string path, string folder, long size = 2 << 20) {
		var entry = new FileEntry { Folder = folder, FileSize = size };
		entry._Path = path;
		return entry;
	}

	static DriveScanGroup Group(string root, DriveSpeedClass speedClass) =>
		new(root) { SpeedClass = speedClass };

	static readonly Dictionary<string, string> NoOverrides = new();
	static readonly Func<string, bool> NoNetwork = _ => false;

	// ── AssignParallelism ───────────────────────────────────────────────────

	[Fact]
	public void UnlimitedSetting_FastDrivesShareCpuBudget_SlowDrivesGetHddCap() {
		var groups = new[] {
			Group(@"C:\", DriveSpeedClass.Fast),
			Group(@"D:\", DriveSpeedClass.Fast),
			Group(@"E:\", DriveSpeedClass.Slow),
		};
		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: -1, hddMaxDegreeOfParallelism: 2, processorCount: 8);
		// -1 must mean "processor count", not unbounded — N fast drives each getting
		// "unlimited" was the fork's oversubscription bug.
		Assert.Equal(4, groups[0].DegreeOfParallelism);
		Assert.Equal(4, groups[1].DegreeOfParallelism);
		Assert.Equal(2, groups[2].DegreeOfParallelism);
	}

	[Fact]
	public void GlobalOne_MeansStrictlySerialEverywhere() {
		var groups = new[] {
			Group(@"C:\", DriveSpeedClass.Fast),
			Group(@"D:\", DriveSpeedClass.Slow),
		};
		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: 1, hddMaxDegreeOfParallelism: 8, processorCount: 16);
		Assert.All(groups, g => Assert.Equal(1, g.DegreeOfParallelism));
	}

	[Fact]
	public void FastShareIsFlooredButNeverZero() {
		var groups = new[] {
			Group(@"C:\", DriveSpeedClass.Fast),
			Group(@"D:\", DriveSpeedClass.Fast),
			Group(@"E:\", DriveSpeedClass.Fast),
		};
		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: 8, hddMaxDegreeOfParallelism: 2, processorCount: 8);
		Assert.All(groups, g => Assert.Equal(2, g.DegreeOfParallelism)); // floor(8/3)

		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: 2, hddMaxDegreeOfParallelism: 2, processorCount: 8);
		Assert.All(groups, g => Assert.Equal(1, g.DegreeOfParallelism)); // floor(2/3) -> min 1
	}

	[Fact]
	public void SingleFastDrive_GetsTheWholeBudget() {
		var groups = new[] { Group(@"C:\", DriveSpeedClass.Fast) };
		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: -1, hddMaxDegreeOfParallelism: 2, processorCount: 12);
		Assert.Equal(12, groups[0].DegreeOfParallelism);
	}

	[Fact]
	public void SlowDrive_NeverExceedsTheGlobalUmbrella() {
		var groups = new[] { Group(@"E:\", DriveSpeedClass.Slow) };
		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: 2, hddMaxDegreeOfParallelism: 6, processorCount: 8);
		Assert.Equal(2, groups[0].DegreeOfParallelism);
	}

	[Fact]
	public void NonPositiveHddCap_FallsBackToDefaultOfTwo() {
		var groups = new[] { Group(@"E:\", DriveSpeedClass.Slow) };
		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: 8, hddMaxDegreeOfParallelism: 0, processorCount: 8);
		Assert.Equal(2, groups[0].DegreeOfParallelism);
	}

	[Fact]
	public void ZeroSetting_IsTreatedAsAuto() {
		// The GUI blocks 0 and the CLI maps it to 1, but a hand-edited settings file can
		// still deliver it — it must behave like "auto", not crash ParallelOptions.
		var groups = new[] { Group(@"C:\", DriveSpeedClass.Fast) };
		DriveScanPlanner.AssignParallelism(groups, maxDegreeOfParallelism: 0, hddMaxDegreeOfParallelism: 2, processorCount: 6);
		Assert.Equal(6, groups[0].DegreeOfParallelism);
	}

	// ── GetRootKey / PartitionByDrive ───────────────────────────────────────

	[Fact]
	public void NestedMountWins_AndMountBoundaryIsRespected() {
		var mounts = new[] { "/mnt/usb", "/" }; // longest first, as SnapshotMountRoots delivers
		Assert.Equal("/mnt/usb", DriveScanPlanner.GetRootKey("/mnt/usb/movies/a.mp4", mounts));
		Assert.Equal("/", DriveScanPlanner.GetRootKey("/home/user/a.mp4", mounts));
		// "/mnt/usb" may not claim files on "/mnt/usb2"
		Assert.Equal("/", DriveScanPlanner.GetRootKey("/mnt/usb2/a.mp4", mounts));
	}

	[Fact]
	public void UncPath_FallsBackToShareRoot() {
		if (!OperatingSystem.IsWindows())
			return; // UNC root semantics; PR CI runs Windows only
		Assert.Equal(@"\\server\share", DriveScanPlanner.GetRootKey(@"\\server\share\videos\a.mp4", Array.Empty<string>()));
	}

	[Fact]
	public void PartitionByDrive_GroupsCaseInsensitivelyAndKeepsStableOrder() {
		var entries = new[] {
			Entry(@"D:\videos\a.mp4", @"D:\videos"),
			Entry(@"C:\clips\b.mp4", @"C:\clips"),
			Entry(@"d:\videos\c.mp4", @"d:\videos"),
			Entry(@"C:\other\d.mp4", @"C:\other"),
		};
		var groups = DriveScanPlanner.PartitionByDrive(entries, new[] { @"C:\", @"D:\" });
		Assert.Equal(2, groups.Count);
		Assert.Equal(@"C:\", groups[0].Root);
		Assert.Equal(2, groups[0].Entries.Count);
		Assert.Equal(@"D:\", groups[1].Root);
		Assert.Equal(2, groups[1].Entries.Count);
	}

	[Fact]
	public void PartitionByDrive_LosesNoEntry() {
		var entries = new[] {
			Entry(@"C:\a\1.mp4", @"C:\a"),
			Entry(@"C:\a\2.mp4", @"C:\a"),
			Entry(@"E:\b\3.mp4", @"E:\b"),
		};
		var groups = DriveScanPlanner.PartitionByDrive(entries, new[] { @"C:\", @"E:\" });
		Assert.Equal(entries.Length, groups.Sum(g => g.Entries.Count));
	}

	// ── MapEntryIndexes ─────────────────────────────────────────────────────

	// #857: the AI dense-sampling pass writes into one result slot per video, so its
	// per-drive loops address videos by their ORIGINAL list index. A wrong mapping
	// here would silently attach embeddings to the wrong file.
	[Fact]
	public void MapEntryIndexes_MapsEveryEntryToItsOriginalIndex_ExactlyOnce() {
		var entries = new List<FileEntry> {
			Entry(@"D:\videos\long.mp4", @"D:\videos"),   // 0
			Entry(@"C:\clips\mid.mp4", @"C:\clips"),      // 1
			Entry(@"D:\videos\short.mp4", @"D:\videos"),  // 2
			Entry(@"C:\other\tiny.mp4", @"C:\other"),     // 3
		};
		var groups = DriveScanPlanner.PartitionByDrive(entries, new[] { @"C:\", @"D:\" });
		int[][] indexes = DriveScanPlanner.MapEntryIndexes(entries, groups);

		Assert.Equal(groups.Count, indexes.Length);
		for (int g = 0; g < groups.Count; g++)
			for (int k = 0; k < indexes[g].Length; k++)
				Assert.Same(groups[g].Entries[k], entries[indexes[g][k]]);
		// Every original index appears exactly once across all groups.
		Assert.Equal(new[] { 0, 1, 2, 3 }, indexes.SelectMany(i => i).OrderBy(i => i).ToArray());
	}

	[Fact]
	public void MapEntryIndexes_PreservesInputOrderWithinAGroup() {
		var entries = new List<FileEntry> {
			Entry(@"C:\a\3.mp4", @"C:\a"),
			Entry(@"C:\a\1.mp4", @"C:\a"),
			Entry(@"C:\a\2.mp4", @"C:\a"),
		};
		var groups = DriveScanPlanner.PartitionByDrive(entries, new[] { @"C:\" });
		int[][] indexes = DriveScanPlanner.MapEntryIndexes(entries, groups);

		// The AI pass sorts videos longest-first before partitioning; that order must
		// survive within each drive group so long files still start first per drive.
		Assert.Equal(new[] { 0, 1, 2 }, indexes[0]);
	}

	// ── ClassifyGroups ──────────────────────────────────────────────────────

	[Fact]
	public void Override_WinsAndSkipsTheProbe_KeyAndValueCaseInsensitive() {
		var groups = new List<DriveScanGroup> { Group(@"D:\", DriveSpeedClass.Slow) };
		bool probed = false;
		DriveScanPlanner.ClassifyGroups(groups,
			new Dictionary<string, string> { ["d:"] = "ssd" },
			NoNetwork,
			_ => { probed = true; return 20.0; });
		Assert.Equal(DriveSpeedClass.Fast, groups[0].SpeedClass);
		Assert.False(probed);
	}

	[Fact]
	public void Override_CanForceSlow() {
		// The probe cannot see through the OS page cache — a warm-cache HDD probes fast.
		// The override map is the documented correction for exactly that case.
		var groups = new List<DriveScanGroup> { Group(@"D:\", DriveSpeedClass.Fast) };
		DriveScanPlanner.ClassifyGroups(groups,
			new Dictionary<string, string> { [@"D:\"] = "HDD" },
			NoNetwork,
			_ => 0.2);
		Assert.Equal(DriveSpeedClass.Slow, groups[0].SpeedClass);
	}

	[Fact]
	public void UnusableOverrideValue_FallsThroughToProbe() {
		var groups = new List<DriveScanGroup> { Group(@"D:\", DriveSpeedClass.Slow) };
		DriveScanPlanner.ClassifyGroups(groups,
			new Dictionary<string, string> { [@"D:\"] = "NVMe" },
			NoNetwork,
			_ => 0.4);
		Assert.Equal(DriveSpeedClass.Fast, groups[0].SpeedClass);
	}

	[Fact]
	public void NetworkRoot_IsSlowWithoutProbing() {
		var groups = new List<DriveScanGroup> { Group(@"\\nas\media", DriveSpeedClass.Fast) };
		bool probed = false;
		DriveScanPlanner.ClassifyGroups(groups, NoOverrides,
			root => root.StartsWith(@"\\"),
			_ => { probed = true; return 0.2; });
		Assert.Equal(DriveSpeedClass.Slow, groups[0].SpeedClass);
		Assert.False(probed);
	}

	[Theory]
	[InlineData(0.4, true)]
	[InlineData(2.9, true)]
	[InlineData(3.0, false)]
	[InlineData(11.0, false)]
	public void ProbeLatency_SplitsAtThreshold(double latencyMs, bool expectFast) {
		var groups = new List<DriveScanGroup> { Group(@"D:\", DriveSpeedClass.Slow) };
		DriveScanPlanner.ClassifyGroups(groups, NoOverrides, NoNetwork, _ => latencyMs);
		Assert.Equal(expectFast ? DriveSpeedClass.Fast : DriveSpeedClass.Slow, groups[0].SpeedClass);
	}

	[Fact]
	public void UnprobeableDrive_IsConservativelySlow() {
		var groups = new List<DriveScanGroup> { Group(@"D:\", DriveSpeedClass.Fast) };
		DriveScanPlanner.ClassifyGroups(groups, NoOverrides, NoNetwork, _ => null);
		Assert.Equal(DriveSpeedClass.Slow, groups[0].SpeedClass);
		Assert.Equal("no probe candidate", groups[0].ClassSource);
	}

	// ── DriveProgressTracker ────────────────────────────────────────────────

	static DriveScanGroup GroupWithEntries(string root, DriveSpeedClass speedClass, params FileEntry[] entries) {
		var group = Group(root, speedClass);
		group.Entries.AddRange(entries);
		return group;
	}

	[Fact]
	public void Tracker_TotalsCountOnlyEntriesInScanScope() {
		var inScope = Entry(@"C:\a\1.mp4", @"C:\a", size: 100);
		var outOfScope = Entry(@"C:\old\2.mp4", @"C:\old", size: 900);
		var groups = new[] { GroupWithEntries(@"C:\", DriveSpeedClass.Fast, inScope, outOfScope) };
		var tracker = new DriveProgressTracker(groups, e => e.Folder == @"C:\a", classified: true);
		DriveProgress[] snapshot = tracker.Snapshot();
		Assert.Single(snapshot);
		Assert.Equal(1, snapshot[0].TotalFiles);
		Assert.Equal(100, snapshot[0].TotalBytes);
	}

	[Fact]
	public void Tracker_CompleteAdvancesDoneAndSnapshotKeepsGroupOrder() {
		var groups = new[] {
			GroupWithEntries(@"C:\", DriveSpeedClass.Fast, Entry(@"C:\a\1.mp4", @"C:\a", 100), Entry(@"C:\a\2.mp4", @"C:\a", 300)),
			GroupWithEntries(@"D:\", DriveSpeedClass.Slow, Entry(@"D:\b\3.mp4", @"D:\b", 500)),
		};
		var tracker = new DriveProgressTracker(groups, _ => true, classified: true);
		tracker.CounterFor(0).Complete(100);
		tracker.CounterFor(1).Complete(500);
		DriveProgress[] snapshot = tracker.Snapshot();
		Assert.Equal(2, snapshot.Length);
		Assert.Equal(@"C:\", snapshot[0].Root);
		Assert.Equal(1, snapshot[0].DoneFiles);
		Assert.Equal(100, snapshot[0].DoneBytes);
		Assert.Equal(400, snapshot[0].TotalBytes);
		Assert.True(snapshot[0].IsFastDrive);
		Assert.Equal(@"D:\", snapshot[1].Root);
		Assert.Equal(1, snapshot[1].DoneFiles);
		Assert.False(snapshot[1].IsFastDrive);
	}

	[Fact]
	public void Tracker_DrivesWithNoWorkAreOmittedFromTheSnapshot() {
		// A drive holding only out-of-scope history would otherwise show a bar stuck at 0.
		var groups = new[] {
			GroupWithEntries(@"C:\", DriveSpeedClass.Fast, Entry(@"C:\a\1.mp4", @"C:\a", 100)),
			GroupWithEntries(@"D:\", DriveSpeedClass.Slow, Entry(@"D:\old\2.mp4", @"D:\old", 900)),
		};
		var tracker = new DriveProgressTracker(groups, e => e.Folder.StartsWith(@"C:\"), classified: true);
		DriveProgress[] snapshot = tracker.Snapshot();
		Assert.Single(snapshot);
		Assert.Equal(@"C:\", snapshot[0].Root);
	}

	[Fact]
	public void Tracker_UnclassifiedScan_ReportsUnknownDriveType() {
		var groups = new[] { GroupWithEntries(@"C:\", DriveSpeedClass.Fast, Entry(@"C:\a\1.mp4", @"C:\a", 100)) };
		var tracker = new DriveProgressTracker(groups, _ => true, classified: false);
		Assert.Null(tracker.Snapshot()[0].IsFastDrive);
	}

	// ── Settings round-trip ─────────────────────────────────────────────────

	[Fact]
	public void DriveTypeOverrides_StayCaseInsensitiveAfterJsonDeserialization() {
		// System.Text.Json rebuilds the dictionary without the comparer; the setter must
		// re-wrap it or "d:\" from a hand-edited settings file silently stops matching "D:\".
		var settings = JsonSerializer.Deserialize<Settings>(
			/*lang=json*/ @"{""DriveTypeOverrides"":{""d:\\"":""SSD""}}",
			new JsonSerializerOptions { IncludeFields = true });
		Assert.NotNull(settings);
		Assert.True(settings!.DriveTypeOverrides.ContainsKey(@"D:\"));
	}
}
