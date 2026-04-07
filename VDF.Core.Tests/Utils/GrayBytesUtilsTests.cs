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

using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class GrayBytesUtilsTests {
	[Fact]
	public void VerifyGrayScaleValues_AllBlack_ReturnsFalse() {
		// All pixels <= 0x20 (BlackPixelLimit) means 100% dark > 80% threshold
		byte[] data = new byte[1024]; // all zeros
		Assert.False(GrayBytesUtils.VerifyGrayScaleValues(data));
	}

	[Fact]
	public void VerifyGrayScaleValues_AllWhite_ReturnsTrue() {
		byte[] data = new byte[1024];
		Array.Fill(data, (byte)0xFF);
		Assert.True(GrayBytesUtils.VerifyGrayScaleValues(data));
	}

	[Fact]
	public void VerifyGrayScaleValues_JustBelowThreshold_ReturnsTrue() {
		// 79% dark pixels should pass (< 80% threshold)
		byte[] data = new byte[100];
		// 79 dark pixels (value 0x00 <= 0x20), 21 bright pixels
		for (int i = 0; i < 79; i++) data[i] = 0x00;
		for (int i = 79; i < 100; i++) data[i] = 0x80;
		Assert.True(GrayBytesUtils.VerifyGrayScaleValues(data));
	}

	[Fact]
	public void VerifyGrayScaleValues_AtThreshold_ReturnsFalse() {
		// 80% dark pixels should fail (>= 80% threshold)
		byte[] data = new byte[100];
		for (int i = 0; i < 80; i++) data[i] = 0x00;
		for (int i = 80; i < 100; i++) data[i] = 0x80;
		Assert.False(GrayBytesUtils.VerifyGrayScaleValues(data));
	}

	[Fact]
	public void VerifyGrayScaleValues_CustomThreshold() {
		byte[] data = new byte[100];
		for (int i = 0; i < 50; i++) data[i] = 0x00;
		for (int i = 50; i < 100; i++) data[i] = 0x80;
		// 50% dark, threshold 60% -> passes
		Assert.True(GrayBytesUtils.VerifyGrayScaleValues(data, darkProcent: 60));
		// 50% dark, threshold 40% -> fails
		Assert.False(GrayBytesUtils.VerifyGrayScaleValues(data, darkProcent: 40));
	}

	[Fact]
	public void PercentageDifference_IdenticalImages_ReturnsZero() {
		byte[] img = new byte[1024];
		var rng = new Random(42);
		rng.NextBytes(img);
		float diff = GrayBytesUtils.PercentageDifference(img, img);
		Assert.Equal(0f, diff);
	}

	[Fact]
	public void PercentageDifference_OppositeImages_ReturnsNonZero() {
		byte[] img1 = new byte[1024];
		byte[] img2 = new byte[1024];
		Array.Fill(img1, (byte)0x00);
		Array.Fill(img2, (byte)0xFF);
		float diff = GrayBytesUtils.PercentageDifference(img1, img2);
		// Max possible difference: 255/256 per pixel = ~0.996
		Assert.True(diff > 0.9f, $"Difference {diff} should be close to 1.0");
	}

	[Fact]
	public void PercentageDifference_SimilarImages_SmallDifference() {
		byte[] img1 = new byte[1024];
		byte[] img2 = new byte[1024];
		Array.Fill(img1, (byte)128);
		Array.Fill(img2, (byte)130); // only 2 values apart
		float diff = GrayBytesUtils.PercentageDifference(img1, img2);
		Assert.True(diff < 0.01f, $"Difference {diff} should be very small");
	}

	[Fact]
	public void FlipGrayScale_DoubleFlip_ReturnsOriginal() {
		byte[] img = new byte[1024]; // 32x32
		var rng = new Random(42);
		rng.NextBytes(img);
		byte[] flipped = GrayBytesUtils.FlipGrayScale(img);
		byte[] doubleFlipped = GrayBytesUtils.FlipGrayScale(flipped);
		Assert.Equal(img, doubleFlipped);
	}

	[Fact]
	public void FlipGrayScale_ReversesEachRow() {
		// Create a simple 32x32 image where each row is [0,1,2,...,31]
		byte[] img = new byte[1024];
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++)
				img[y * 32 + x] = (byte)x;

		byte[] flipped = GrayBytesUtils.FlipGrayScale(img);

		// Each row should now be [31,30,...,1,0]
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++)
				Assert.Equal((byte)(31 - x), flipped[y * 32 + x]);
	}

	[Fact]
	public void FlipGrayScale16x16_DoubleFlip_ReturnsOriginal() {
		byte[] img = new byte[256]; // 16x16
		var rng = new Random(42);
		rng.NextBytes(img);
		byte[] flipped = GrayBytesUtils.FlipGrayScale16x16(img);
		byte[] doubleFlipped = GrayBytesUtils.FlipGrayScale16x16(flipped);
		Assert.Equal(img, doubleFlipped);
	}

	[Fact]
	public void PercentageDifferenceWithoutSpecificPixels_IgnoresBlackPixels() {
		byte[] img1 = new byte[1024];
		byte[] img2 = new byte[1024];
		// All pixels are black (<=0x20) -> should all be skipped
		Array.Fill(img1, (byte)0x10);
		Array.Fill(img2, (byte)0x00);

		// When ignoring black pixels, only non-black pixels are compared.
		// If all are black, counter is 0 and we'd get NaN/error,
		// so set some bright pixels
		img1[0] = 0x80;
		img2[0] = 0x80;
		float diff = GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(img1, img2, ignoreBlackPixels: true, ignoreWhitePixels: false);
		Assert.Equal(0f, diff);
	}
}
