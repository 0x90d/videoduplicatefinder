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

namespace VDF.Core.Tests;

public class PartialClipGroupingTests {
	[Fact]
	public void OneClipMatchingTwoSources_KeepsLongestSource_AndProducesNoSingletons() {
		// videos[] is sorted by duration DESC, so source idx 0 is the longest.
		// Clip 2 is contained in both source 0 and source 1; source 1 has no other clips.
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 5),
			(sourceIdx: 1, clipIdx: 2, sim: 0.91f, offsetSec: 3),
		};

		var assignments = ScanEngine.AssignPartialClipGroups(matches);

		// Only one assignment survives — clip 2 stays with source 0 (longest match).
		Assert.Single(assignments);
		Assert.Equal(0, assignments[0].sourceIdx);
		Assert.Equal(2, assignments[0].clipIdx);
		// Source 1 is dropped entirely — no singleton group of just source 1.
		Assert.DoesNotContain(assignments, a => a.sourceIdx == 1);
	}

	[Fact]
	public void IndependentMatches_FormSeparateGroups() {
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 0),
			(sourceIdx: 1, clipIdx: 3, sim: 0.92f, offsetSec: 0),
		};

		var assignments = ScanEngine.AssignPartialClipGroups(matches);

		Assert.Equal(2, assignments.Count);
		Assert.NotEqual(assignments[0].groupId, assignments[1].groupId);
	}

	[Fact]
	public void SourceWithMultipleClips_AllShareOneGroupId() {
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 0),
			(sourceIdx: 0, clipIdx: 3, sim: 0.93f, offsetSec: 0),
			(sourceIdx: 0, clipIdx: 4, sim: 0.91f, offsetSec: 0),
		};

		var assignments = ScanEngine.AssignPartialClipGroups(matches);

		Assert.Equal(3, assignments.Count);
		var groupIds = assignments.Select(a => a.groupId).Distinct().ToList();
		Assert.Single(groupIds);
	}

	[Fact]
	public void ClipMatchingTwoSources_SecondSourceStillUsable_IfItHasOtherClips() {
		// Source 1 also matches clip 6, which is unclaimed → source 1's group survives.
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 5, sim: 0.95f, offsetSec: 0),
			(sourceIdx: 1, clipIdx: 5, sim: 0.92f, offsetSec: 0),
			(sourceIdx: 1, clipIdx: 6, sim: 0.90f, offsetSec: 0),
		};

		var assignments = ScanEngine.AssignPartialClipGroups(matches);

		// (0,5) and (1,6) survive; (1,5) is skipped.
		Assert.Equal(2, assignments.Count);
		Assert.Contains(assignments, a => a.sourceIdx == 0 && a.clipIdx == 5);
		Assert.Contains(assignments, a => a.sourceIdx == 1 && a.clipIdx == 6);
		Assert.DoesNotContain(assignments, a => a.sourceIdx == 1 && a.clipIdx == 5);

		// Source 0's group and source 1's group are distinct.
		var g0 = assignments.First(a => a.sourceIdx == 0).groupId;
		var g1 = assignments.First(a => a.sourceIdx == 1).groupId;
		Assert.NotEqual(g0, g1);
	}

	[Fact]
	public void SourceOrderInInput_DoesNotAffectOutcome() {
		// Same matches as the first test, but supplied out of order.
		var matches = new[] {
			(sourceIdx: 1, clipIdx: 2, sim: 0.91f, offsetSec: 3),
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 5),
		};

		var assignments = ScanEngine.AssignPartialClipGroups(matches);

		Assert.Single(assignments);
		Assert.Equal(0, assignments[0].sourceIdx);
		Assert.Equal(5, assignments[0].offsetSec);
	}

	[Fact]
	public void EmptyInput_ProducesEmptyOutput() {
		var assignments = ScanEngine.AssignPartialClipGroups(
			Array.Empty<(int sourceIdx, int clipIdx, float sim, int offsetSec)>());
		Assert.Empty(assignments);
	}

	[Fact]
	public void EveryGroupHasAtLeastTwoMembers() {
		// Stress: clip 10 and clip 11 are each contended by sources 0, 1, 2.
		// Source 2 also has its own clip 12.
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 10, sim: 0.95f, offsetSec: 0),
			(sourceIdx: 0, clipIdx: 11, sim: 0.94f, offsetSec: 0),
			(sourceIdx: 1, clipIdx: 10, sim: 0.93f, offsetSec: 0),
			(sourceIdx: 1, clipIdx: 11, sim: 0.92f, offsetSec: 0),
			(sourceIdx: 2, clipIdx: 10, sim: 0.91f, offsetSec: 0),
			(sourceIdx: 2, clipIdx: 12, sim: 0.90f, offsetSec: 0),
		};

		var assignments = ScanEngine.AssignPartialClipGroups(matches);

		// Reconstruct group sizes: each group = 1 source + N clips.
		var groupSizes = assignments
			.GroupBy(a => a.groupId)
			.Select(g => 1 + g.Count())
			.ToList();

		// Source 1 should be entirely dropped (its only clips, 10 and 11, are taken by source 0).
		// Source 0 → {s0, c10, c11}, Source 2 → {s2, c12}. No singletons.
		Assert.All(groupSizes, size => Assert.True(size >= 2, $"Found singleton group of size {size}"));
		Assert.DoesNotContain(assignments, a => a.sourceIdx == 1);
	}
}
