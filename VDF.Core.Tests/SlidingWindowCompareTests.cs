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

	[Fact]
	public void SlidingWindowCompare_MinSimFiltersLowMatches() {
		// With minSim=0 we should find a low-similarity best match.
		// With a high minSim the result should be the same (just computed faster via early exit).
		uint[] shorter = { 0x00000000, 0x00000000 };
		uint[] longer = { 0x0000000F, 0x0000000F, 0xFFFFFFFF };

		var (simNoMin, offsetNoMin) = ScanEngine.SlidingWindowCompare(shorter, longer);
		var (simWithMin, offsetWithMin) = ScanEngine.SlidingWindowCompare(shorter, longer, minSim: 0f);

		// Both must produce identical results — minSim only affects performance, not correctness
		Assert.Equal(simNoMin, simWithMin);
		Assert.Equal(offsetNoMin, offsetWithMin);
	}

	[Fact]
	public void SlidingWindowCompare_MinSimDoesNotAlterResult() {
		// Embedded match at offset 3 with noise around it.
		// A high minSim should still find the exact match via early exit skipping bad offsets.
		uint[] shorter = { 0xDEADBEEF, 0xCAFEBABE };
		uint[] longer = { 0xFFFFFFFF, 0x11111111, 0x22222222, 0xDEADBEEF, 0xCAFEBABE, 0x33333333 };

		var (sim, offset) = ScanEngine.SlidingWindowCompare(shorter, longer, minSim: 0.9f);
		Assert.Equal(1.0f, sim);
		Assert.Equal(3, offset);
	}

	[Fact]
	public void SlidingWindowCompare_LargeArrays_ExercisesSimdPath() {
		// Arrays large enough to exercise Vector256 (8+ elements) and Vector128 (4+) paths.
		// Embed a 16-element shorter array inside a 32-element longer array at offset 10.
		var rng = new Random(42);
		uint[] longer = new uint[32];
		for (int i = 0; i < longer.Length; i++)
			longer[i] = (uint)rng.Next();

		uint[] shorter = new uint[16];
		Array.Copy(longer, 10, shorter, 0, shorter.Length);

		var (similarity, offset) = ScanEngine.SlidingWindowCompare(shorter, longer);
		Assert.Equal(1.0f, similarity);
		Assert.Equal(10, offset);
	}

	[Fact]
	public void SlidingWindowCompare_LargeArrays_WithMinSim_SameResult() {
		// Same large-array test but with minSim — result must be identical.
		var rng = new Random(42);
		uint[] longer = new uint[32];
		for (int i = 0; i < longer.Length; i++)
			longer[i] = (uint)rng.Next();

		uint[] shorter = new uint[16];
		Array.Copy(longer, 10, shorter, 0, shorter.Length);

		var (sim1, off1) = ScanEngine.SlidingWindowCompare(shorter, longer);
		var (sim2, off2) = ScanEngine.SlidingWindowCompare(shorter, longer, minSim: 0.8f);

		Assert.Equal(sim1, sim2);
		Assert.Equal(off1, off2);
	}

	[Fact]
	public void SlidingWindowCompare_LargeArrays_NearMatch_CorrectSimilarity() {
		// Create a near-match by flipping a few bits in the embedded region.
		var rng = new Random(123);
		uint[] longer = new uint[40];
		for (int i = 0; i < longer.Length; i++)
			longer[i] = (uint)rng.Next();

		uint[] shorter = new uint[20];
		Array.Copy(longer, 15, shorter, 0, shorter.Length);
		// Flip 1 bit in each of the first 4 blocks (4 bits out of 20*32=640 total)
		for (int i = 0; i < 4; i++)
			shorter[i] ^= 1u;

		var (similarity, offset) = ScanEngine.SlidingWindowCompare(shorter, longer);
		Assert.Equal(15, offset);
		// 4 bits differ out of 640: similarity = 1 - 4/640 = 0.99375
		Assert.True(similarity > 0.99f);
		Assert.True(similarity < 1.0f);
	}
}
