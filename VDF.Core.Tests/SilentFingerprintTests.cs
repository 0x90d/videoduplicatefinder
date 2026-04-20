// /*
//     Copyright (C) 2025 0x90d
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
// Regression tests for issue #719: videos with silent audio tracks produced
// all-zero Chromaprint fingerprints and were incorrectly grouped as partial
// duplicates because two all-zero fingerprints Hamming-match at 100%.

namespace VDF.Core.Tests;

public class SilentFingerprintTests {
	[Fact]
	public void IsSilentFingerprint_AllZeroBlocks_ReturnsTrue() {
		uint[] fp = new uint[10]; // default-initialised to 0
		Assert.True(ScanEngine.IsSilentFingerprint(fp));
	}

	[Fact]
	public void IsSilentFingerprint_SingleZeroBlock_ReturnsTrue() {
		uint[] fp = { 0u };
		Assert.True(ScanEngine.IsSilentFingerprint(fp));
	}

	[Fact]
	public void IsSilentFingerprint_EmptyArray_ReturnsFalse() {
		// Empty means "no fingerprint yet" (e.g. NoAudioTrack), not "silent"
		Assert.False(ScanEngine.IsSilentFingerprint(Array.Empty<uint>()));
	}

	[Fact]
	public void IsSilentFingerprint_AnyNonZeroBlock_ReturnsFalse() {
		uint[] fp = { 0u, 0u, 0u, 1u, 0u };
		Assert.False(ScanEngine.IsSilentFingerprint(fp));
	}

	[Fact]
	public void IsSilentFingerprint_AllNonZeroBlocks_ReturnsFalse() {
		uint[] fp = { 0xDEADBEEF, 0xCAFEBABE, 0x12345678 };
		Assert.False(ScanEngine.IsSilentFingerprint(fp));
	}

	[Fact]
	public void SlidingWindowCompare_TwoAllZeroFingerprints_FalsePositiveWithoutFilter() {
		// Demonstrates the bug behind issue #719: without the silent-fingerprint
		// filter, two unrelated silent videos look like a perfect match.
		// This test locks in the mathematical property so the filter stays necessary.
		uint[] silentA = new uint[8];
		uint[] silentB = new uint[20];
		var (similarity, _) = ScanEngine.SlidingWindowCompare(silentA, silentB);
		Assert.Equal(1.0f, similarity);
	}
}
