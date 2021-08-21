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

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace VDF.Core.Utils {
	static class GrayBytesUtils {

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool VerifyGrayScaleValues(byte[] data, double darkProcent = 80) {
			int darkPixels = 0;
			// ReSharper disable once LoopCanBeConvertedToQuery
			for (int i = 0; i < data.Length; i++) {
				if (data[i] <= 0x20)
					darkPixels++;
			}
			return 100d / data.Length * darkPixels < darkProcent;
		}

		public static unsafe byte[]? GetGrayScaleValues(Bitmap original, int width, double darkProcent = 80) {
			// Lock the bitmap's bits.  
			Rectangle rect = new Rectangle(0, 0, original.Width, original.Height);
			BitmapData bmpData = original.LockBits(rect, ImageLockMode.ReadOnly, original.PixelFormat);

			// Get the address of the first line.
			IntPtr ptr = bmpData.Scan0;

			// Declare an array to hold the bytes of the bitmap.
			int bytes = bmpData.Stride * original.Height;
			byte* rgbValues = stackalloc byte[bytes];
			byte[] buffer = new byte[width*width];

			// Copy the RGB values into the array.
			Unsafe.CopyBlock(rgbValues, (void*)ptr, (uint)bytes);
			original.UnlockBits(bmpData);

			int count = 0, all = bmpData.Width * bmpData.Height;
			int buffercounter = 0;
			for (int i = 0; i < bytes; i += 4) {
				byte r = rgbValues[i + 2], g = rgbValues[i + 1], b = rgbValues[i];
				buffer[buffercounter] = r;
				buffercounter++;
				var brightness = (byte)Math.Round(0.299 * r + 0.5876 * g + 0.114 * b);
				if (brightness <= 0x20)
					count++;
			}
			return 100d / all * count >= darkProcent ? null : buffer;

		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe float PercentageDifference(byte[] img1, byte[] img2) {
			Debug.Assert(img1.Length == img2.Length, "Images must be of the same size");

			if (Avx2.IsSupported && img1.Length % 32 == 0)
				return PercentageDifferenceAvx2(img1, img2);
			else if (Sse2.IsSupported && img1.Length % 16 == 0)
				return PercentageDifferenceSse2(img1, img2);
			else
				return PercentageDifferenceLoop(img1, img2);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte[] FlipGrayScale(byte[] img, int img_width)
		{
			Debug.Assert((img.Length % img_width) == 0, "Invalid img.Len or img_width");

			if (Avx2.IsSupported && img_width == 16)
				return FlipGrayScaleAvx2(img);
			else if (Sse2.IsSupported && img_width == 16)
				return FlipGrayScaleSse2(img);
			else
				return FlipGrayScaleReverse(img, img_width);
		}

	//-------------------------------------------------------------------------

	// Different PercentageDifference versions:
	// (Return value: [0..1])

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe float PercentageDifferenceLoop(byte[] img1, byte[] img2) {
			long diff = 0;
			for (int i = 0; i < img1.Length; i++)
					diff += Math.Abs(img1[i] - img2[i]);

			return (float)diff /  img1.Length / 256;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe float PercentageDifferenceAvx2(byte[] img1, byte[] img2) {
			long diff = 0;
			Vector256<ushort> vec = Vector256<ushort>.Zero;
			Span<Vector256<byte>> vImg1 = MemoryMarshal.Cast<byte, Vector256<byte>>(img1);
			Span<Vector256<byte>> vImg2 = MemoryMarshal.Cast<byte, Vector256<byte>>(img2);

			for (int i = 0; i < vImg1.Length; i++)
				vec = Avx2.Add(vec, Avx2.SumAbsoluteDifferences(vImg2[i], vImg1[i]));

			for (int i = 0; i < Vector256<ushort>.Count; i++)
				diff += Math.Abs(vec.GetElement(i));

			return (float)diff /  img1.Length / 256;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static unsafe float PercentageDifferenceSse2(byte[] img1, byte[] img2) {
			long diff = 0;
			Vector128<ushort> vec = Vector128<ushort>.Zero;
			Span<Vector128<byte>> vImg1 = MemoryMarshal.Cast<byte, Vector128<byte>>(img1);
			Span<Vector128<byte>> vImg2 = MemoryMarshal.Cast<byte, Vector128<byte>>(img2);

			for (int i = 0; i < vImg1.Length; i++)
				vec = Sse2.Add(vec, Sse2.SumAbsoluteDifferences(vImg2[i], vImg1[i]));

			for (int i = 0; i < Vector128<ushort>.Count; i++)
				diff += Math.Abs(vec.GetElement(i));

			return (float)diff /  img1.Length / 256;
		}


	// Different FlipGrayScale versions:

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static byte[] FlipGrayScaleReverse(byte[] img, int img_width)
		{
			int rows = img.Length / img_width;
			byte[] flip_img = (byte[])img.Clone();
			for (int i = 0; i < rows; i++)
				Array.Reverse(flip_img, i*img_width, img_width);
			return flip_img;
		}

		private static byte[] flipp_shuf256 = {
				15,14,13,12,11,10, 9, 8,   7, 6, 5, 4, 3, 2, 1, 0, 
				31,30,29,28,27,26,25,24,  23,22,21,20,19,18,17,16 
		};
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static byte[] FlipGrayScaleAvx2(byte[] img)
		{
			byte[] flip_img = new byte[img.Length];
			Span<Vector256<byte>> vImg = MemoryMarshal.Cast<byte, Vector256<byte>>(img);
			Span<Vector256<byte>> vImg_flipped = MemoryMarshal.Cast<byte, Vector256<byte>>(flip_img);
			Span<Vector256<byte>> vFlipp_shuf = MemoryMarshal.Cast<byte, Vector256<byte>>(flipp_shuf256);

			for (int i = 0; i < vImg.Length; i++)
				vImg_flipped[i] = Avx2.Shuffle(vImg[i], vFlipp_shuf[0]);

			return flip_img;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static byte[] FlipGrayScaleSse2(byte[] img)
		{
			byte[] flip_img = new byte[img.Length];
			Span<Vector128<byte>> vImg = MemoryMarshal.Cast<byte, Vector128<byte>>(img);
			Span<Vector128<byte>> vImg_flipped = MemoryMarshal.Cast<byte, Vector128<byte>>(flip_img);
			Span<Vector128<byte>> vFlipp_shuf = MemoryMarshal.Cast<byte, Vector128<byte>>(flipp_shuf256);

			for (int i = 0; i < vImg.Length; i++)
				vImg_flipped[i] = Avx2.Shuffle(vImg[i], vFlipp_shuf[0]);

			return flip_img;
		}
	}
}
