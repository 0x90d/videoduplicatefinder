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

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VDF.Core.Utils {
	static class GrayBytesUtils {
		internal const int OldSide = 16;
		internal const int Side = 32;
		const int GrayByteValueLength = Side * Side; //1024
		const byte BlackPixelLimit = 0x20;
		const byte WhitePixelLimit = 0xF0;
		const int brightnessScalePerPixel = 256; // 0-255

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool VerifyGrayScaleValues(byte[] data, double darkProcent = 80) {
			int darkPixels = 0;
			// ReSharper disable once LoopCanBeConvertedToQuery
			for (int i = 0; i < data.Length; i++) {
				if (data[i] <= BlackPixelLimit)
					darkPixels++;
			}
			return 100d / data.Length * darkPixels < darkProcent;
		}

		/// <summary>
		/// Dark-frame check for RGB24 embedding inputs — same convention as
		/// <see cref="VerifyGrayScaleValues"/> (a pixel is dark when every channel is at
		/// or below the black-pixel limit; the frame fails when at least
		/// <paramref name="darkProcent"/> of pixels are dark). Dark frames embed
		/// near-identically regardless of content, so they must never feed AI matching.
		/// </summary>
		public static bool VerifyRgbFrameValues(byte[] rgb, double darkProcent = 80) {
			int pixels = rgb.Length / 3;
			if (pixels == 0)
				return false;
			int darkPixels = 0;
			for (int i = 0; i + 2 < rgb.Length; i += 3) {
				if (rgb[i] <= BlackPixelLimit && rgb[i + 1] <= BlackPixelLimit && rgb[i + 2] <= BlackPixelLimit)
					darkPixels++;
			}
			return 100d / pixels * darkPixels < darkProcent;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe float PercentageDifferenceWithoutSpecificPixels(byte[] img1, byte[] img2, bool ignoreBlackPixels, bool ignoreWhitePixels) {
			Debug.Assert(img1.Length == img2.Length, "Images must be of the same size");

			// When neither filter is enabled this degenerates to PercentageDifference,
			// which is already AVX2-vectorized — go straight there.
			if (!ignoreBlackPixels && !ignoreWhitePixels)
				return PercentageDifference(img1, img2);

			long diff = 0;
			long counter = 0;

			if (Avx2.IsSupported && img1.Length >= Vector256<byte>.Count && img1.Length % Vector256<byte>.Count == 0) {
				// SIMD path: ~20× faster than scalar at 1024 bytes.
				//
				// Approach per 32-byte block:
				//   1. Build a per-byte "valid" mask (0xFF where the pixel passes the filter, 0x00 where excluded).
				//      Unsigned > / < comparisons are encoded via SubtractSaturate then CompareEqual-with-zero,
				//      since AVX2 native byte compares are signed-only.
				//   2. AND both inputs with the mask (excluded lanes become 0 in both → contribute 0 to SAD).
				//   3. Sum-absolute-differences for the diff total; SAD(mask, 0) for the count
				//      (each surviving 0xFF byte contributes 255, divide at the end).
				var blackThr = Vector256.Create(BlackPixelLimit);
				var whiteMinus1 = Vector256.Create((byte)(WhitePixelLimit - 1));
				var zero = Vector256<byte>.Zero;

				// Int64-lane accumulators (see PercentageDifference) so neither the difference nor
				// the surviving-pixel count can overflow the way a 16-bit accumulator would.
				Vector256<long> diffVec = Vector256<long>.Zero;
				Vector256<long> countVec = Vector256<long>.Zero;

				Span<Vector256<byte>> vImg1 = MemoryMarshal.Cast<byte, Vector256<byte>>(img1.AsSpan());
				Span<Vector256<byte>> vImg2 = MemoryMarshal.Cast<byte, Vector256<byte>>(img2.AsSpan());

				for (int i = 0; i < vImg1.Length; i++) {
					var v1 = vImg1[i];
					var v2 = vImg2[i];

					Vector256<byte> validMask = Vector256<byte>.AllBitsSet;

					if (ignoreBlackPixels) {
						// "Black" ⇔ v ≤ BlackPixelLimit ⇔ SubtractSaturate(v, blackThr) == 0.
						var v1IsBlack = Avx2.CompareEqual(Avx2.SubtractSaturate(v1, blackThr), zero);
						var v2IsBlack = Avx2.CompareEqual(Avx2.SubtractSaturate(v2, blackThr), zero);
						var anyBlack = Avx2.Or(v1IsBlack, v2IsBlack);
						validMask = Avx2.AndNot(anyBlack, validMask);
					}
					if (ignoreWhitePixels) {
						// "Not white" ⇔ v < WhitePixelLimit ⇔ v ≤ WhitePixelLimit-1
						// ⇔ SubtractSaturate(v, whiteMinus1) == 0.
						var v1NotWhite = Avx2.CompareEqual(Avx2.SubtractSaturate(v1, whiteMinus1), zero);
						var v2NotWhite = Avx2.CompareEqual(Avx2.SubtractSaturate(v2, whiteMinus1), zero);
						var bothNotWhite = Avx2.And(v1NotWhite, v2NotWhite);
						validMask = Avx2.And(validMask, bothNotWhite);
					}

					var v1Masked = Avx2.And(v1, validMask);
					var v2Masked = Avx2.And(v2, validMask);
					diffVec = Avx2.Add(diffVec, Avx2.SumAbsoluteDifferences(v1Masked, v2Masked).AsInt64());
					countVec = Avx2.Add(countVec, Avx2.SumAbsoluteDifferences(validMask, zero).AsInt64());
				}

				for (int i = 0; i < Vector256<long>.Count; i++) {
					diff += diffVec.GetElement(i);
					counter += countVec.GetElement(i);
				}
				counter /= 255; // PSADBW(mask, 0) accumulated 255 per surviving byte.
			}
			else {
				for (int i = 0; i < img1.Length; i++) {
					bool isValid = true;
					if (ignoreBlackPixels)
						isValid = img1[i] > BlackPixelLimit && img2[i] > BlackPixelLimit;
					if (!isValid) continue;
					if (ignoreWhitePixels)
						isValid = img1[i] < WhitePixelLimit && img2[i] < WhitePixelLimit;
					if (!isValid) continue;
					diff += Math.Abs(img1[i] - img2[i]);
					counter++;
				}
			}

			return (float)diff / counter / brightnessScalePerPixel;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe float PercentageDifference(byte[] img1, byte[] img2) {
			Debug.Assert(img1.Length == img2.Length, "Images must be of the same size");
			long diff = 0;
			if (Avx2.IsSupported) {
				// PSADBW emits each 8-byte SAD (≤ 8*255 = 2040) into its own 64-bit lane. Accumulate
				// those into a Vector256<long> so the running total can never overflow, whatever the
				// buffer size. A 16-bit accumulator wrapped once the per-lane sum passed 65535 — at
				// 32x32 = 1024 bytes that overflowed on the SSE2 path and reported wildly different
				// images as near-identical (#810); Int64 lanes remove the failure mode entirely.
				Vector256<long> acc = Vector256<long>.Zero;
				Span<Vector256<byte>> vImg1 = MemoryMarshal.Cast<byte, Vector256<byte>>(img1.AsSpan());
				Span<Vector256<byte>> vImg2 = MemoryMarshal.Cast<byte, Vector256<byte>>(img2.AsSpan());

				for (int i = 0; i < vImg1.Length; i++)
					acc = Avx2.Add(acc, Avx2.SumAbsoluteDifferences(vImg2[i], vImg1[i]).AsInt64());

				diff = acc.GetElement(0) + acc.GetElement(1) + acc.GetElement(2) + acc.GetElement(3);
			}
			else if (Sse2.IsSupported) {
				// Same overflow-proof Int64-lane accumulation as the AVX2 path (PSADBW puts two SAD
				// results in 64-bit lanes 0 and 1 here). The previous Vector128<ushort> accumulator
				// overflowed for dissimilar 1024-byte pairs and was the actual #810 bug.
				Vector128<long> acc = Vector128<long>.Zero;
				Span<Vector128<byte>> vImg1 = MemoryMarshal.Cast<byte, Vector128<byte>>(img1.AsSpan());
				Span<Vector128<byte>> vImg2 = MemoryMarshal.Cast<byte, Vector128<byte>>(img2.AsSpan());

				for (int i = 0; i < vImg1.Length; i++)
					acc = Sse2.Add(acc, Sse2.SumAbsoluteDifferences(vImg2[i], vImg1[i]).AsInt64());

				diff = acc.GetElement(0) + acc.GetElement(1);
			}
			else {
				for (int i = 0; i < img1.Length; i++)
					diff += Math.Abs(img1[i] - img2[i]);
			}
			return (float)diff / img1.Length / brightnessScalePerPixel;
		}

		static readonly byte[] shuffle16 =
		{
			15,14,13,12,11,10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0
		};

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte[] FlipGrayScale(byte[] img) {
			Debug.Assert((int)Math.Sqrt(img.Length) * (int)Math.Sqrt(img.Length) == img.Length, "Invalid img.Length");

			int side = (int)Math.Sqrt(img.Length);
			byte[] dst = new byte[img.Length];

			if (Avx2.IsSupported && (side % 32) == 0) {
				// line by line: two 16-byte shuffles per 32-byte block + swap of halves
				var shuf = MemoryMarshal.Cast<byte, Vector128<byte>>(shuffle16.AsSpan())[0];

				for (int y = 0; y < side; y++) {
					int rowBase = y * side;

					for (int x = 0; x < side; x += 32) {
						int left = rowBase + x;
						int right = rowBase + (side - 32 - x);

						// read left[0..31] and right[0..31]
						var L_lo = Unsafe.ReadUnaligned<Vector128<byte>>(ref img[left + 0]);
						var L_hi = Unsafe.ReadUnaligned<Vector128<byte>>(ref img[left + 16]);
						var R_lo = Unsafe.ReadUnaligned<Vector128<byte>>(ref img[right + 0]);
						var R_hi = Unsafe.ReadUnaligned<Vector128<byte>>(ref img[right + 16]);

						// reverse 16B chunks
						var L_lo_rev = Ssse3.IsSupported ? Ssse3.Shuffle(L_lo, shuf) : SoftwareReverse16(L_lo);
						var L_hi_rev = Ssse3.IsSupported ? Ssse3.Shuffle(L_hi, shuf) : SoftwareReverse16(L_hi);
						var R_lo_rev = Ssse3.IsSupported ? Ssse3.Shuffle(R_lo, shuf) : SoftwareReverse16(R_lo);
						var R_hi_rev = Ssse3.IsSupported ? Ssse3.Shuffle(R_hi, shuf) : SoftwareReverse16(R_hi);

						// swap positions (mirror)
						Unsafe.WriteUnaligned(ref dst[right + 16], L_lo_rev);
						Unsafe.WriteUnaligned(ref dst[right + 0], L_hi_rev);
						Unsafe.WriteUnaligned(ref dst[left + 16], R_lo_rev);
						Unsafe.WriteUnaligned(ref dst[left + 0], R_hi_rev);
					}
				}
				return dst;
			}

			// fallback
			for (int y = 0; y < side; y++) {
				int baseIdx = y * side;
				Array.Copy(img, baseIdx, dst, baseIdx, side);
				Array.Reverse(dst, baseIdx, side);
			}
			return dst;

			// --- local helper for non-SSSE3 reverse16 ---
			static Vector128<byte> SoftwareReverse16(Vector128<byte> v) {
				Span<byte> t = stackalloc byte[16];
				Unsafe.WriteUnaligned(ref t[0], v);
				t.Reverse();
				return Unsafe.ReadUnaligned<Vector128<byte>>(ref t[0]);
			}
		}
	}
}
