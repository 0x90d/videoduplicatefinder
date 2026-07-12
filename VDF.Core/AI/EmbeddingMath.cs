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
			// Clamp: rounding pushes the quantized norm above 127 for many unit vectors
			// (self-dot > 127², systematically for near-identical pairs), so the raw
			// quotient exceeds 1. Unclamped this made `1 - similarity` negative — the
			// GUI showed >100% and the default 0–100 similarity filter hid the pair.
			return Math.Clamp(dot / (QuantScale * QuantScale), -1f, 1f);
		}

		/// <summary>Scalar reference implementation; the SIMD path is verified against it in tests.</summary>
		internal static float CosineSimilarityScalar(byte[] a, byte[] b) {
			ReadOnlySpan<sbyte> sa = MemoryMarshal.Cast<byte, sbyte>(a);
			ReadOnlySpan<sbyte> sb = MemoryMarshal.Cast<byte, sbyte>(b);
			int len = Math.Min(sa.Length, sb.Length);
			int dot = 0;
			for (int i = 0; i < len; i++)
				dot += sa[i] * sb[i];
			return Math.Clamp(dot / (QuantScale * QuantScale), -1f, 1f);
		}

		internal const int SignatureWords = Dimensions / 64;

		/// <summary>Sign bitmask of a quantized vector — bit i set when component i is negative.</summary>
		internal static ulong[] SignSignature(byte[] quantized) {
			var signature = new ulong[SignatureWords];
			ReadOnlySpan<sbyte> s = MemoryMarshal.Cast<byte, sbyte>(quantized);
			int len = Math.Min(s.Length, Dimensions);
			for (int i = 0; i < len; i++)
				if (s[i] < 0)
					signature[i >> 6] |= 1UL << (i & 63);
			return signature;
		}

		internal static int HammingDistance(ulong[] a, ulong[] b) {
			int distance = 0;
			for (int i = 0; i < a.Length; i++)
				distance += System.Numerics.BitOperations.PopCount(a[i] ^ b[i]);
			return distance;
		}

		/// <summary>
		/// Sign-LSH prefilter bound for <see cref="HammingDistance"/> over
		/// <see cref="SignSignature"/>s: two unit vectors at angle θ differ in sign on a
		/// dimension with probability θ/π, so vectors with cosine ≥ threshold have
		/// expected sign-hamming E = D·acos(threshold)/π with σ² = D·p(1−p). Above
		/// E + 4.6σ a pair can reach the cosine threshold only with negligible
		/// probability (≈2·10⁻⁶ per frame pair — far below the matching pass's evidence
		/// quorum), so the expensive exact dot product can be skipped.
		/// </summary>
		internal static int SignatureHammingBound(float cosineThreshold) {
			double p = Math.Acos(Math.Clamp(cosineThreshold, -1f, 1f)) / Math.PI;
			double expected = Dimensions * p;
			double sigma = Math.Sqrt(Dimensions * p * (1 - p));
			return (int)Math.Ceiling(expected + 4.6 * sigma);
		}
	}
}
