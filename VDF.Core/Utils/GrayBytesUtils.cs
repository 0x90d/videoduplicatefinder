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

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VDF.Core.Utils {
	static class GrayBytesUtils {
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

		public static unsafe byte[]? GetGrayScaleValues(Image original, double darkPercent = 80) {

			using var img = original.CloneAs<L8>();
			img.Mutate(ctx => ctx.Resize(Side, Side).Grayscale());

			byte[] buffer = new byte[GrayByteValueLength];

			int dark = 0;
			img.ProcessPixelRows(accessor =>
			{
				for (int y = 0; y < Side; y++) {
					Span<L8> row = accessor.GetRowSpan(y);
					int baseIdx = y * Side;
					for (int x = 0; x < Side; x++) {
						byte lum = row[x].PackedValue;
						buffer[baseIdx + x] = lum;
						if (lum <= BlackPixelLimit) dark++;
					}
				}
			});

			double darkP = 100d / GrayByteValueLength * dark;
			return darkP >= darkPercent ? null : buffer;

		}
		public static unsafe byte[]? GetGrayScaleValues16x16(Image original, double darkPercent = 80) {
			const int graybyteLength = 256;
			using var img = original.CloneAs<L8>();
			img.Mutate(ctx => ctx.Resize(Side, Side).Grayscale());

			byte[] buffer = new byte[graybyteLength];

			int dark = 0;
			img.ProcessPixelRows(accessor => {
				for (int y = 0; y < Side; y++) {
					Span<L8> row = accessor.GetRowSpan(y);
					int baseIdx = y * Side;
					for (int x = 0; x < Side; x++) {
						byte lum = row[x].PackedValue;
						buffer[baseIdx + x] = lum;
						if (lum <= BlackPixelLimit) dark++;
					}
				}
			});

			double darkP = 100d / graybyteLength * dark;
			return darkP >= darkPercent ? null : buffer;

		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float PercentageDifferenceWithoutSpecificPixels(byte[] img1, byte[] img2, bool ignoreBlackPixels, bool ignoreWhitePixels) {
			Debug.Assert(img1.Length == img2.Length, "Images must be of the same size");
			long diff = 0;
			int counter = 0;
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
			return (float)diff / counter / brightnessScalePerPixel;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe float PercentageDifference(byte[] img1, byte[] img2) {
			Debug.Assert(img1.Length == img2.Length, "Images must be of the same size");
			long diff = 0;
			if (Avx2.IsSupported) {
				Vector256<ushort> vec = Vector256<ushort>.Zero;
				Span<Vector256<byte>> vImg1 = MemoryMarshal.Cast<byte, Vector256<byte>>(img1);
				Span<Vector256<byte>> vImg2 = MemoryMarshal.Cast<byte, Vector256<byte>>(img2);

				for (int i = 0; i < vImg1.Length; i++)
					vec = Avx2.Add(vec, Avx2.SumAbsoluteDifferences(vImg2[i], vImg1[i]));

				for (int i = 0; i < Vector256<ushort>.Count; i++)
					diff += Math.Abs(vec.GetElement(i));
			}
			else if (Sse2.IsSupported) {
				Vector128<ushort> vec = Vector128<ushort>.Zero;
				Span<Vector128<byte>> vImg1 = MemoryMarshal.Cast<byte, Vector128<byte>>(img1);
				Span<Vector128<byte>> vImg2 = MemoryMarshal.Cast<byte, Vector128<byte>>(img2);

				for (int i = 0; i < vImg1.Length; i++)
					vec = Sse2.Add(vec, Sse2.SumAbsoluteDifferences(vImg2[i], vImg1[i]));

				for (int i = 0; i < Vector128<ushort>.Count; i++)
					diff += Math.Abs(vec.GetElement(i));
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
				var shuf = MemoryMarshal.Cast<byte, Vector128<byte>>(shuffle16)[0];

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



		readonly static byte[] flipp_shuf256 = {
				15,14,13,12,11,10, 9, 8,   7, 6, 5, 4, 3, 2, 1, 0,
				31,30,29,28,27,26,25,24,  23,22,21,20,19,18,17,16
		};

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte[] FlipGrayScale16x16(byte[] img) {
			Debug.Assert((img.Length % 16) == 0, "Invalid img.Length");
			byte[] flip_img;
			if (Avx2.IsSupported) {
				flip_img = new byte[img.Length];
				Span<Vector256<byte>> vImg = MemoryMarshal.Cast<byte, Vector256<byte>>(img);
				Span<Vector256<byte>> vImg_flipped = MemoryMarshal.Cast<byte, Vector256<byte>>(flip_img);
				Span<Vector256<byte>> vFlipp_shuf = MemoryMarshal.Cast<byte, Vector256<byte>>(flipp_shuf256);

				for (int i = 0; i < vImg.Length; i++)
					vImg_flipped[i] = Avx2.Shuffle(vImg[i], vFlipp_shuf[0]);
			}
			else if (Sse3.IsSupported) {
				flip_img = new byte[img.Length];
				Span<Vector128<byte>> vImg = MemoryMarshal.Cast<byte, Vector128<byte>>(img);
				Span<Vector128<byte>> vImg_flipped = MemoryMarshal.Cast<byte, Vector128<byte>>(flip_img);
				Span<Vector128<byte>> vFlipp_shuf = MemoryMarshal.Cast<byte, Vector128<byte>>(flipp_shuf256);

				for (int i = 0; i < vImg.Length; i++)
					vImg_flipped[i] = Ssse3.Shuffle(vImg[i], vFlipp_shuf[0]);
			}
			else {
				flip_img = (byte[])img.Clone();
				for (int i = 0; i < 16; i++)
					Array.Reverse(flip_img, i * 16, 16);
			}
			return flip_img;
		}
	}
}
