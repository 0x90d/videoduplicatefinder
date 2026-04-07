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

using System.Numerics;
using VDF.Core.pHash;

namespace VDF.Core.Tests.pHash;

public class PerceptualHashTests {
	[Fact]
	public void ComputePHash_AllBlack_ConsistentHash() {
		byte[] black = new byte[1024]; // all zeros
		ulong hash1 = PerceptualHash.ComputePHashFromGray32x32(black);
		ulong hash2 = PerceptualHash.ComputePHashFromGray32x32(black);
		Assert.Equal(hash1, hash2);
	}

	[Fact]
	public void ComputePHash_AllWhite_ConsistentHash() {
		byte[] white = new byte[1024];
		Array.Fill(white, (byte)0xFF);
		ulong hash1 = PerceptualHash.ComputePHashFromGray32x32(white);
		ulong hash2 = PerceptualHash.ComputePHashFromGray32x32(white);
		Assert.Equal(hash1, hash2);
	}

	[Fact]
	public void ComputePHash_SameInput_SameHash() {
		byte[] img = new byte[1024];
		var rng = new Random(42);
		rng.NextBytes(img);
		ulong hash1 = PerceptualHash.ComputePHashFromGray32x32(img);
		ulong hash2 = PerceptualHash.ComputePHashFromGray32x32(img);
		Assert.Equal(hash1, hash2);
	}

	[Fact]
	public void ComputePHash_SlightChange_SimilarHash() {
		byte[] img = new byte[1024];
		var rng = new Random(42);
		rng.NextBytes(img);

		byte[] imgModified = (byte[])img.Clone();
		imgModified[512] = (byte)(imgModified[512] ^ 0x10); // flip one pixel slightly

		ulong hash1 = PerceptualHash.ComputePHashFromGray32x32(img);
		ulong hash2 = PerceptualHash.ComputePHashFromGray32x32(imgModified);

		int hammingDist = BitOperations.PopCount(hash1 ^ hash2);
		// A single pixel change should produce a very similar hash (small Hamming distance)
		Assert.True(hammingDist <= 10, $"Hamming distance {hammingDist} is too large for a 1-pixel change");
	}

	[Fact]
	public void ComputePHash_VeryDifferentInputs_DifferentHash() {
		// Horizontal gradient
		byte[] gradient = new byte[1024];
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++)
				gradient[y * 32 + x] = (byte)(x * 8);

		// Inverse gradient
		byte[] inverse = new byte[1024];
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++)
				inverse[y * 32 + x] = (byte)(255 - x * 8);

		ulong hash1 = PerceptualHash.ComputePHashFromGray32x32(gradient);
		ulong hash2 = PerceptualHash.ComputePHashFromGray32x32(inverse);

		int hammingDist = BitOperations.PopCount(hash1 ^ hash2);
		// Very different images should produce distant hashes
		Assert.True(hammingDist > 20, $"Hamming distance {hammingDist} is too small for very different images");
	}

	[Fact]
	public void ComputePHash_WrongLength_Throws() {
		byte[] tooSmall = new byte[512];
		Assert.Throws<ArgumentException>(() =>
			PerceptualHash.ComputePHashFromGray32x32(tooSmall));
	}
}
