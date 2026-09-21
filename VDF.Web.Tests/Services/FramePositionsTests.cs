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

using System.Globalization;
using VDF.Core;
using VDF.Core.ViewModels;
using VDF.Web.Services;

namespace VDF.Web.Tests.Services;

/// <summary>
/// Which moment of a file the Web UI shows: the frames the scan sampled, and for a
/// partial clip the same moment of clip and source.
/// </summary>
public sealed class FramePositionsTests {
	static DuplicateItem Video(double seconds, double? partialOffset = null) => new() {
		Path = @"C:\vdf-webtests\video.mp4",
		Duration = TimeSpan.FromSeconds(seconds),
		Flags = partialOffset.HasValue ? DuplicateFlags.PartialClip : DuplicateFlags.None,
		PartialClipOffset = TimeSpan.FromSeconds(partialOffset ?? 0),
	};

	static DuplicateItem Image() => new() { Path = @"C:\vdf-webtests\image.jpg", IsImage = true };

	static Settings Sampling(int frames, double limitSeconds = 0) =>
		new() { ThumbnailCount = frames, MaxSamplingDurationSeconds = limitSeconds };

	// Positions are accumulated floats: a hundredth of a second is far below one frame.
	static void AssertSeconds(double expected, TimeSpan actual) =>
		Assert.InRange(actual.TotalSeconds, expected - 0.01, expected + 0.01);

	// === The default frame (result cards) ===

	[Fact]
	public void DefaultFrame_WithOneSample_IsTheSampledMidpoint() =>
		AssertSeconds(30, FramePositions.DefaultTimestamp(Video(60), Sampling(1)));

	[Fact]
	public void DefaultFrame_IsTheMiddleOneOfTheSampledFrames() {
		// 50 samples: the first one sits at 2 percent, in the intro. The middle one
		// (index 24, 25/51 of the file) is the one that says something about the video.
		var at = FramePositions.DefaultTimestamp(Video(5100), Sampling(50));

		AssertSeconds(2500, at);
	}

	[Fact]
	public void DefaultFrame_StaysInsideTheSamplingLimit() =>
		// Only the first 20 seconds were sampled, so that is where the frame comes from.
		AssertSeconds(10, FramePositions.DefaultTimestamp(Video(600), Sampling(1, limitSeconds: 20)));

	[Fact]
	public void DefaultFrame_LimitLongerThanTheFile_ChangesNothing() =>
		AssertSeconds(30, FramePositions.DefaultTimestamp(Video(60), Sampling(1, limitSeconds: 600)));

	[Fact]
	public void Image_HasNoPosition() {
		Assert.Equal(TimeSpan.Zero, FramePositions.DefaultTimestamp(Image(), Sampling(5)));
		Assert.Equal(TimeSpan.Zero, FramePositions.Resolve(Image(), "12", Sampling(5)));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-3)]
	public void BrokenSampleCount_StillYieldsAFrame(int frames) {
		AssertSeconds(30, FramePositions.DefaultTimestamp(Video(60), Sampling(frames)));
		Assert.Equal(1, FramePositions.FrameCount(Video(60), Video(60), Sampling(frames)));
	}

	// === Frame count ===

	[Fact]
	public void FrameCount_IsTheNumberOfSampledFrames() =>
		Assert.Equal(50, FramePositions.FrameCount(Video(60), Video(61), Sampling(50)));

	[Fact]
	public void FrameCount_PartialClip_HasAtLeastTheThreeVerifiedMoments() {
		Assert.Equal(3, FramePositions.FrameCount(Video(600), Video(60, partialOffset: 100), Sampling(1)));
		Assert.Equal(8, FramePositions.FrameCount(Video(600), Video(60, partialOffset: 100), Sampling(8)));
	}

	[Fact]
	public void FrameCount_Images_IsOne() =>
		Assert.Equal(1, FramePositions.FrameCount(Image(), Image(), Sampling(50)));

	[Theory]
	[InlineData(1, 0)]
	[InlineData(2, 0)]
	[InlineData(3, 1)]
	[InlineData(50, 24)]
	public void MiddleFrame(int count, int expected) =>
		Assert.Equal(expected, FramePositions.MiddleFrame(count));

	// === Duplicates: same relative position ===

	[Fact]
	public void Duplicates_ShowTheSameRelativePositionOfEachFile() {
		// Compared at 25/50/75 percent of each file, whatever its length.
		var (a, b) = FramePositions.AlignedPair(Video(100), Video(120), 0, Sampling(3));

		AssertSeconds(25, a);
		AssertSeconds(30, b);
	}

	[Fact]
	public void Duplicates_IndexOutOfRange_IsClamped() {
		var first = FramePositions.AlignedPair(Video(100), Video(100), -5, Sampling(3));
		var last = FramePositions.AlignedPair(Video(100), Video(100), 99, Sampling(3));

		AssertSeconds(25, first.A);
		AssertSeconds(75, last.A);
	}

	// === Partial clips: same moment on the source's timeline ===

	[Fact]
	public void PartialClip_SourceIsShiftedByTheClipsOffset() {
		// The reporter's group: a 20:33 clip found at 20:33 of a 50:20 source.
		var source = Video(3020);
		var clip = Video(1233, partialOffset: 1233);

		var (s, c) = FramePositions.AlignedPair(source, clip, 1, Sampling(3));

		AssertSeconds(616.5, c); // middle of the clip
		AssertSeconds(1233 + 616.5, s); // the same moment in the source
	}

	[Fact]
	public void PartialClip_WorksWhicheverSideTheClipIsOn() {
		var source = Video(3020);
		var clip = Video(1233, partialOffset: 1233);

		var (c, s) = FramePositions.AlignedPair(clip, source, 0, Sampling(3));

		AssertSeconds(308.25, c);
		AssertSeconds(1233 + 308.25, s);
	}

	[Fact]
	public void PartialClip_AtOffsetZero_ShowsTheSameTimeOnBothSides() {
		// Offset 0 (the reporter's first group): relative positions would show 25 percent of
		// each file, which drift apart the longer the source is.
		var (s, c) = FramePositions.AlignedPair(Video(649), Video(602, partialOffset: 0), 0, Sampling(3));

		AssertSeconds(150.5, c);
		AssertSeconds(150.5, s);
	}

	[Fact]
	public void PartialClip_RunningPastTheSourcesEnd_OnlyStepsThroughTheSharedPart() {
		// Offset 90 + 60s clip in a 120s source: they share 30 seconds.
		var (s, c) = FramePositions.AlignedPair(Video(120), Video(60, partialOffset: 90), 2, Sampling(3));

		AssertSeconds(22.5, c);
		AssertSeconds(112.5, s);
	}

	[Fact]
	public void PartialClip_IgnoresTheSamplingLimit() {
		// The limit says which frames were hashed; a partial clip was not matched on those.
		var (s, c) = FramePositions.AlignedPair(Video(3020), Video(1233, partialOffset: 1233), 1, Sampling(3, limitSeconds: 20));

		AssertSeconds(616.5, c);
		AssertSeconds(1849.5, s);
	}

	[Fact]
	public void TwoClipsOfOneSource_ShowTheMomentTheyShare() {
		// On the source's timeline: 100..160 and 130..190, shared 130..160.
		var (a, b) = FramePositions.AlignedPair(Video(60, partialOffset: 100), Video(60, partialOffset: 130), 1, Sampling(3));

		AssertSeconds(45, a); // source 145
		AssertSeconds(15, b);
	}

	[Fact]
	public void TwoClipsThatShareNothing_FallBackToRelativePositions() {
		var (a, b) = FramePositions.AlignedPair(Video(60, partialOffset: 0), Video(40, partialOffset: 500), 1, Sampling(3));

		AssertSeconds(30, a);
		AssertSeconds(20, b);
	}

	// === The "t" of a thumbnail request ===

	[Fact]
	public void Resolve_TakesTheRequestedSecond() =>
		AssertSeconds(12.5, FramePositions.Resolve(Video(60), "12.50", Sampling(1)));

	[Fact]
	public void Resolve_ParsesInvariant_WhateverTheServersCulture() {
		var before = CultureInfo.CurrentCulture;
		try {
			// de-DE reads "12.50" as 1250.
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");
			AssertSeconds(12.5, FramePositions.Resolve(Video(60), "12.50", Sampling(1)));
			Assert.Equal("12.50", FramePositions.ToQueryValue(TimeSpan.FromSeconds(12.5)));
		}
		finally { CultureInfo.CurrentCulture = before; }
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("NaN")]
	[InlineData("Infinity")]
	public void Resolve_WithoutAUsableValue_IsTheDefaultFrame(string? requested) =>
		AssertSeconds(30, FramePositions.Resolve(Video(60), requested, Sampling(1)));

	[Fact]
	public void Resolve_KeepsThePositionInsideTheFile() {
		// Seeking onto or past the end yields no frame at all.
		AssertSeconds(59.9, FramePositions.Resolve(Video(60), "4000", Sampling(1)));
		AssertSeconds(0, FramePositions.Resolve(Video(60), "-5", Sampling(1)));
	}

	[Fact]
	public void QueryValue_RoundTrips() {
		var at = TimeSpan.FromSeconds(1849.5);

		Assert.Equal(at, FramePositions.Resolve(Video(3020), FramePositions.ToQueryValue(at), Sampling(1)));
	}
}
