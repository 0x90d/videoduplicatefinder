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
// Engine-level cover for ScanEngine.SplitDaisyChainGroups after its rewrite onto
// DaisyChainSplitter (#901): the group/removal bookkeeping on real DuplicateItems
// and FileEntry gray frames, plus the over-budget skip that keeps a group intact.
// The splitter's graph algorithm itself is pinned against the old implementation
// in DaisyChainSplitterTests.

using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // SplitDaisyChainGroups resolves members through the shared static database
public class DaisyChainScanTests : IDisposable {

	readonly List<FileEntry> added = new();

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
	}

	const int FrameLength = GrayBytesUtils.Side * GrayBytesUtils.Side; // 1024

	/// <summary>Mid-gray frame with one quarter (<paramref name="quarter"/> 0..3) raised by 64 levels; -1 = plain.</summary>
	static byte[] Frame(int quarter) {
		var frame = new byte[FrameLength];
		Array.Fill(frame, (byte)128);
		if (quarter >= 0)
			Array.Fill(frame, (byte)192, quarter * (FrameLength / 4), FrameLength / 4);
		return frame;
	}

	FileEntry AddImage(string name, byte[] frame) {
		var entry = new FileEntry {
			_Path = @"C:\vdf-daisychain-" + Guid.NewGuid().ToString("N") + "\\" + name,
			FileSize = 1,
			invalid = false,
			IsImage = true,
			mediaInfo = new MediaInfo { Duration = TimeSpan.Zero },
		};
		entry.grayBytes[0] = frame;
		entry.compareGray = new byte[]?[] { frame };
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	static ScanEngine NewEngine() {
		var engine = new ScanEngine();
		engine.Settings.ThumbnailCount = 1;
		engine.positionList.Add(0.5f);
		// Hub vs leaf differ in one quarter by 64 levels: 6.25% (a 93.75% match, passes).
		// Leaf vs leaf differ in two quarters: 12.5% (87.5%, fails).
		engine.Settings.Percent = 90f;
		engine.Settings.IncludeImages = true;
		engine.ElapsedTimer.Start();
		return engine;
	}

	/// <summary>A hub and three leaves that match the hub only, all filed under one GroupId as the merge phase would leave them.</summary>
	(ScanEngine engine, FileEntry hub, List<FileEntry> leaves, Guid groupId) StarGroup() {
		var engine = NewEngine();
		var hub = AddImage("hub.jpg", Frame(-1));
		var leaves = new List<FileEntry> { AddImage("leaf1.jpg", Frame(0)), AddImage("leaf2.jpg", Frame(1)), AddImage("leaf3.jpg", Frame(2)) };
		var groupId = Guid.NewGuid();
		engine.Duplicates.Add(new DuplicateItem(hub, 0f, groupId, DuplicateFlags.None));
		foreach (var leaf in leaves)
			engine.Duplicates.Add(new DuplicateItem(leaf, 0.0625f, groupId, DuplicateFlags.None));
		return (engine, hub, leaves, groupId);
	}

	[Fact]
	public void StarGroup_DropsOneLeafAndRegroupsTheRest() {
		var (engine, hub, leaves, groupId) = StarGroup();
		// Sanity: the frames really form a star under the engine's own verdict.
		foreach (var leaf in leaves)
			Assert.True(engine.CheckIfDuplicate(hub, null, null, leaf, out _));
		Assert.False(engine.CheckIfDuplicate(leaves[0], null, null, leaves[1], out _));

		engine.SplitDaisyChainGroups();

		// Majority rule on 4 members needs 2 partners; the first leaf (degree 1) goes,
		// then 3 members need 1 partner each, which the remaining leaves have.
		Assert.Equal(3, engine.Duplicates.Count);
		Assert.Contains(engine.Duplicates, d => d.Path == hub.Path);
		Assert.Equal(2, leaves.Count(l => engine.Duplicates.Any(d => d.Path == l.Path)));
		var newGroupId = Assert.Single(engine.Duplicates.Select(d => d.GroupId).Distinct());
		Assert.NotEqual(groupId, newGroupId);
	}

	[Fact]
	public void StarGroup_OverBudget_IsKeptExactlyAsFound() {
		var (engine, _, _, groupId) = StarGroup();
		engine.daisyChainMatrixBudgetBytes = 0;

		engine.SplitDaisyChainGroups();

		Assert.Equal(4, engine.Duplicates.Count);
		Assert.All(engine.Duplicates, d => Assert.Equal(groupId, d.GroupId));
	}

	[Fact]
	public void Clique_IsLeftUntouched() {
		var engine = NewEngine();
		var groupId = Guid.NewGuid();
		for (int i = 0; i < 3; i++)
			engine.Duplicates.Add(new DuplicateItem(AddImage($"same{i}.jpg", Frame(-1)), 0f, groupId, DuplicateFlags.None));

		engine.SplitDaisyChainGroups();

		Assert.Equal(3, engine.Duplicates.Count);
		Assert.All(engine.Duplicates, d => Assert.Equal(groupId, d.GroupId));
	}

	[Fact]
	public void GroupWithMissingSnapshot_IsSkipped() {
		var (engine, hub, _, groupId) = StarGroup();
		hub.compareGray = null; // as after a compare phase released its snapshots

		engine.SplitDaisyChainGroups();

		Assert.Equal(4, engine.Duplicates.Count);
		Assert.All(engine.Duplicates, d => Assert.Equal(groupId, d.GroupId));
	}
}
