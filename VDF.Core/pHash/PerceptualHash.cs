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

		// Precompute cosine matrix and alpha coefficients once
		private static readonly float[,] Cos = BuildCos(N);
		private static readonly float[] Alpha = BuildAlpha(N);

		// Reusable buffers via ArrayPool to reduce GC pressure
		private static readonly ArrayPool<float> Pool = ArrayPool<float>.Shared;

		public static ulong ComputePHashFromGray32x32(ReadOnlySpan<byte> gray) {
			if (gray.Length != N * N) throw new ArgumentException("expected 32x32=1024 bytes");
			int len = N * N;
			var pool = ArrayPool<float>.Shared;
			float[] input = pool.Rent(len);
			float[] temp = pool.Rent(len);
			float[] dct = pool.Rent(len);
			try {
				// copy bytes -> float
				for (int i = 0; i < len; i++) input[i] = gray[i];

				// DCT rows
				for (int y = 0; y < N; y++) {
					int yBase = y * N;
					for (int u = 0; u < N; u++) {
						float sum = 0f;
						for (int x = 0; x < N; x++) sum += input[yBase + x] * Cos[u, x];
						temp[yBase + u] = Alpha[u] * sum;
					}
				}
				// DCT cols
				for (int u = 0; u < N; u++) {
					for (int v = 0; v < N; v++) {
						float sum = 0f;
						for (int y = 0; y < N; y++) sum += temp[y * N + u] * Cos[v, y];
						dct[v * N + u] = Alpha[v] * sum;
					}
				}
				// 8x8 AC -> Median -> Bits
				Span<float> ac = stackalloc float[K * K];
				int k = 0;
				for (int v = 1; v <= K; v++) {
					int vBase = v * N;
					for (int u = 1; u <= K; u++) ac[k++] = dct[vBase + u];
				}
				float median = Median64(ac);
				ulong hash = 0UL;
				for (int i = 0; i < ac.Length; i++)
					if (ac[i] > median) hash |= 1UL << i;
				return hash;
			}
			finally { pool.Return(input); pool.Return(temp); pool.Return(dct); }
		}


		private static float[,] BuildCos(int n) {
			var t = new float[n, n];
			for (int k = 0; k < n; k++)
				for (int i = 0; i < n; i++)
					t[k, i] = (float)Math.Cos(((2 * i + 1) * k * Math.PI) / (2.0 * n));
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
