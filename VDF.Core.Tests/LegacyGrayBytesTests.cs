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

namespace VDF.Core.Tests;

// Regression tests for #881: a database migrated from the 16x16 era carries 256-byte
// gray frames verbatim. Every "already sampled" gate keys on position presence, so the
// legacy frames were reused forever - and with pHash disabled (the only mode that
// checked sizes) the first legacy-vs-modern pair made PercentageDifference read past
// the end of the shorter buffer, aborting the whole scan with IndexOutOfRangeException.
public class LegacyGrayBytesTests {
	const int ModernSize = 32 * 32;
	const int LegacySize = 16 * 16;

	static ScanEngine Engine() {
		var engine = new ScanEngine {
			Settings = new Settings {
				UsePHashing = false,
				CombineGrayscaleAndPHash = false,
				Percent = 96f,
				ThumbnailCount = 1,
			}
		};
		engine.positionList.Clear();
		engine.positionList.Add(0.5f);
		return engine;
	}

	static FileEntry Video(string name, int graySize) {
		var entry = new FileEntry {
			_Path = @"X:\" + name,
			Folder = @"X:\",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(4) },
			invalid = false,
		};
		entry.grayBytes[4d * 0.5f] = new byte[graySize];
		return entry;
	}

	static FileEntry Image(string name, int graySize) {
		var entry = new FileEntry {
			_Path = @"X:\" + name,
			Folder = @"X:\",
			invalid = false,
		};
		entry.Flags.Set(EntryFlags.IsImage);
		entry.grayBytes[0] = new byte[graySize];
		return entry;
	}

	[Fact]
	public void Snapshot_RejectsLegacyGraySize_EvenWithoutPHash() {
		// The exact #881 setup: pHash off, so the old validation (which only lived in
		// the pHash branch) never ran and the legacy entry reached the compare loop.
		var engine = Engine();
		Assert.True(engine.TryBuildCompareSnapshot(Video("modern.mp4", ModernSize), usePHashing: false));
		Assert.False(engine.TryBuildCompareSnapshot(Video("legacy.mp4", LegacySize), usePHashing: false));
	}

	[Fact]
	public void Snapshot_RejectsLegacyGraySize_ForImages() {
		var engine = Engine();
		Assert.True(engine.TryBuildCompareSnapshot(Image("modern.jpg", ModernSize), usePHashing: false));
		Assert.False(engine.TryBuildCompareSnapshot(Image("legacy.jpg", LegacySize), usePHashing: false));
	}

	[Fact]
	public void PercentageDifference_MismatchedSizes_ThrowsAClearError() {
		// Pre-fix: the longer-first order crashed with an opaque IndexOutOfRangeException
		// from inside the SIMD loop, and the shorter-first order silently compared only a
		// prefix. Both now fail loudly with an explanation.
		Assert.Throws<ArgumentException>(() =>
			GrayBytesUtils.PercentageDifference(new byte[ModernSize], new byte[LegacySize]));
		Assert.Throws<ArgumentException>(() =>
			GrayBytesUtils.PercentageDifference(new byte[LegacySize], new byte[ModernSize]));
		Assert.Throws<ArgumentException>(() =>
			GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(new byte[ModernSize], new byte[LegacySize], true, false));
	}

	[Fact]
	public void Heal_RemovesLegacyFramesAndTheirPHashes_SoTheyResample() {
		var entry = Video("mixed.mp4", ModernSize);
		entry.grayBytes[1.0] = new byte[LegacySize];
		entry.PHashes[1.0] = 42UL; // computed from the legacy frame - stale with it
		entry.grayBytes[3.0] = null; // "sampled and failed" marker - retry policy owns it
		entry.PHashes[2.0] = 7UL;

		ScanEngine.HealLegacyGrayBytes(entry);

		Assert.False(entry.grayBytes.ContainsKey(1.0));
		Assert.False(entry.PHashes.ContainsKey(1.0));
		Assert.True(entry.grayBytes.ContainsKey(4d * 0.5f), "the modern frame must survive");
		Assert.True(entry.grayBytes.ContainsKey(3.0), "failure markers must survive - only wrong-sized data heals");
		Assert.True(entry.PHashes.ContainsKey(2.0));
	}

	[Fact]
	public void CachedGrayGate_TreatsLegacyFramesAsUnusable_ButKeepsFailureMarkerSemantics() {
		var entry = Video("gate.mp4", ModernSize);
		double idx = 4d * 0.5f;
		Assert.True(ScanEngine.HasUsableCachedGray(entry, idx));

		entry.grayBytes[idx] = new byte[LegacySize];
		Assert.False(ScanEngine.HasUsableCachedGray(entry, idx));

		entry.grayBytes[idx] = null; // sampled-and-failed stays "usable" so it is not retried here
		Assert.True(ScanEngine.HasUsableCachedGray(entry, idx));

		Assert.False(ScanEngine.HasUsableCachedGray(entry, 99.0));
	}
}
