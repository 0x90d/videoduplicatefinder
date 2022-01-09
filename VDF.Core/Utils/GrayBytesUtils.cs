// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SixLabors.ImageSharp.PixelFormats;

namespace VDF.Core.Utils {
	static class GrayBytesUtils {
		const int GrayByteValueLength = 256;
		const byte BlackPixelLimit = 0x20;
		const byte WhitePixelLimit = 0xF0;

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

		public static unsafe byte[]? GetGrayScaleValues(Image original, double darkProcent = 80) {
			// Lock the bitmap's bits.  
			using var tempImage = original.CloneAs<Bgra32>();
			tempImage.TryGetSinglePixelSpan(out var pixelSpan);
			Span<byte> span = MemoryMarshal.AsBytes(pixelSpan);

			byte[] buffer = new byte[GrayByteValueLength];
			int stride = tempImage.GetPixelRowSpan(0).Length;
			int bytes = stride * original.Height;

			int count = 0, all = original.Width * original.Height;
			int buffercounter = 0;
			for (int i = 0; i < span.Length; i += 4) {
				byte r = span[i + 2], g = span[i + 1], b = span[i];
				byte brightness = (byte)Math.Round(0.299 * r + 0.5876 * g + 0.114 * b);
				buffer[buffercounter++] = brightness;
				if (brightness <= BlackPixelLimit)
					count++;
			}
			return 100d / all * count >= darkProcent ? null : buffer;

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
			return (float)diff / counter / 256;
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
			return (float)diff / img1.Length / 256;
		}

		readonly static byte[] flipp_shuf256 = {
				15,14,13,12,11,10, 9, 8,   7, 6, 5, 4, 3, 2, 1, 0,
				31,30,29,28,27,26,25,24,  23,22,21,20,19,18,17,16
		};

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte[] FlipGrayScale(byte[] img) {
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
			else if (Sse2.IsSupported) {
				flip_img = new byte[img.Length];
				Span<Vector128<byte>> vImg = MemoryMarshal.Cast<byte, Vector128<byte>>(img);
				Span<Vector128<byte>> vImg_flipped = MemoryMarshal.Cast<byte, Vector128<byte>>(flip_img);
				Span<Vector128<byte>> vFlipp_shuf = MemoryMarshal.Cast<byte, Vector128<byte>>(flipp_shuf256);

				for (int i = 0; i < vImg.Length; i++)
					vImg_flipped[i] = Avx2.Shuffle(vImg[i], vFlipp_shuf[0]);
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
