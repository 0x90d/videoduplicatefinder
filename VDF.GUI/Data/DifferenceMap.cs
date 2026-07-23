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

namespace VDF.GUI.Data {
	/// <summary>
	/// Pure math for the comparer's Difference view: turns two frames into a mask of
	/// regions that differ STRUCTURALLY (logos, subtitles, crop bars) while staying
	/// blind to global brightness/contrast/color-grade shifts and codec noise. Each
	/// image is reduced to luma, normalized to zero mean / unit variance (an affine
	/// brightness or contrast change cancels exactly), blurred to swallow resampling
	/// and block noise, then differenced and thresholded. Operates on raw arrays so
	/// it is unit-testable without the Avalonia runtime; DiffOverlayRenderer owns the
	/// bitmap plumbing.
	/// </summary>
	internal static class DifferenceMap {
		// Analysis resolution: enough to localize a logo, small enough that a 4K pair
		// diffes in a few milliseconds. B is resampled onto A's grid, so the mask
		// always spans image A; an aspect-ratio mismatch shows up as difference bands,
		// which is the honest answer for such a pair.
		internal const int MaxAnalysisSize = 384;

		/// <summary>Analysis grid for a source frame: fit within MaxAnalysisSize, never upscale.</summary>
		internal static (int Width, int Height) AnalysisSize(int srcWidth, int srcHeight, int max = MaxAnalysisSize) {
			if (srcWidth <= 0 || srcHeight <= 0) return (0, 0);
			double scale = Math.Min(1.0, (double)max / Math.Max(srcWidth, srcHeight));
			return (Math.Max(1, (int)Math.Round(srcWidth * scale)), Math.Max(1, (int)Math.Round(srcHeight * scale)));
		}

		/// <summary>
		/// Streaming box-downscaler: BGRA/RGBA rows go in one at a time (so a full-size
		/// frame never needs a second full-size buffer), averaged luma comes out at the
		/// analysis size. Source rows map to grid cells by integer projection; every
		/// source pixel lands in exactly one cell.
		/// </summary>
		internal sealed class LumaDownscaler {
			readonly int srcWidth, srcHeight, dstWidth, dstHeight;
			readonly bool sourceIsRgba;
			readonly float[] sums;
			readonly int[] counts;

			public LumaDownscaler(int srcWidth, int srcHeight, int dstWidth, int dstHeight, bool sourceIsRgba) {
				if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
					throw new ArgumentOutOfRangeException(nameof(srcWidth), "Dimensions must be positive");
				this.srcWidth = srcWidth;
				this.srcHeight = srcHeight;
				this.dstWidth = dstWidth;
				this.dstHeight = dstHeight;
				this.sourceIsRgba = sourceIsRgba;
				sums = new float[dstWidth * dstHeight];
				counts = new int[dstWidth * dstHeight];
			}

			public void AddRow(ReadOnlySpan<byte> pixelRow, int srcY) {
				if (pixelRow.Length < srcWidth * 4)
					throw new ArgumentException("Row shorter than srcWidth * 4", nameof(pixelRow));
				if (srcY < 0 || srcY >= srcHeight) return;
				int dy = Math.Min(dstHeight - 1, (int)((long)srcY * dstHeight / srcHeight));
				int rowBase = dy * dstWidth;
				for (int x = 0; x < srcWidth; x++) {
					int dx = Math.Min(dstWidth - 1, (int)((long)x * dstWidth / srcWidth));
					int o = x * 4;
					byte c0 = pixelRow[o];
					byte g = pixelRow[o + 1];
					byte c2 = pixelRow[o + 2];
					float r = sourceIsRgba ? c0 : c2;
					float b = sourceIsRgba ? c2 : c0;
					sums[rowBase + dx] += 0.299f * r + 0.587f * g + 0.114f * b;
					counts[rowBase + dx]++;
				}
			}

			public float[] Finish() {
				var result = new float[sums.Length];
				for (int i = 0; i < sums.Length; i++)
					result[i] = counts[i] > 0 ? sums[i] / counts[i] : 0f;
				return result;
			}
		}

		/// <summary>
		/// Zero-mean/unit-variance normalization. A flat (no-contrast) image has no
		/// structure to compare, so it maps to all zeros instead of amplifying noise
		/// through a near-zero divisor.
		/// </summary>
		internal static float[] Normalize(float[] luma) {
			var result = new float[luma.Length];
			if (luma.Length == 0) return result;
			double sum = 0;
			for (int i = 0; i < luma.Length; i++) sum += luma[i];
			float mean = (float)(sum / luma.Length);
			double varSum = 0;
			for (int i = 0; i < luma.Length; i++) {
				float d = luma[i] - mean;
				varSum += d * d;
			}
			float std = (float)Math.Sqrt(varSum / luma.Length);
			if (std < 1e-4f) return result;
			for (int i = 0; i < luma.Length; i++)
				result[i] = (luma[i] - mean) / std;
			return result;
		}

		/// <summary>3x3 box blur with clamped edges, separable passes.</summary>
		internal static float[] BoxBlur3(float[] src, int width, int height) {
			var tmp = new float[src.Length];
			var dst = new float[src.Length];
			for (int y = 0; y < height; y++) {
				int rowBase = y * width;
				for (int x = 0; x < width; x++) {
					float l = src[rowBase + Math.Max(0, x - 1)];
					float c = src[rowBase + x];
					float r = src[rowBase + Math.Min(width - 1, x + 1)];
					tmp[rowBase + x] = (l + c + r) / 3f;
				}
			}
			for (int y = 0; y < height; y++) {
				int up = Math.Max(0, y - 1) * width;
				int mid = y * width;
				int down = Math.Min(height - 1, y + 1) * width;
				for (int x = 0; x < width; x++)
					dst[mid + x] = (tmp[up + x] + tmp[mid + x] + tmp[down + x]) / 3f;
			}
			return dst;
		}

		/// <summary>
		/// Slider position (0..1, higher = more sensitive) to threshold in normalized
		/// luma units. Structural differences (a logo over content) land around 1.5-3;
		/// residual noise from re-encoding lands well under 0.3 after the blurs.
		/// </summary>
		internal static float ThresholdFor(double sensitivity) =>
			(float)(1.8 - 1.55 * Math.Clamp(sensitivity, 0.0, 1.0));

		/// <summary>
		/// Full pipeline on two same-size luma grids. Returns a per-pixel alpha mask:
		/// 0 where the frames agree, 90..230 (scaled by how far past the threshold the
		/// difference is) where they structurally differ.
		/// </summary>
		internal static byte[] Compute(float[] lumaA, float[] lumaB, int width, int height, double sensitivity) {
			if (lumaA.Length != width * height || lumaB.Length != width * height)
				throw new ArgumentException("Luma arrays must match width * height");
			var a = BoxBlur3(Normalize(lumaA), width, height);
			var b = BoxBlur3(Normalize(lumaB), width, height);
			var diff = new float[width * height];
			for (int i = 0; i < diff.Length; i++)
				diff[i] = MathF.Abs(a[i] - b[i]);
			// Second blur pools the difference over a small neighborhood, so a genuine
			// region survives thresholding while single-pixel disagreements dilute away.
			diff = BoxBlur3(diff, width, height);
			return ThresholdMask(diff, width, height, ThresholdFor(sensitivity));
		}

		/// <summary>Bounding box of one difference region, normalized to 0..1 of the analysis grid.</summary>
		internal readonly record struct DiffRegion(float X, float Y, float Width, float Height);

		internal const int MaxRegions = 12;
		// Boxes closer than this (analysis pixels) fuse into one, so a subtitle line or
		// broken-up logo reads as one rectangle instead of confetti.
		const int RegionMergeGap = 3;
		const int MinRegionPixels = 4;
		const int RegionPadding = 2;

		/// <summary>
		/// Groups the mask into rectangles the UI can draw around: connected components
		/// (8-way), tiny ones dropped, near-neighbors merged, largest MaxRegions kept.
		/// </summary>
		internal static List<DiffRegion> FindDifferenceRegions(byte[] mask, int width, int height, int maxRegions = MaxRegions) {
			var boxes = new List<(int MinX, int MinY, int MaxX, int MaxY, int Pixels)>();
			var visited = new bool[width * height];
			var stack = new Stack<int>();

			for (int start = 0; start < mask.Length; start++) {
				if (mask[start] == 0 || visited[start]) continue;
				int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, pixels = 0;
				visited[start] = true;
				stack.Push(start);
				while (stack.Count > 0) {
					int i = stack.Pop();
					int x = i % width, y = i / width;
					pixels++;
					if (x < minX) minX = x;
					if (x > maxX) maxX = x;
					if (y < minY) minY = y;
					if (y > maxY) maxY = y;
					for (int dy = -1; dy <= 1; dy++) {
						int ny = y + dy;
						if (ny < 0 || ny >= height) continue;
						for (int dx = -1; dx <= 1; dx++) {
							int nx = x + dx;
							if (nx < 0 || nx >= width) continue;
							int ni = ny * width + nx;
							if (mask[ni] == 0 || visited[ni]) continue;
							visited[ni] = true;
							stack.Push(ni);
						}
					}
				}
				if (pixels >= MinRegionPixels)
					boxes.Add((minX, minY, maxX, maxY, pixels));
			}

			// Fuse boxes whose gap is within RegionMergeGap until nothing changes.
			bool mergedAny = true;
			while (mergedAny) {
				mergedAny = false;
				for (int i = 0; i < boxes.Count && !mergedAny; i++) {
					for (int j = i + 1; j < boxes.Count; j++) {
						var a = boxes[i];
						var b = boxes[j];
						// Empty pixels between the boxes per axis; negative = overlap.
						int sepX = Math.Max(a.MinX, b.MinX) - Math.Min(a.MaxX, b.MaxX) - 1;
						int sepY = Math.Max(a.MinY, b.MinY) - Math.Min(a.MaxY, b.MaxY) - 1;
						if (sepX <= RegionMergeGap && sepY <= RegionMergeGap) {
							boxes[i] = (Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY),
								Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY), a.Pixels + b.Pixels);
							boxes.RemoveAt(j);
							mergedAny = true;
							break;
						}
					}
				}
			}

			boxes.Sort((a, b) => b.Pixels.CompareTo(a.Pixels));
			if (boxes.Count > maxRegions)
				boxes.RemoveRange(maxRegions, boxes.Count - maxRegions);

			var regions = new List<DiffRegion>(boxes.Count);
			foreach (var box in boxes) {
				int x0 = Math.Max(0, box.MinX - RegionPadding);
				int y0 = Math.Max(0, box.MinY - RegionPadding);
				int x1 = Math.Min(width - 1, box.MaxX + RegionPadding);
				int y1 = Math.Min(height - 1, box.MaxY + RegionPadding);
				regions.Add(new DiffRegion(
					(float)x0 / width, (float)y0 / height,
					(float)(x1 - x0 + 1) / width, (float)(y1 - y0 + 1) / height));
			}
			return regions;
		}

		/// <summary>
		/// Threshold plus speck removal: a passing pixel needs at least two passing
		/// 8-neighbors, which keeps 1px-wide logo strokes but drops isolated pixels.
		/// </summary>
		internal static byte[] ThresholdMask(float[] diff, int width, int height, float threshold) {
			var pass = new bool[width * height];
			for (int i = 0; i < pass.Length; i++)
				pass[i] = diff[i] >= threshold;

			var mask = new byte[width * height];
			float thresholdScale = Math.Max(threshold, 1e-3f);
			for (int y = 0; y < height; y++) {
				for (int x = 0; x < width; x++) {
					int i = y * width + x;
					if (!pass[i]) continue;
					int neighbors = 0;
					for (int dy = -1; dy <= 1; dy++) {
						int ny = y + dy;
						if (ny < 0 || ny >= height) continue;
						for (int dx = -1; dx <= 1; dx++) {
							if (dx == 0 && dy == 0) continue;
							int nx = x + dx;
							if (nx < 0 || nx >= width) continue;
							if (pass[ny * width + nx]) neighbors++;
						}
					}
					if (neighbors < 2) continue;
					float over = (diff[i] - threshold) / thresholdScale;
					mask[i] = (byte)(90 + (int)(140 * Math.Min(1f, over * 0.5f)));
				}
			}
			return mask;
		}
	}
}
