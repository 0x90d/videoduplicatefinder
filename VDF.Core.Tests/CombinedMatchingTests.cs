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

using VDF.Core;

namespace VDF.Core.Tests;

// #842: the combined mode runs BOTH classic algorithms in one comparison pass.
// Either match makes the pair a duplicate; DuplicateFlags record which algorithm(s)
// found it, and when both did the better (smaller) difference wins - the same rule
// the flip-vs-normal selection uses. The stored pHash cache is deliberately fed
// contradictory values here so the two algorithms can disagree on the same pair.
public class CombinedMatchingTests {
	const int GraySize = 32 * 32;

	static ScanEngine Engine(bool combined = true, float percent = 96f) {
		var engine = new ScanEngine {
			Settings = new Settings {
				CombineGrayscaleAndPHash = combined,
				UsePHashing = false,
				Percent = percent,
				PHashRequiredMatchingSampleRatio = 1f,
				ThumbnailCount = 1,
			}
		};
		engine.positionList.Clear();
		engine.positionList.Add(0.5f);
		return engine;
	}

	static FileEntry Video(string name, byte grayFill, ulong pHash) {
		var entry = new FileEntry {
			_Path = @"X:\" + name,
			Folder = @"X:\",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(4) },
			invalid = false,
		};
		double index = 4d * 0.5f;
		var gray = new byte[GraySize];
		Array.Fill(gray, grayFill);
		entry.grayBytes[index] = gray;
		entry.PHashes[index] = pHash; // cached value wins over computing from gray
		return entry;
	}

	static bool Check(ScanEngine engine, FileEntry a, FileEntry b, out float difference, out DuplicateFlags algorithms) {
		Assert.True(engine.TryBuildCompareSnapshot(a, usePHashing: true));
		Assert.True(engine.TryBuildCompareSnapshot(b, usePHashing: true));
		return engine.CheckIfDuplicate(a, null, null, b, out difference, out _, out algorithms);
	}

	[Fact]
	public void GrayscaleOnlyMatch_FlagsGrayscale() {
		var engine = Engine();
		// Identical frames, maximally divergent cached hashes: only grayscale agrees.
		var a = Video("a.mp4", grayFill: 7, pHash: 0UL);
		var b = Video("b.mp4", grayFill: 7, pHash: ulong.MaxValue);

		Assert.True(Check(engine, a, b, out float difference, out DuplicateFlags algorithms));
		Assert.Equal(DuplicateFlags.GrayscaleMatched, algorithms);
		Assert.Equal(0f, difference);
	}

	[Fact]
	public void PHashOnlyMatch_FlagsPHash() {
		var engine = Engine();
		// Maximally divergent frames, identical cached hashes: only pHash agrees.
		var a = Video("a.mp4", grayFill: 0, pHash: 7UL);
		var b = Video("b.mp4", grayFill: 255, pHash: 7UL);

		Assert.True(Check(engine, a, b, out float difference, out DuplicateFlags algorithms));
		Assert.Equal(DuplicateFlags.PHashMatched, algorithms);
		Assert.Equal(0f, difference);
	}

	[Fact]
	public void BothMatch_FlagsBoth_AndReportsTheSmallerDifference() {
		var engine = Engine();
		// Slightly different frames (diff ~0.039, inside the 4% budget) and identical
		// hashes (diff 0): both match, the reported difference is the better one.
		var a = Video("a.mp4", grayFill: 0, pHash: 7UL);
		var b = Video("b.mp4", grayFill: 10, pHash: 7UL);

		Assert.True(Check(engine, a, b, out float difference, out DuplicateFlags algorithms));
		Assert.Equal(DuplicateFlags.GrayscaleMatched | DuplicateFlags.PHashMatched, algorithms);
		Assert.Equal(0f, difference);
	}

	[Fact]
	public void NeitherMatches_NotADuplicate() {
		var engine = Engine();
		var a = Video("a.mp4", grayFill: 0, pHash: 0UL);
		var b = Video("b.mp4", grayFill: 255, pHash: ulong.MaxValue);

		Assert.False(Check(engine, a, b, out _, out DuplicateFlags algorithms));
		Assert.Equal(DuplicateFlags.None, algorithms);
	}

	[Fact]
	public void CombinedOff_SingleAlgorithmScansCarryNoAlgorithmFlags() {
		// Single-algorithm runs would badge every group identically - flags stay None.
		var engine = Engine(combined: false);
		var a = Video("a.mp4", grayFill: 7, pHash: 7UL);
		var b = Video("b.mp4", grayFill: 7, pHash: 7UL);

		Assert.True(Check(engine, a, b, out _, out DuplicateFlags algorithms));
		Assert.Equal(DuplicateFlags.None, algorithms);
	}

	[Fact]
	public void Images_AlwaysCompareByGrayscale_AndBadgeSaysSo() {
		var engine = Engine();
		var a = Image("a.jpg", grayFill: 7);
		var b = Image("b.jpg", grayFill: 7);

		Assert.True(engine.TryBuildCompareSnapshot(a, usePHashing: true));
		Assert.True(engine.TryBuildCompareSnapshot(b, usePHashing: true));
		Assert.True(engine.CheckIfDuplicate(a, null, null, b, out _, out _, out DuplicateFlags algorithms));
		Assert.Equal(DuplicateFlags.GrayscaleMatched, algorithms);
	}

	static FileEntry Image(string name, byte grayFill) {
		var entry = new FileEntry {
			_Path = @"X:\" + name,
			Folder = @"X:\",
			invalid = false,
		};
		entry.Flags.Set(EntryFlags.IsImage);
		var gray = new byte[GraySize];
		Array.Fill(gray, grayFill);
		entry.grayBytes[0] = gray;
		return entry;
	}
}
