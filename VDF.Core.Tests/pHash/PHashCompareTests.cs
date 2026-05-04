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

using VDF.Core.pHash;

namespace VDF.Core.Tests.pHash;

public class PHashCompareTests {
	[Fact]
	public void IsDuplicateByPercent_IdenticalHashes_ReturnsTrue() {
		ulong hash = 0xDEADBEEF_CAFEBABE;
		bool result = PHashCompare.IsDuplicateByPercent(hash, hash, out _, percent: 0.90);
		Assert.True(result);
	}

	[Fact]
	public void IsDuplicateByPercent_IdenticalHashes_SimilarityIsOne() {
		ulong hash = 0xDEADBEEF_CAFEBABE;
		PHashCompare.IsDuplicateByPercent(hash, hash, out float similarity);
		Assert.Equal(1.0f, similarity);
	}

	[Fact]
	public void IsDuplicateByPercent_CompletelyDifferent_ReturnsFalse() {
		bool result = PHashCompare.IsDuplicateByPercent(0UL, ulong.MaxValue, out _);
		Assert.False(result);
	}

	[Fact]
	public void IsDuplicateByPercent_CompletelyDifferent_SimilarityIsZero() {
		PHashCompare.IsDuplicateByPercent(0UL, ulong.MaxValue, out float similarity);
		Assert.Equal(0.0f, similarity);
	}

	[Fact]
	public void IsDuplicateByPercent_OneBitDiff_At90Pct_ReturnsTrue() {
		// 1 bit difference out of 64 = 98.4% similarity
		ulong a = 0UL;
		ulong b = 1UL; // differs in bit 0 only
		bool result = PHashCompare.IsDuplicateByPercent(a, b, out float similarity, percent: 0.90);
		Assert.True(result);
		Assert.Equal(63f / 64f, similarity);
	}

	[Fact]
	public void IsDuplicateByPercent_InvalidPercent_Negative_Throws() {
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PHashCompare.IsDuplicateByPercent(0UL, 0UL, out _, percent: -0.1));
	}

	[Fact]
	public void IsDuplicateByPercent_InvalidPercent_OverOne_Throws() {
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PHashCompare.IsDuplicateByPercent(0UL, 0UL, out _, percent: 1.1));
	}

	[Fact]
	public void IsDuplicateByPercent_ZeroPercent_AlwaysTrue() {
		// 0% threshold means everything matches
		bool result = PHashCompare.IsDuplicateByPercent(0UL, ulong.MaxValue, out _, percent: 0.0);
		Assert.True(result);
	}

	[Theory]
	[InlineData(0UL, 0UL, 1.0f)]           // identical
	[InlineData(0UL, 1UL, 63f / 64f)]      // 1 bit different
	[InlineData(0UL, 3UL, 62f / 64f)]      // 2 bits different (bits 0 and 1)
	[InlineData(0UL, 0xFFUL, 56f / 64f)]   // 8 bits different
	public void IsDuplicateByPercent_KnownHammingDistances_CorrectSimilarity(ulong a, ulong b, float expectedSimilarity) {
		PHashCompare.IsDuplicateByPercent(a, b, out float similarity);
		Assert.Equal(expectedSimilarity, similarity, precision: 5);
	}

	[Fact]
	public void IsDuplicateByPercent_StrictVsNonStrict_BoundaryBehavior() {
		// With percent=0.90, allowable bits = (1-0.90)*64 = 6.4
		// strict (floor): maxBits = 6, so 6 bits diff should pass, 7 should fail
		// non-strict (round): maxBits = 6, same result here since round(6.4) = 6

		// Create values with exactly 6 bits different
		ulong a = 0UL;
		ulong b = 0b111111UL; // 6 bits set
		bool strict = PHashCompare.IsDuplicateByPercent(a, b, out _, percent: 0.90, strict: true);
		bool nonStrict = PHashCompare.IsDuplicateByPercent(a, b, out _, percent: 0.90, strict: false);
		Assert.True(strict);
		Assert.True(nonStrict);

		// 7 bits different - should fail both
		ulong c = 0b1111111UL; // 7 bits set
		bool strict7 = PHashCompare.IsDuplicateByPercent(a, c, out _, percent: 0.90, strict: true);
		bool nonStrict7 = PHashCompare.IsDuplicateByPercent(a, c, out _, percent: 0.90, strict: false);
		Assert.False(strict7);
		Assert.False(nonStrict7);
	}

	[Fact]
	public void IsDuplicateByPercent_100Percent_OnlyExactMatch() {
		ulong a = 0xDEADBEEF_CAFEBABE;
		ulong b = a ^ 1UL; // 1 bit different
		bool exact = PHashCompare.IsDuplicateByPercent(a, a, out _, percent: 1.0);
		bool oneBitOff = PHashCompare.IsDuplicateByPercent(a, b, out _, percent: 1.0);
		Assert.True(exact);
		Assert.False(oneBitOff);
	}
}
