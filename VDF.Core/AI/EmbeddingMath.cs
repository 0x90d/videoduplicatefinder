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
using System.Runtime.InteropServices;

namespace VDF.Core.AI {
	/// <summary>
	/// Math over quantized embedding vectors. Embeddings are L2-normalized float vectors
	/// quantized to int8 (value = round(x·127), stored in byte[] so the sidecar caches
	/// can carry them without a custom formatter), so cosine similarity reduces to an
	/// integer dot product divided by 127². Quantization error on the similarity is
	/// &lt;1% — far below the matching thresholds in use.
	/// </summary>
	static class EmbeddingMath {
		/// <summary>DINOv2-small embedding width.</summary>
		public const int Dimensions = 384;
		const float QuantScale = 127f;

		internal static byte[] QuantizeUnitVector(ReadOnlySpan<float> vector) {
			var q = new byte[vector.Length];
			for (int i = 0; i < vector.Length; i++) {
				int rounded = (int)MathF.Round(vector[i] * QuantScale);
				q[i] = unchecked((byte)(sbyte)Math.Clamp(rounded, -127, 127));
			}
			return q;
		}

		/// <summary>Cosine similarity (≈ −1..1) of two int8-quantized unit vectors.</summary>
		internal static float CosineSimilarity(byte[] a, byte[] b) {
			ReadOnlySpan<sbyte> sa = MemoryMarshal.Cast<byte, sbyte>(a);
			ReadOnlySpan<sbyte> sb = MemoryMarshal.Cast<byte, sbyte>(b);
			int len = Math.Min(sa.Length, sb.Length);
			int dot = 0;
			int i = 0;
			if (Vector.IsHardwareAccelerated && len >= Vector<sbyte>.Count) {
				Vector<int> acc = Vector<int>.Zero;
				int width = Vector<sbyte>.Count;
				for (; i <= len - width; i += width) {
					Vector.Widen(new Vector<sbyte>(sa.Slice(i)), out Vector<short> a1, out Vector<short> a2);
					Vector.Widen(new Vector<sbyte>(sb.Slice(i)), out Vector<short> b1, out Vector<short> b2);
					// Element products fit in short (max |−127·127| = 16129); sums do not,
					// so widen each product vector to int lanes before accumulating.
					Vector.Widen(a1 * b1, out Vector<int> p1, out Vector<int> p2);
					Vector.Widen(a2 * b2, out Vector<int> p3, out Vector<int> p4);
					acc += p1 + p2 + p3 + p4;
				}
				dot = Vector.Sum(acc);
			}
			for (; i < len; i++)
				dot += sa[i] * sb[i];
			return dot / (QuantScale * QuantScale);
		}

		/// <summary>Scalar reference implementation; the SIMD path is verified against it in tests.</summary>
		internal static float CosineSimilarityScalar(byte[] a, byte[] b) {
			ReadOnlySpan<sbyte> sa = MemoryMarshal.Cast<byte, sbyte>(a);
			ReadOnlySpan<sbyte> sb = MemoryMarshal.Cast<byte, sbyte>(b);
			int len = Math.Min(sa.Length, sb.Length);
			int dot = 0;
			for (int i = 0; i < len; i++)
				dot += sa[i] * sb[i];
			return dot / (QuantScale * QuantScale);
		}
	}
}
