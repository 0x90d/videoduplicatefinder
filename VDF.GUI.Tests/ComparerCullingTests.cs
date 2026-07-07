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

using VDF.Core.ViewModels;
using VDF.GUI.Data;

namespace VDF.GUI.Tests;

public class CullingPairFlowTests {

	[Fact]
	public void StartsWithFirstTwoItems() {
		var flow = new CullingPairFlow(3);
		Assert.Equal(0, flow.LeftIndex);
		Assert.Equal(1, flow.RightIndex);
		Assert.Equal(1, flow.PairNumber);
		Assert.Equal(2, flow.PairCount);
		Assert.True(flow.HasPair);
	}

	[Fact]
	public void SingleItemGroup_HasNoPair() {
		var flow = new CullingPairFlow(1);
		Assert.False(flow.HasPair);
		Assert.True(flow.Advance(CullingPairFlow.Decision.KeepLeft).GroupFinished);
	}

	[Fact]
	public void KeepLeft_ChecksChallenger_KeeperStays() {
		var flow = new CullingPairFlow(3);
		var step = flow.Advance(CullingPairFlow.Decision.KeepLeft);

		Assert.Equal(1, step.CheckIndex);   // challenger marked for deletion
		Assert.Equal(0, step.KeepIndex);
		Assert.False(step.GroupFinished);
		Assert.Equal(0, flow.LeftIndex);    // champion unchanged
		Assert.Equal(2, flow.RightIndex);   // next challenger
		Assert.Equal(2, flow.PairNumber);
	}

	[Fact]
	public void KeepRight_CrownsChallenger_ChecksOldKeeper() {
		var flow = new CullingPairFlow(4);
		var step = flow.Advance(CullingPairFlow.Decision.KeepRight);

		Assert.Equal(0, step.CheckIndex);   // dethroned keeper checked
		Assert.Equal(1, step.KeepIndex);
		Assert.Equal(1, flow.LeftIndex);    // challenger is the new keeper
		Assert.Equal(2, flow.RightIndex);
	}

	[Fact]
	public void Skip_DecidesNothing_AdvancesChallenger() {
		var flow = new CullingPairFlow(3);
		var step = flow.Advance(CullingPairFlow.Decision.Skip);

		Assert.Equal(-1, step.CheckIndex);
		Assert.Equal(-1, step.KeepIndex);
		Assert.Equal(0, flow.LeftIndex);
		Assert.Equal(2, flow.RightIndex);
	}

	[Fact]
	public void GroupOfN_FinishesAfterNMinusOnePairs() {
		var flow = new CullingPairFlow(4);
		Assert.False(flow.Advance(CullingPairFlow.Decision.KeepLeft).GroupFinished);  // pair 1
		Assert.False(flow.Advance(CullingPairFlow.Decision.KeepRight).GroupFinished); // pair 2
		Assert.True(flow.Advance(CullingPairFlow.Decision.KeepLeft).GroupFinished);   // pair 3 = last
		Assert.False(flow.HasPair);
	}

	[Fact]
	public void FullWalk_EveryLoserGetsCheckedExactlyOnce() {
		// 0 beats 1, loses to 2, 2 beats 3 → checked: 1, 0, 3; kept: 2.
		var flow = new CullingPairFlow(4);
		var checkedItems = new List<int>();
		void Track(CullingPairFlow.StepResult s) { if (s.CheckIndex >= 0) checkedItems.Add(s.CheckIndex); }

		Track(flow.Advance(CullingPairFlow.Decision.KeepLeft));
		Track(flow.Advance(CullingPairFlow.Decision.KeepRight));
		Track(flow.Advance(CullingPairFlow.Decision.KeepLeft));

		Assert.Equal(new[] { 1, 0, 3 }, checkedItems);
		Assert.Equal(2, flow.LeftIndex); // final keeper
	}

	[Fact]
	public void SetPair_ReanchorsTheWalk() {
		var flow = new CullingPairFlow(5);
		flow.SetPair(2, 3);
		Assert.Equal(2, flow.LeftIndex);
		Assert.Equal(3, flow.RightIndex);

		var step = flow.Advance(CullingPairFlow.Decision.KeepLeft);
		Assert.Equal(3, step.CheckIndex);
		Assert.Equal(4, flow.RightIndex);

		// Invalid picks are ignored.
		flow.SetPair(1, 1);
		Assert.Equal(2, flow.LeftIndex);
		flow.SetPair(-1, 2);
		Assert.Equal(2, flow.LeftIndex);
	}
}

public class ComparerChipsTests {

	static DuplicateItem Video(long size = 1000, int frameSizeInt = 1920 * 1080, string frameSize = "1920x1080",
		string? format = "HEVC", float fps = 24, string? audio = "AAC", int sampleRate = 48000) => new() {
			SizeLong = size,
			FrameSizeInt = frameSizeInt,
			FrameSize = frameSize,
			Format = format,
			Fps = fps,
			AudioFormat = audio,
			AudioSampleRate = sampleRate,
			Duration = new TimeSpan(0, 20, 26),
			DateCreated = new DateTime(2024, 4, 28),
			IsImage = false,
		};

	[Fact]
	public void EqualValues_StayNeutral() {
		var a = Video();
		var b = Video();
		var chips = ComparerChips.Build(a, b);
		Assert.All(chips, c => Assert.True(c.IsNeutral));
	}

	[Fact]
	public void HigherResolutionAndSize_TurnGreen_LowerRed() {
		var a = Video(size: 2000, frameSizeInt: 3840 * 2160, frameSize: "3840x2160");
		var b = Video(size: 1000);

		var chipsA = ComparerChips.Build(a, b);
		var chipsB = ComparerChips.Build(b, a);

		Assert.True(chipsA.Single(c => c.Text == "3840x2160").IsBetter);
		Assert.True(chipsA.Single(c => c.Text == a.Size).IsBetter);
		Assert.True(chipsB.Single(c => c.Text == "1920x1080").IsWorse);
		Assert.True(chipsB.Single(c => c.Text == b.Size).IsWorse);
	}

	[Fact]
	public void DurationCodecAudioDate_AreNeverJudged() {
		var a = Video(fps: 60);
		var b = Video(fps: 24);
		var chips = ComparerChips.Build(a, b);

		Assert.True(chips.Single(c => c.Text == "20:26").IsNeutral);
		Assert.True(chips.Single(c => c.Text.StartsWith("HEVC")).IsNeutral);
		Assert.True(chips.Single(c => c.Text.StartsWith("AAC")).IsNeutral);
	}

	[Fact]
	public void ImageItem_SkipsVideoOnlyChips() {
		var image = new DuplicateItem {
			SizeLong = 500, FrameSizeInt = 100, FrameSize = "800x600", IsImage = true,
			Format = "should-not-appear", AudioFormat = "should-not-appear",
		};
		var chips = ComparerChips.Build(image, null);

		Assert.DoesNotContain(chips, c => c.Text.Contains("should-not-appear"));
		Assert.Contains(chips, c => c.Text == "800x600");
	}

	[Fact]
	public void NoOther_AllNeutral() {
		var chips = ComparerChips.Build(Video(), null);
		Assert.All(chips, c => Assert.True(c.IsNeutral));
	}

	[Fact]
	public void HdrBeatsSdrAndLowerHdrRank() {
		var hdr = Video(); hdr.HdrFormat = "HDR10+";
		var sdr = Video(); // empty HdrFormat -> no chip, rank 0
		var chips = ComparerChips.Build(hdr, sdr);
		Assert.True(chips.Single(c => c.Text == "HDR10+").IsBetter);
	}

	[Theory]
	[InlineData(0, 20, 26, "20:26")]
	[InlineData(1, 2, 3, "1:02:03")]
	[InlineData(0, 0, 42, "0:42")]
	public void DurationFormat_MatchesMockup(int h, int m, int s, string expected) =>
		Assert.Equal(expected, ComparerChips.FormatDuration(new TimeSpan(h, m, s)));
}
