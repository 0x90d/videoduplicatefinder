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
		// A textured frame and its negative. The input must carry energy in every cell of
		// the 8x8 AC block: for a linear gradient a*x + b*y the 2D DCT only has energy at
		// (u,0) and (0,v), so the block pHash reads (u,v >= 1) is mathematically zero and
		// the hash is float rounding noise whose pattern depends on the SIMD width. The
		// previous horizontal-gradient input asserted on exactly that noise. Separable
		// symmetric patterns (sin(x)*sin(y)) are nearly as bad: their odd harmonics vanish.
		byte[] textured = Blur(RandomBytes(new Random(7)), 2);
		byte[] inverse = new byte[1024];
		for (int i = 0; i < 1024; i++) inverse[i] = (byte)(255 - textured[i]);

		ulong hash1 = PerceptualHash.ComputePHashFromGray32x32(textured);
		ulong hash2 = PerceptualHash.ComputePHashFromGray32x32(inverse);

		int hammingDist = BitOperations.PopCount(hash1 ^ hash2);
		// Negating the input negates every AC coefficient, so nearly every bit flips.
		Assert.True(hammingDist > 20, $"Hamming distance {hammingDist} is too small for very different images");
	}

	[Fact]
	public void ComputePHash_WrongLength_Throws() {
		byte[] tooSmall = new byte[512];
		Assert.Throws<ArgumentException>(() =>
			PerceptualHash.ComputePHashFromGray32x32(tooSmall));
	}

	static byte[] RandomBytes(Random r) { byte[] b = new byte[1024]; r.NextBytes(b); return b; }
	static byte[] Blur(byte[] src, int radius) {
		byte[] dst = new byte[1024];
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++) {
				int sum = 0, cnt = 0;
				for (int dy = -radius; dy <= radius; dy++)
					for (int dx = -radius; dx <= radius; dx++) {
						int yy = y + dy, xx = x + dx;
						if (yy < 0 || yy >= 32 || xx < 0 || xx >= 32) continue;
						sum += src[yy * 32 + xx];
						cnt++;
					}
				dst[y * 32 + x] = (byte)(sum / cnt);
			}
		return dst;
	}

	static readonly string[] DriftProneClasses = { "blurred-noise", "smooth-lowfreq", "low-contrast", "dark-fade", "gradient+noise" };

	/// <summary>Synthetic stand-ins for natural frames; every class is low-frequency dominated.</summary>
	static byte[] Generate(string cls, Random rng) {
		switch (cls) {
			case "blurred-noise": return Blur(RandomBytes(rng), 2);
			case "smooth-lowfreq": return Blur(RandomBytes(rng), 5);
			case "low-contrast": {
				byte[] b = new byte[1024];
				int baseValue = rng.Next(0, 240);
				for (int p = 0; p < 1024; p++) b[p] = (byte)(baseValue + rng.Next(0, 12));
				return Blur(b, 2);
			}
			case "dark-fade": {
				byte[] b = Blur(RandomBytes(rng), 3);
				for (int p = 0; p < 1024; p++) b[p] = (byte)(b[p] / 12);
				return b;
			}
			case "gradient+noise": {
				byte[] b = new byte[1024];
				double ax = rng.NextDouble() * 8 - 4, ay = rng.NextDouble() * 8 - 4;
				for (int y = 0; y < 32; y++)
					for (int x = 0; x < 32; x++)
						b[y * 32 + x] = (byte)Math.Clamp(128 + ax * (x - 16) + ay * (y - 16) + rng.Next(-6, 7), 0, 255);
				return b;
			}
			default: throw new ArgumentException(cls);
		}
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

	/// <summary>
	/// Inputs found by a seed search that DO hash differently from the full-DCT reference
	/// on at least one x64 SIMD width (AVX-512, AVX2 or SSE; the scalar path is exact).
	/// They prove the 2-bit tolerance in <see cref="ComputePHash_MatchesFullDctReference"/>
	/// is exercised rather than vacuous, and pin its upper bound on real drift cases.
	/// Each input is generated from its own <c>new Random(seed)</c>, so the cases are
	/// independent of corpus order.
	/// </summary>
	[Theory]
	[InlineData("low-contrast", 3669)]     // drifts on AVX-512, AVX2 and SSE
	[InlineData("low-contrast", 7725)]     // drifts on AVX-512, AVX2 and SSE
	[InlineData("low-contrast", 24499)]    // drifts on AVX-512, AVX2 and SSE
	[InlineData("low-contrast", 28673)]    // drifts on AVX-512, AVX2 and SSE
	[InlineData("smooth-lowfreq", 5581)]   // drifts on AVX-512, AVX2 and SSE
	[InlineData("smooth-lowfreq", 16848)]  // drifts on AVX-512, AVX2 and SSE
	[InlineData("gradient+noise", 24027)]  // drifts on AVX2 and SSE
	[InlineData("gradient+noise", 24138)]  // drifts on AVX-512 and AVX2
	[InlineData("dark-fade", 22161)]       // drifts on AVX-512
	[InlineData("dark-fade", 2511)]        // drifts on SSE
	[InlineData("blurred-noise", 7071)]    // drifts on AVX2 (1 bit)
	public void ComputePHash_KnownSimdDriftInputs_WithinTolerance(string cls, int seed) {
		byte[] gray = Generate(cls, new Random(seed));
		ulong actual = PerceptualHash.ComputePHashFromGray32x32(gray);
		ulong expected = ReferenceFullDct(gray);
		int distance = BitOperations.PopCount(expected ^ actual);
		Assert.True(distance <= 2, $"{cls}/{seed}: Hamming distance {distance} to the full-DCT reference exceeds the 2-bit tolerance");
	}

	/// <summary>
	/// Tolerance guard against the original full N×N DCT implementation. The production
	/// code computes only the 8×8 cells that are read and accumulates with
	/// <c>TensorPrimitives.Dot</c>, whose float summation order depends on the SIMD width
	/// of the machine (AVX-512 / AVX2 / SSE / NEON / scalar). Measured over 112k frames of
	/// every class below, at most 0.01% of hashes differ from the reference and never by
	/// more than 2 bits; the scalar path is bit-identical. Hashes are persisted in the scan
	/// database, so this is also the drift a user sees between an old scan and a rescan,
	/// or between two machines sharing a database. Flat frames and linear gradients are
	/// excluded on purpose: their AC block is mathematically zero, so the hash is rounding
	/// noise in every implementation including the original, and no tolerance applies.
	/// If a change exceeds this tolerance for any meaningful number of inputs, bump
	/// <c>DatabaseUtils.DbVersion</c> at the same time.
	/// </summary>
	[Fact]
	public void ComputePHash_MatchesFullDctReference() {
		var rng = new Random(20260501);
		var corpus = new List<(string cls, byte[] gray)>();
		for (int i = 0; i < 16; i++) corpus.Add(("random-noise", RandomBytes(rng)));
		// Natural frames are dominated by low frequencies; those are the inputs whose AC
		// coefficients sit close to the median and flip under a different summation order.
		foreach (string cls in DriftProneClasses)
			for (int i = 0; i < 600; i++) corpus.Add((cls, Generate(cls, rng)));

		int differing = 0;
		foreach (var (cls, gray) in corpus) {
			ulong actual = PerceptualHash.ComputePHashFromGray32x32(gray);
			ulong expected = ReferenceFullDct(gray);
			int distance = BitOperations.PopCount(expected ^ actual);
			if (distance > 0) differing++;
			Assert.True(distance <= 2, $"{cls}: Hamming distance {distance} to the full-DCT reference exceeds the 2-bit tolerance");
		}
		// Well under 1% of inputs may drift; more than that means the accumulation changed
		// in a way cached hashes will notice.
		Assert.True(differing <= corpus.Count / 100, $"{differing} of {corpus.Count} hashes differ from the reference; expected a handful at most");
	}
}
