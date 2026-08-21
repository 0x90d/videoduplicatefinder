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

using System.Buffers;
using System.Numerics.Tensors;

namespace VDF.Core.pHash {
	internal static class PerceptualHash {

		const int N = 32;      // working size
		const int K = 8;       // low-frequency block size (1..8,1..8)

		// Cosine table flattened to 1D: Cos[k, i] is now Cos[k * N + i]. Faster than
		// float[,] (one bounds check instead of two) and lets dot-product loops index
		// linearly. Precomputed once at static init.
		static readonly float[] Cos = BuildCos();
		static readonly float Alpha = (float)Math.Sqrt(2.0 * (1.0 / N));

		public static ulong ComputePHashFromGray32x32(ReadOnlySpan<byte> gray) {
			if (gray.Length != N * N) throw new ArgumentException("expected 32x32=1024 bytes");

			// The original implementation computed a full N×N DCT — 1024 row outputs
			// followed by 1024 column outputs — and then read only the K×K=64 cells
			// dct[1..K, 1..K]. ~6.4× of the multiplications were thrown away.
			//
			// This version is SIMD-dependent and can give different results compared
			// to previous version(s). In testing, fewer than 0.01% of frames showed
			// differences, with a max hamming distance of 2.

			Span<float> temp = stackalloc float[K * N];
			Span<float> rowF = stackalloc float[N];

			for (int y = 0; y < N; y++) {
				TensorPrimitives.ConvertChecked(gray.Slice(y * N, N), rowF);
				for (int u = 0; u < K; u++) {
					var cos = Cos.AsSpan(u * N, N);
					temp[u * N + y] = Alpha * TensorPrimitives.Dot(rowF, cos);
				}
			}

			Span<float> ac = stackalloc float[K * K];
			int k = 0;
			for (int v = 0; v < K; v++) {
				var cos = Cos.AsSpan(v * N, N);
				for (int u = 0; u < K; u++)
					ac[k++] = Alpha * TensorPrimitives.Dot(temp.Slice(u * N, N), cos);
			}

			float median = Median64(ac);
			ulong hash = 0UL;
			for (int i = 0; i < ac.Length; i++)
				if (ac[i] > median) hash |= 1UL << i;
			return hash;
		}

		static float[] BuildCos() {
			var t = new float[K * N];
			for (int k = 0; k < K; k++)
				for (int i = 0; i < N; i++)
					t[k * N + i] = (float)Math.Cos(((2 * i + 1) * (k + 1) * Math.PI) / (2.0 * N));
			return t;
		}

		static float Median64(Span<float> values) {
			// Copy to the stack and sort; faster than fancy selection for 64 elems
			Span<float> buf = stackalloc float[K * K];
			values.CopyTo(buf);
			buf.Sort();
			return (buf[31] + buf[32]) * 0.5f; // even length = 64
		}
	}
}
