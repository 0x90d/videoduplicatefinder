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

// pHash mode used to compare only the FIRST sampled position's hash, so one
// coincidental frame (black intro, shared title card) matched two unrelated
// videos — and one divergent first frame hid a real duplicate. Matching now
// requires PHashRequiredMatchingSampleRatio of ALL sampled positions to pass
// the similarity threshold individually.
public class PHashQuorumTests {
	const int GraySize = 32 * 32;

	static ScanEngine Engine(float percent, float ratio, params float[] positions) {
		var engine = new ScanEngine {
			Settings = new Settings {
				UsePHashing = true,
				Percent = percent,
				PHashRequiredMatchingSampleRatio = ratio,
				ThumbnailCount = positions.Length,
			}
		};
		engine.positionList.Clear();
		engine.positionList.AddRange(positions);
		return engine;
	}

	// Duration 4s with positions given as fractions → grayBytes index = 4 * position.
	static FileEntry Entry(params (float Position, ulong PHash)[] samples) {
		var entry = new FileEntry {
			_Path = @"X:\video.mp4",
			Folder = @"X:\",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(4) },
			invalid = false,
		};
		foreach ((float position, ulong hash) in samples) {
			double index = 4d * position;
			entry.grayBytes[index] = new byte[GraySize];
			entry.PHashes[index] = hash;
		}
		return entry;
	}

	static bool Check(ScanEngine engine, FileEntry a, FileEntry b, out float difference) {
		Assert.True(engine.TryBuildCompareSnapshot(a, usePHashing: true));
		Assert.True(engine.TryBuildCompareSnapshot(b, usePHashing: true));
		return engine.CheckIfDuplicate(a, null, null, b, out difference);
	}

	[Fact]
	public void UsesAllSamples_NotJustTheFirst() {
		// First position identical, second maximally different. The old
		// first-sample-only compare reported this pair as duplicates.
		var engine = Engine(percent: 90f, ratio: 1f, 0.25f, 0.5f);
		var a = Entry((0.25f, 0UL), (0.5f, 0UL));
		var b = Entry((0.25f, 0UL), (0.5f, ulong.MaxValue));

		Assert.False(Check(engine, a, b, out _));
	}

	[Fact]
	public void QuorumCatchesPairsTheFirstSampleMisses() {
		// First position differs (old behavior: never a duplicate), but 3 of 4
		// positions match — quorum 60% needs ceil(4*0.6)=3.
		var engine = Engine(percent: 90f, ratio: 0.6f, 0.2f, 0.4f, 0.6f, 0.8f);
		var a = Entry((0.2f, ulong.MaxValue), (0.4f, 7UL), (0.6f, 7UL), (0.8f, 7UL));
		var b = Entry((0.2f, 0UL), (0.4f, 7UL), (0.6f, 7UL), (0.8f, 7UL));

		Assert.True(Check(engine, a, b, out float difference));
		Assert.Equal(0f, difference);
	}

	[Fact]
	public void RequiredSampleRatioIsHonored() {
		// 2 of 5 positions match: fails at 60% (needs 3), passes at 40% (needs 2).
		var engine = Engine(percent: 75f, ratio: 0.6f, 0.1f, 0.3f, 0.5f, 0.7f, 0.9f);
		var a = Entry((0.1f, 0UL), (0.3f, 0UL), (0.5f, 0UL), (0.7f, 0UL), (0.9f, 0UL));
		var b = Entry((0.1f, 0UL), (0.3f, 0UL), (0.5f, 0xFFFFFUL), (0.7f, 0xFFFFFUL), (0.9f, 0xFFFFFUL));

		Assert.False(Check(engine, a, b, out _));

		engine.Settings.PHashRequiredMatchingSampleRatio = 0.4f;
		Assert.True(engine.CheckIfDuplicate(a, null, null, b, out _));
	}

	[Fact]
	public void DifferenceIsAveragedOverMatchingSamples() {
		// Sample 1: identical (diff 0). Sample 2: 16 differing bits = similarity
		// 0.75, exactly at the 75% threshold (diff 0.25). Average = 0.125.
		var engine = Engine(percent: 75f, ratio: 1f, 0.25f, 0.5f);
		var a = Entry((0.25f, 0UL), (0.5f, 0UL));
		var b = Entry((0.25f, 0UL), (0.5f, 0xFFFFUL));

		Assert.True(Check(engine, a, b, out float difference));
		Assert.InRange(difference, 0.124f, 0.126f);
	}

	[Fact]
	public void SnapshotPrefillsEveryPositionsPHash() {
		// The compare snapshot must compute and cache the pHash of EVERY position
		// up front: the parallel per-pair hot path only reads comparePHashes, so
		// prefilling is what keeps it free of dictionary writes (thread safety).
		var engine = Engine(percent: 96f, ratio: 0.6f, 0.25f, 0.5f, 0.75f);
		var entry = new FileEntry {
			_Path = @"X:\video.mp4",
			Folder = @"X:\",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(4) },
			invalid = false,
		};
		foreach (float position in new[] { 0.25f, 0.5f, 0.75f })
			entry.grayBytes[4d * position] = new byte[GraySize];

		Assert.True(engine.TryBuildCompareSnapshot(entry, usePHashing: true));
		Assert.NotNull(entry.comparePHashes);
		Assert.Equal(3, entry.comparePHashes!.Length);
		foreach (float position in new[] { 0.25f, 0.5f, 0.75f })
			Assert.True(entry.PHashes.TryGetValue(4d * position, out ulong? cached) && cached.HasValue);
	}

	[Fact]
	public void LegacySmallGrayBytesAreExcludedInsteadOfCrashing() {
		// 16x16 data from a legacy database cannot be pHashed; the entry must be
		// dropped from the pHash comparison, not throw.
		var engine = Engine(percent: 96f, ratio: 0.6f, 0.5f);
		var entry = new FileEntry {
			_Path = @"X:\old.mp4",
			Folder = @"X:\",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(4) },
			invalid = false,
		};
		entry.grayBytes[2d] = new byte[16 * 16];

		Assert.False(engine.TryBuildCompareSnapshot(entry, usePHashing: true));
	}

	[Fact]
	public void FlippedPHashesComputePerPositionOrNotAtAll() {
		// The flip path precomputes one hash per position (once per entry, never
		// per pair). Any unhashable position disables the flip check entirely.
		var good = new byte[]?[] { new byte[GraySize], new byte[GraySize] };
		ulong[]? hashes = ScanEngine.ComputePHashesFromGray(good);
		Assert.NotNull(hashes);
		Assert.Equal(2, hashes!.Length);

		var legacy = new byte[]?[] { new byte[GraySize], new byte[16 * 16] };
		Assert.Null(ScanEngine.ComputePHashesFromGray(legacy));
	}
}
