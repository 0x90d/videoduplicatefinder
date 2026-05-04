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

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VDF.Core.pHash {
	internal static class PerceptualHash {

		private const int N = 32;      // working size
		private const int K = 8;       // low-frequency block size (1..8,1..8)

		// Cosine table flattened to 1D: Cos[k, i] is now Cos[k * N + i]. Faster than
		// float[,] (one bounds check instead of two) and lets dot-product loops index
		// linearly. Precomputed once at static init.
		private static readonly float[] Cos = BuildCos(N);
		private static readonly float[] Alpha = BuildAlpha(N);

		public static ulong ComputePHashFromGray32x32(ReadOnlySpan<byte> gray) {
			if (gray.Length != N * N) throw new ArgumentException("expected 32x32=1024 bytes");
			int len = N * N;

			// The original implementation computed a full N×N DCT — 1024 row outputs
			// followed by 1024 column outputs — and then read only the K×K=64 cells
			// dct[1..K, 1..K]. ~6.4× of the multiplications were thrown away.
			//
			// This version computes only the cells that are read, in the same scalar
			// accumulation order as before. Floats are bit-identical to the previous
			// implementation so cached PHashes from older scans remain valid.
			var pool = ArrayPool<float>.Shared;
			float[] input = pool.Rent(len);
			try {
				for (int i = 0; i < len; i++) input[i] = gray[i];

				// Row DCT: for each row y, produce K outputs (u in 1..K). Compact layout
				// temp[y * K + (u - 1)] avoids carrying the unused 24/32 columns.
				Span<float> temp = stackalloc float[N * K];
				for (int y = 0; y < N; y++) {
					int yBase = y * N;
					int tBase = y * K;
					for (int u = 1; u <= K; u++) {
						int cosBase = u * N;
						float sum = 0f;
						for (int x = 0; x < N; x++)
							sum += input[yBase + x] * Cos[cosBase + x];
						temp[tBase + (u - 1)] = Alpha[u] * sum;
					}
				}

				// Column DCT: K×K outputs, written directly into the AC buffer.
				// Sweep order matches the original (v outer, u inner) so the resulting
				// bit positions in `hash` are unchanged.
				Span<float> ac = stackalloc float[K * K];
				int k = 0;
				for (int v = 1; v <= K; v++) {
					int cosBase = v * N;
					float alphaV = Alpha[v];
					for (int u = 1; u <= K; u++) {
						int tu = u - 1;
						float sum = 0f;
						for (int y = 0; y < N; y++)
							sum += temp[y * K + tu] * Cos[cosBase + y];
						ac[k++] = alphaV * sum;
					}
				}

				float median = Median64(ac);
				ulong hash = 0UL;
				for (int i = 0; i < ac.Length; i++)
					if (ac[i] > median) hash |= 1UL << i;
				return hash;
			}
			finally { pool.Return(input); }
		}


		private static float[] BuildCos(int n) {
			var t = new float[n * n];
			for (int k = 0; k < n; k++)
				for (int i = 0; i < n; i++)
					t[k * n + i] = (float)Math.Cos(((2 * i + 1) * k * Math.PI) / (2.0 * n));
			return t;
		}

		private static float[] BuildAlpha(int n) {
			var a = new float[n];
			double invN = 1.0 / n;
			a[0] = (float)Math.Sqrt(invN);
			for (int k = 1; k < n; k++) a[k] = (float)Math.Sqrt(2.0 * invN);
			return a;
		}

		private static float Median64(Span<float> values) {
			// Copy to array and sort; faster than fancy selection for 64 elems
			float[] buf = new float[values.Length];
			values.CopyTo(buf);
			Array.Sort(buf);
			return (buf[31] + buf[32]) * 0.5f; // even length = 64
		}
	}
}
