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

	/// <summary>
	/// Bit-exact parity guard. The current implementation skips computing the 94% of DCT
	/// coefficients that are never read, but its scalar accumulation order matches a
	/// reference implementation that computes the full N×N DCT. This test pins that
	/// equivalence — any future change that reorders adds (e.g. SIMD vectorization)
	/// can produce slightly different floats, flip 1-2 bits at the median boundary,
	/// and silently invalidate cached PHashes from older scans. If you intend to break
	/// that compatibility, bump <c>DatabaseUtils.DbVersion</c> at the same time.
	/// </summary>
	[Fact]
	public void ComputePHash_MatchesFullDctReference() {
		var rng = new Random(20260501);
		for (int trial = 0; trial < 16; trial++) {
			byte[] gray = new byte[1024];
			rng.NextBytes(gray);

			ulong actual = PerceptualHash.ComputePHashFromGray32x32(gray);
			ulong expected = ReferenceFullDct(gray);

			Assert.Equal(expected, actual);
		}

		// Mirrors the previous N×N implementation: compute every DCT cell, then sweep the
		// 8×8 AC block, then median + bit set. Kept here as the spec for the production code.
		static ulong ReferenceFullDct(byte[] gray) {
			const int N = 32;
			const int K = 8;
			var cos = new float[N, N];
			for (int k = 0; k < N; k++)
				for (int i = 0; i < N; i++)
					cos[k, i] = (float)Math.Cos((2 * i + 1) * k * Math.PI / (2.0 * N));
			var alpha = new float[N];
			alpha[0] = (float)Math.Sqrt(1.0 / N);
			for (int k = 1; k < N; k++) alpha[k] = (float)Math.Sqrt(2.0 / N);

			var input = new float[N * N];
			for (int i = 0; i < input.Length; i++) input[i] = gray[i];
			var temp = new float[N * N];
			for (int y = 0; y < N; y++) {
				int yBase = y * N;
				for (int u = 0; u < N; u++) {
					float sum = 0f;
					for (int x = 0; x < N; x++) sum += input[yBase + x] * cos[u, x];
					temp[yBase + u] = alpha[u] * sum;
				}
			}
			var dct = new float[N * N];
			for (int u = 0; u < N; u++) {
				for (int v = 0; v < N; v++) {
					float sum = 0f;
					for (int y = 0; y < N; y++) sum += temp[y * N + u] * cos[v, y];
					dct[v * N + u] = alpha[v] * sum;
				}
			}
			Span<float> ac = stackalloc float[K * K];
			int kIdx = 0;
			for (int v = 1; v <= K; v++) {
				int vBase = v * N;
				for (int u = 1; u <= K; u++) ac[kIdx++] = dct[vBase + u];
			}
			var sortedAc = new float[ac.Length];
			ac.CopyTo(sortedAc);
			Array.Sort(sortedAc);
			float median = (sortedAc[31] + sortedAc[32]) * 0.5f;
			ulong hash = 0UL;
			for (int i = 0; i < ac.Length; i++)
				if (ac[i] > median) hash |= 1UL << i;
			return hash;
		}
	}
}
