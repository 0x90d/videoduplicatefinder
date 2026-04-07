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

namespace VDF.Core.Tests;

public class SlidingWindowCompareTests {
	[Fact]
	public void SlidingWindowCompare_IdenticalArrays_SimilarityOne() {
		uint[] a = { 0xAAAAAAAA, 0xBBBBBBBB, 0xCCCCCCCC };
		var (similarity, offset) = ScanEngine.SlidingWindowCompare(a, a);
		Assert.Equal(1.0f, similarity);
		Assert.Equal(0, offset);
	}

	[Fact]
	public void SlidingWindowCompare_CompletelyDifferent_LowSimilarity() {
		uint[] shorter = { 0x00000000, 0x00000000 };
		uint[] longer = { 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF };
		var (similarity, _) = ScanEngine.SlidingWindowCompare(shorter, longer);
		Assert.Equal(0.0f, similarity);
	}

	[Fact]
	public void SlidingWindowCompare_SubsetAtKnownOffset_FindsCorrectOffset() {
		// Build longer array with the shorter array embedded at offset 2
		uint[] shorter = { 0xDEADBEEF, 0xCAFEBABE };
		uint[] longer = { 0x00000000, 0x11111111, 0xDEADBEEF, 0xCAFEBABE, 0x22222222 };
		var (similarity, offset) = ScanEngine.SlidingWindowCompare(shorter, longer);
		Assert.Equal(1.0f, similarity);
		Assert.Equal(2, offset);
	}

	[Fact]
	public void SlidingWindowCompare_SingleElement() {
		uint[] shorter = { 0xAAAAAAAA };
		uint[] longer = { 0x00000000, 0xAAAAAAAA, 0x00000000 };
		var (similarity, offset) = ScanEngine.SlidingWindowCompare(shorter, longer);
		Assert.Equal(1.0f, similarity);
		Assert.Equal(1, offset);
	}

	[Fact]
	public void SlidingWindowCompare_SameLength_BehavesCorrectly() {
		uint[] a = { 0xAAAAAAAA, 0xBBBBBBBB };
		uint[] b = { 0xAAAAAAAA, 0xBBBBBBBB };
		// Same length: only one position to compare (offset 0)
		var (similarity, offset) = ScanEngine.SlidingWindowCompare(a, b);
		Assert.Equal(1.0f, similarity);
		Assert.Equal(0, offset);
	}
}
