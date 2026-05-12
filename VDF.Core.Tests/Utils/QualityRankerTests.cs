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

using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class QualityRankerTests {

	sealed record Item(string Name, int Resolution, TimeSpan Duration, decimal Bitrate, float Fps, int AudioSampleRate, bool IsImage = false);
	sealed record Sized(string Name, int Resolution, long Size);

	static QualityRanker.Criterion<Item> Resolution = new("Resolution", i => i.Resolution, videoOnly: false);
	static QualityRanker.Criterion<Item> Duration = new("Duration", i => i.Duration, videoOnly: true);
	static QualityRanker.Criterion<Item> Bitrate = new("Bitrate", i => i.Bitrate, videoOnly: true);
	static QualityRanker.Criterion<Item> Fps = new("FPS", i => i.Fps, videoOnly: true);
	static QualityRanker.Criterion<Item> AudioBitrate = new("Audio Bitrate", i => i.AudioSampleRate, videoOnly: true);

	// Regression test for issue #746: when the top-ranked criterion produces a tie,
	// the next criterion must run only against the tied subset - not the original full list.
	// Pre-fix, this returned the 720p item because it had the longest duration overall.
	[Fact]
	public void Issue746_TieBreaker_DoesNotPickItemThatLostOnTopCriterion() {
		var a = new Item("A_1080p_30s", Resolution: 3000, Duration: TimeSpan.FromSeconds(30), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);
		var b = new Item("B_1080p_60s", Resolution: 3000, Duration: TimeSpan.FromSeconds(60), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);
		var c = new Item("C_720p_90s",  Resolution: 2000, Duration: TimeSpan.FromSeconds(90), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);

		var keeper = QualityRanker.PickKeeper(new[] { a, b, c }, new[] { Resolution, Duration }, _ => false);

		Assert.Equal(b, keeper);
	}

	[Fact]
	public void TopCriterion_HasUniqueWinner_NoTieBreakerNeeded() {
		var a = new Item("A_4k", Resolution: 5000, Duration: TimeSpan.FromSeconds(10), Bitrate: 1000, Fps: 30, AudioSampleRate: 44100);
		var b = new Item("B_1080p", Resolution: 3000, Duration: TimeSpan.FromSeconds(99), Bitrate: 9999, Fps: 60, AudioSampleRate: 48000);

		var keeper = QualityRanker.PickKeeper(new[] { a, b }, new[] { Resolution, Duration, Bitrate }, _ => false);

		Assert.Equal(a, keeper);
	}

	[Fact]
	public void TieCascadesThroughMultipleCriteria_NarrowsAtEachStep() {
		// All three tie on Resolution. A and B tie on Duration; C is shorter.
		// Bitrate then breaks the A/B tie. C must not win even though it has the highest Bitrate overall.
		var a = new Item("A", Resolution: 3000, Duration: TimeSpan.FromSeconds(60), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);
		var b = new Item("B", Resolution: 3000, Duration: TimeSpan.FromSeconds(60), Bitrate: 7000, Fps: 30, AudioSampleRate: 44100);
		var c = new Item("C", Resolution: 3000, Duration: TimeSpan.FromSeconds(10), Bitrate: 9999, Fps: 30, AudioSampleRate: 44100);

		var keeper = QualityRanker.PickKeeper(new[] { a, b, c }, new[] { Resolution, Duration, Bitrate }, _ => false);

		Assert.Equal(b, keeper);
	}

	[Fact]
	public void AllCriteriaTied_ReturnsFirstItem() {
		var a = new Item("A", Resolution: 3000, Duration: TimeSpan.FromSeconds(30), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);
		var b = new Item("B", Resolution: 3000, Duration: TimeSpan.FromSeconds(30), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);

		var keeper = QualityRanker.PickKeeper(new[] { a, b }, new[] { Resolution, Duration, Bitrate, Fps, AudioBitrate }, _ => false);

		Assert.Equal(a, keeper);
	}

	[Fact]
	public void VideoOnlyCriteria_AreSkipped_WhenKeepIsImage() {
		// Two images tied on Resolution. Duration/Bitrate/etc. are video-only and must be ignored
		// even though their numeric values would otherwise pick a different "winner".
		// With all video criteria skipped, the first item in the tied set wins.
		var imgA = new Item("A_jpg", Resolution: 3000, Duration: TimeSpan.Zero, Bitrate: 0, Fps: 0, AudioSampleRate: 0, IsImage: true);
		var imgB = new Item("B_jpg", Resolution: 3000, Duration: TimeSpan.FromSeconds(99), Bitrate: 9999, Fps: 999, AudioSampleRate: 99999, IsImage: true);

		var keeper = QualityRanker.PickKeeper(new[] { imgA, imgB }, new[] { Resolution, Duration, Bitrate, Fps }, i => i.IsImage);

		Assert.Equal(imgA, keeper);
	}

	[Fact]
	public void EmptyItems_Throws() {
		Assert.Throws<ArgumentException>(() =>
			QualityRanker.PickKeeper(Array.Empty<Item>(), new[] { Resolution }, _ => false));
	}

	[Fact]
	public void SingleItem_ReturnsThatItem() {
		var only = new Item("only", Resolution: 1000, Duration: TimeSpan.FromSeconds(1), Bitrate: 100, Fps: 24, AudioSampleRate: 22050);
		var keeper = QualityRanker.PickKeeper(new[] { only }, new[] { Resolution, Duration }, _ => false);
		Assert.Equal(only, keeper);
	}

	[Fact]
	public void NoCriteriaProvided_ReturnsFirstItem() {
		var a = new Item("A", 3000, TimeSpan.FromSeconds(30), 5000, 30, 44100);
		var b = new Item("B", 9999, TimeSpan.FromSeconds(99), 9999, 60, 48000);

		var keeper = QualityRanker.PickKeeper(new[] { a, b }, Array.Empty<QualityRanker.Criterion<Item>>(), _ => false);

		Assert.Equal(a, keeper);
	}

	[Fact]
	public void CriterionOrderMatters_DurationFirstFlipsResult() {
		// Resolution-first → A wins; Duration-first → B wins.
		var a = new Item("A_high_res", Resolution: 5000, Duration: TimeSpan.FromSeconds(30), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);
		var b = new Item("B_long",     Resolution: 3000, Duration: TimeSpan.FromSeconds(99), Bitrate: 5000, Fps: 30, AudioSampleRate: 44100);

		Assert.Equal(a, QualityRanker.PickKeeper(new[] { a, b }, new[] { Resolution, Duration }, _ => false));
		Assert.Equal(b, QualityRanker.PickKeeper(new[] { a, b }, new[] { Duration, Resolution }, _ => false));
	}

	// Regression test for issue #765: when used as a final tiebreaker, the Size criterion
	// is intentionally ascending - smaller file wins. The premise is that every preceding
	// quality signal has already tied, so the larger file is just disk-space overhead.
	[Fact]
	public void AscendingCriterion_PicksSmallerValue() {
		var big   = new Sized("big",   Resolution: 3000, Size: 10_000_000);
		var small = new Sized("small", Resolution: 3000, Size:  2_000_000);

		var resolution = new QualityRanker.Criterion<Sized>("Resolution", i => i.Resolution, videoOnly: false);
		var size       = new QualityRanker.Criterion<Sized>("Size",       i => i.Size,       videoOnly: false, ascending: true);

		var keeper = QualityRanker.PickKeeper(new[] { big, small }, new[] { resolution, size }, _ => false);

		Assert.Equal(small, keeper);
	}
}
