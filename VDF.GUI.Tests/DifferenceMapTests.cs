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

using VDF.GUI.Data;

namespace VDF.GUI.Tests;

// The Difference view's promise is asymmetric: a logo/subtitle/crop must light up,
// while a global brightness, contrast or gamma change must NOT flood the mask —
// that failure mode (naive pixel diff highlighting everything) is exactly why the
// pipeline normalizes per image before differencing.
public class DifferenceMapTests {

	static float[] Checker(int width, int height, int cell, float dark, float light) {
		var result = new float[width * height];
		for (int y = 0; y < height; y++)
			for (int x = 0; x < width; x++)
				result[y * width + x] = ((x / cell) + (y / cell)) % 2 == 0 ? dark : light;
		return result;
	}

	static float[] WithRect(float[] src, int width, int x0, int y0, int rw, int rh, float value) {
		var result = (float[])src.Clone();
		for (int y = y0; y < y0 + rh; y++)
			for (int x = x0; x < x0 + rw; x++)
				result[y * width + x] = value;
		return result;
	}

	static int CountHighlighted(byte[] mask) {
		int n = 0;
		foreach (var m in mask)
			if (m > 0) n++;
		return n;
	}

	[Fact]
	public void BrightnessAndContrastShift_ProducesNoHighlight() {
		// B = A * 1.15 + 10: an affine change cancels exactly under normalization,
		// even at high sensitivity.
		var a = Checker(96, 96, 8, 40, 210);
		var b = new float[a.Length];
		for (int i = 0; i < a.Length; i++) b[i] = a[i] * 1.15f + 10f;

		var mask = DifferenceMap.Compute(a, b, 96, 96, sensitivity: 0.85);
		Assert.Equal(0, CountHighlighted(mask));
	}

	[Fact]
	public void GammaShift_ProducesNoHighlightAtDefaultSensitivity() {
		// Nonlinear tone change (gamma 0.85) leaves a small structural residual;
		// it must stay below the default threshold.
		var a = Checker(96, 96, 8, 40, 210);
		var b = new float[a.Length];
		for (int i = 0; i < a.Length; i++) b[i] = 255f * MathF.Pow(a[i] / 255f, 0.85f);

		var mask = DifferenceMap.Compute(a, b, 96, 96, sensitivity: 0.5);
		Assert.Equal(0, CountHighlighted(mask));
	}

	[Fact]
	public void IdenticalImages_ProduceNoHighlightEvenAtMaxSensitivity() {
		var a = Checker(64, 64, 8, 40, 210);
		var mask = DifferenceMap.Compute(a, (float[])a.Clone(), 64, 64, sensitivity: 1.0);
		Assert.Equal(0, CountHighlighted(mask));
	}

	[Fact]
	public void FlatImagesAtDifferentLevels_ProduceNoHighlight() {
		// Zero-contrast images have no structure; the std guard must not blow up
		// the difference into a full-frame highlight.
		var a = new float[64 * 64];
		var b = new float[64 * 64];
		for (int i = 0; i < b.Length; i++) { a[i] = 30f; b[i] = 200f; }
		var mask = DifferenceMap.Compute(a, b, 64, 64, sensitivity: 1.0);
		Assert.Equal(0, CountHighlighted(mask));
	}

	[Fact]
	public void Logo_HighlightsOnlyTheLogoRegion() {
		// A 32x32 white "logo" over a checker pattern: the mask must concentrate
		// inside the logo and stay quiet elsewhere.
		const int W = 128, H = 128;
		const int LX = 84, LY = 84, LS = 32;
		var a = Checker(W, H, 16, 40, 210);
		var b = WithRect(a, W, LX, LY, LS, LS, 255f);

		var mask = DifferenceMap.Compute(a, b, W, H, sensitivity: 0.5);

		int insideHighlighted = 0, insideTotal = 0, outsideHighlighted = 0, outsideTotal = 0;
		const int margin = 3; // blur bleeds a couple of pixels past the logo edge
		for (int y = 0; y < H; y++) {
			for (int x = 0; x < W; x++) {
				bool inside = x >= LX && x < LX + LS && y >= LY && y < LY + LS;
				bool nearLogo = x >= LX - margin && x < LX + LS + margin && y >= LY - margin && y < LY + LS + margin;
				if (inside) {
					insideTotal++;
					if (mask[y * W + x] > 0) insideHighlighted++;
				}
				else if (!nearLogo) {
					outsideTotal++;
					if (mask[y * W + x] > 0) outsideHighlighted++;
				}
			}
		}

		// The dark checker cells under the logo flip hard; the light cells barely
		// change (white on near-white is invisible to the eye too), so expect
		// roughly half the logo area, and essentially nothing outside it.
		Assert.True(insideHighlighted >= insideTotal * 0.30,
			$"Only {insideHighlighted}/{insideTotal} logo pixels highlighted");
		Assert.True(outsideHighlighted <= outsideTotal * 0.005,
			$"{outsideHighlighted}/{outsideTotal} non-logo pixels highlighted");
	}

	[Fact]
	public void HigherSensitivity_NeverHighlightsFewerPixels() {
		const int W = 128, H = 128;
		var a = Checker(W, H, 16, 40, 210);
		var b = WithRect(a, W, 84, 84, 32, 32, 255f);

		int prev = -1;
		foreach (var s in new[] { 0.2, 0.5, 0.8 }) {
			int count = CountHighlighted(DifferenceMap.Compute(a, b, W, H, s));
			Assert.True(count >= prev, $"Sensitivity {s} highlighted {count} < previous {prev}");
			prev = count;
		}
	}

	[Fact]
	public void ThresholdMask_DropsIsolatedSpecks_KeepsRegions() {
		const int W = 32, H = 32;
		var diff = new float[W * H];
		diff[5 * W + 5] = 10f; // isolated pixel: no passing neighbors
		for (int y = 20; y < 23; y++) // 3x3 block: every pixel has >= 2 passing neighbors
			for (int x = 20; x < 23; x++)
				diff[y * W + x] = 10f;

		var mask = DifferenceMap.ThresholdMask(diff, W, H, threshold: 1f);
		Assert.Equal(0, mask[5 * W + 5]);
		for (int y = 20; y < 23; y++)
			for (int x = 20; x < 23; x++)
				Assert.True(mask[y * W + x] > 0, $"Block pixel ({x},{y}) was dropped");
	}

	[Fact]
	public void MaskIntensity_ScalesWithDifferenceStrength() {
		const int W = 8, H = 8;
		var weak = new float[W * H];
		var strong = new float[W * H];
		for (int i = 0; i < W * H; i++) { weak[i] = 1.05f; strong[i] = 5f; }
		var weakMask = DifferenceMap.ThresholdMask(weak, W, H, threshold: 1f);
		var strongMask = DifferenceMap.ThresholdMask(strong, W, H, threshold: 1f);
		Assert.True(weakMask[0] > 0);
		Assert.True(strongMask[0] > weakMask[0]);
		Assert.True(strongMask[0] <= 230);
	}

	[Theory]
	[InlineData(3840, 2160, 384, 216)] // 4K downscales to the cap
	[InlineData(2160, 3840, 216, 384)] // portrait preserved
	[InlineData(300, 200, 300, 200)]   // small frames are never upscaled
	[InlineData(0, 100, 0, 0)]         // degenerate input
	public void AnalysisSize_FitsWithoutUpscaling(int srcW, int srcH, int expectedW, int expectedH) {
		var (w, h) = DifferenceMap.AnalysisSize(srcW, srcH);
		Assert.Equal((expectedW, expectedH), (w, h));
	}

	[Fact]
	public void LumaDownscaler_AveragesCellsAndTreatsBgraRgbaAlike() {
		// 4x4 source to 2x2 grid; gray pixels (R=G=B) make expected luma exact.
		byte[] Row(params byte[] grays) {
			var row = new byte[grays.Length * 4];
			for (int i = 0; i < grays.Length; i++) {
				row[i * 4] = grays[i];
				row[i * 4 + 1] = grays[i];
				row[i * 4 + 2] = grays[i];
				row[i * 4 + 3] = 255;
			}
			return row;
		}

		var bgra = new DifferenceMap.LumaDownscaler(4, 4, 2, 2, sourceIsRgba: false);
		var rgba = new DifferenceMap.LumaDownscaler(4, 4, 2, 2, sourceIsRgba: true);
		byte[][] rows = {
			Row(10, 20, 100, 200),
			Row(30, 40, 100, 200),
			Row(0, 0, 50, 50),
			Row(0, 0, 50, 50),
		};
		for (int y = 0; y < 4; y++) {
			bgra.AddRow(rows[y], y);
			rgba.AddRow(rows[y], y);
		}

		var lumaBgra = bgra.Finish();
		var lumaRgba = rgba.Finish();
		Assert.Equal(25f, lumaBgra[0], 2);  // (10+20+30+40)/4
		Assert.Equal(150f, lumaBgra[1], 2); // (100+200+100+200)/4
		Assert.Equal(0f, lumaBgra[2], 2);
		Assert.Equal(50f, lumaBgra[3], 2);
		// Gray pixels: swapping R and B channels must not change luma.
		Assert.Equal(lumaBgra, lumaRgba);
	}

	[Fact]
	public void Normalize_RemovesAffineShift_AndZeroesFlatInput() {
		var src = new float[] { 10, 20, 30, 40 };
		var shifted = new float[] { 10 * 2 + 5, 20 * 2 + 5, 30 * 2 + 5, 40 * 2 + 5 };
		var n1 = DifferenceMap.Normalize(src);
		var n2 = DifferenceMap.Normalize(shifted);
		for (int i = 0; i < n1.Length; i++)
			Assert.Equal(n1[i], n2[i], 4);

		var flat = DifferenceMap.Normalize(new float[] { 7, 7, 7, 7 });
		Assert.All(flat, v => Assert.Equal(0f, v));
	}

	[Theory]
	[InlineData(0.0, 1.8f)]
	[InlineData(0.5, 1.025f)]
	[InlineData(1.0, 0.25f)]
	[InlineData(-3.0, 1.8f)] // clamped
	[InlineData(9.0, 0.25f)] // clamped
	public void ThresholdFor_MapsAndClampsSensitivity(double sensitivity, float expected) {
		Assert.Equal(expected, DifferenceMap.ThresholdFor(sensitivity), 3);
	}

	// ---- region extraction (the rectangles the UI draws) ----

	static byte[] MaskWithBlobs(int width, int height, params (int X, int Y, int W, int H)[] blobs) {
		var mask = new byte[width * height];
		foreach (var blob in blobs)
			for (int y = blob.Y; y < blob.Y + blob.H; y++)
				for (int x = blob.X; x < blob.X + blob.W; x++)
					mask[y * width + x] = 200;
		return mask;
	}

	[Fact]
	public void Logo_YieldsOneRegionAroundTheLogo() {
		const int W = 128, H = 128;
		const int LX = 84, LY = 84, LS = 32;
		var a = Checker(W, H, 16, 40, 210);
		var b = WithRect(a, W, LX, LY, LS, LS, 255f);

		var mask = DifferenceMap.Compute(a, b, W, H, sensitivity: 0.5);
		var regions = DifferenceMap.FindDifferenceRegions(mask, W, H);

		var region = Assert.Single(regions);
		// Box must sit on the logo (within blur bleed + padding) and span most of it.
		const float slack = 8f / W;
		Assert.InRange(region.X, (float)LX / W - slack, (float)LX / W + slack);
		Assert.InRange(region.Y, (float)LY / H - slack, (float)LY / H + slack);
		Assert.InRange(region.X + region.Width, (float)(LX + LS) / W - slack, (float)(LX + LS) / W + slack);
		Assert.InRange(region.Y + region.Height, (float)(LY + LS) / H - slack, (float)(LY + LS) / H + slack);
	}

	[Fact]
	public void BrightnessShift_YieldsNoRegions() {
		var a = Checker(96, 96, 8, 40, 210);
		var b = new float[a.Length];
		for (int i = 0; i < a.Length; i++) b[i] = a[i] * 1.15f + 10f;
		var mask = DifferenceMap.Compute(a, b, 96, 96, sensitivity: 0.85);
		Assert.Empty(DifferenceMap.FindDifferenceRegions(mask, 96, 96));
	}

	[Fact]
	public void TwoDistantChanges_YieldTwoRegions() {
		const int W = 128, H = 128;
		var a = Checker(W, H, 16, 40, 210);
		var b = WithRect(a, W, 4, 4, 12, 12, 255f);
		b = WithRect(b, W, 100, 100, 12, 12, 255f);
		var mask = DifferenceMap.Compute(a, b, W, H, sensitivity: 0.5);
		Assert.Equal(2, DifferenceMap.FindDifferenceRegions(mask, W, H).Count);
	}

	[Fact]
	public void NearbyFragments_MergeIntoOneRegion() {
		// Two blobs 3px apart (within the merge gap) fuse; 10px apart stay separate.
		var near = MaskWithBlobs(64, 64, (10, 10, 6, 6), (19, 10, 6, 6));
		Assert.Single(DifferenceMap.FindDifferenceRegions(near, 64, 64));

		var far = MaskWithBlobs(64, 64, (10, 10, 6, 6), (40, 10, 6, 6));
		Assert.Equal(2, DifferenceMap.FindDifferenceRegions(far, 64, 64).Count);
	}

	[Fact]
	public void TinyComponents_AreDropped() {
		var tiny = MaskWithBlobs(64, 64, (10, 10, 1, 3)); // 3 px, below the minimum
		Assert.Empty(DifferenceMap.FindDifferenceRegions(tiny, 64, 64));

		var justEnough = MaskWithBlobs(64, 64, (10, 10, 2, 2)); // 4 px
		Assert.Single(DifferenceMap.FindDifferenceRegions(justEnough, 64, 64));
	}

	[Fact]
	public void RegionCount_IsCapped() {
		// 5x5 grid of well-separated blobs; only the MaxRegions largest survive.
		var blobs = new List<(int, int, int, int)>();
		for (int by = 0; by < 5; by++)
			for (int bx = 0; bx < 5; bx++)
				blobs.Add((bx * 24 + 2, by * 24 + 2, 4, 4));
		var mask = MaskWithBlobs(128, 128, blobs.ToArray());
		Assert.Equal(DifferenceMap.MaxRegions, DifferenceMap.FindDifferenceRegions(mask, 128, 128).Count);
	}
}
