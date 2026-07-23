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

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VDF.GUI.Data;

namespace VDF.GUI.Utils {
	/// <summary>
	/// Avalonia side of the comparer's "Highlight differences" toggle: pulls pixels
	/// out of the two displayed frames, feeds them through DifferenceMap and draws a
	/// red rectangle around each differing region on a transparent bitmap. The overlay
	/// keeps image A's aspect ratio, so a Stretch=Uniform Image lands on exactly the
	/// same rect as the frame underneath it and the boxes track zoom/pan for free.
	/// </summary>
	static class DiffOverlayRenderer {
		// The outlines are drawn at up to this resolution (not the 384px analysis grid)
		// so they stay crisp when the user zooms into a marked region.
		const int MaxOverlaySize = 1536;

		/// <summary>
		/// Returns the outline overlay (fully transparent when nothing differs), or
		/// null when pixels cannot be read from either bitmap.
		/// </summary>
		public static Bitmap? Render(Bitmap imageA, Bitmap imageB, double sensitivity) {
			var (width, height) = DifferenceMap.AnalysisSize(imageA.PixelSize.Width, imageA.PixelSize.Height);
			if (width <= 0 || height <= 0) return null;
			if (imageB.PixelSize.Width <= 0 || imageB.PixelSize.Height <= 0) return null;

			var lumaA = ExtractLuma(imageA, width, height);
			var lumaB = ExtractLuma(imageB, width, height);
			if (lumaA == null || lumaB == null) return null;

			var mask = DifferenceMap.Compute(lumaA, lumaB, width, height, sensitivity);
			var regions = DifferenceMap.FindDifferenceRegions(mask, width, height);

			var (overlayW, overlayH) = DifferenceMap.AnalysisSize(
				imageA.PixelSize.Width, imageA.PixelSize.Height, MaxOverlaySize);
			return BuildOverlay(regions, overlayW, overlayH);
		}

		static float[]? ExtractLuma(Bitmap bmp, int dstWidth, int dstHeight) =>
			TryExtractViaCopyPixels(bmp, dstWidth, dstHeight) ?? TryExtractViaReencode(bmp, dstWidth, dstHeight);

		/// <summary>
		/// Fast path: banded CopyPixels straight off the decoded bitmap, strip by strip,
		/// so a full-size 4K/photo frame never needs a second full-size buffer.
		/// </summary>
		static float[]? TryExtractViaCopyPixels(Bitmap bmp, int dstWidth, int dstHeight) {
			try {
				int srcWidth = bmp.PixelSize.Width, srcHeight = bmp.PixelSize.Height;
				var format = bmp.Format;
				if (format != PixelFormat.Bgra8888 && format != PixelFormat.Rgba8888) return null;
				bool rgba = format == PixelFormat.Rgba8888;

				var scaler = new DifferenceMap.LumaDownscaler(srcWidth, srcHeight, dstWidth, dstHeight, rgba);
				int stride = srcWidth * 4;
				int stripRows = Math.Clamp(8 * 1024 * 1024 / stride, 1, srcHeight);
				var buffer = new byte[stripRows * stride];
				var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
				try {
					for (int y = 0; y < srcHeight; y += stripRows) {
						int rows = Math.Min(stripRows, srcHeight - y);
						bmp.CopyPixels(new PixelRect(0, y, srcWidth, rows), handle.AddrOfPinnedObject(), rows * stride, stride);
						for (int r = 0; r < rows; r++)
							scaler.AddRow(buffer.AsSpan(r * stride, stride), y + r);
					}
				}
				finally {
					handle.Free();
				}
				return scaler.Finish();
			}
			catch {
				return null;
			}
		}

		/// <summary>
		/// Fallback for bitmaps whose pixels aren't directly readable (RenderTargetBitmap
		/// composites, exotic formats): round-trip through an encoded stream into a
		/// lockable WriteableBitmap. Slower, but this path is rare.
		/// </summary>
		static unsafe float[]? TryExtractViaReencode(Bitmap bmp, int dstWidth, int dstHeight) {
			try {
				using var ms = new MemoryStream();
				bmp.Save(ms);
				ms.Position = 0;
				using var decoded = WriteableBitmap.Decode(ms);
				using var fb = decoded.Lock();
				bool rgba = fb.Format == PixelFormat.Rgba8888;
				if (fb.Format != PixelFormat.Bgra8888 && !rgba) return null;
				int srcWidth = fb.Size.Width, srcHeight = fb.Size.Height;
				var scaler = new DifferenceMap.LumaDownscaler(srcWidth, srcHeight, dstWidth, dstHeight, rgba);
				byte* src = (byte*)fb.Address;
				for (int y = 0; y < srcHeight; y++)
					scaler.AddRow(new ReadOnlySpan<byte>(src + (long)y * fb.RowBytes, srcWidth * 4), y);
				return scaler.Finish();
			}
			catch {
				return null;
			}
		}

		static unsafe WriteableBitmap BuildOverlay(IReadOnlyList<DifferenceMap.DiffRegion> regions, int width, int height) {
			var overlay = new WriteableBitmap(
				new PixelSize(width, height),
				new Vector(96, 96),
				PixelFormat.Bgra8888,
				AlphaFormat.Unpremul);
			using var fb = overlay.Lock();
			byte* dst = (byte*)fb.Address;
			// Start fully transparent; WriteableBitmap memory is not guaranteed zeroed.
			for (int y = 0; y < height; y++)
				new Span<byte>(dst + (long)y * fb.RowBytes, width * 4).Clear();

			int thickness = Math.Max(2, (int)Math.Round(Math.Max(width, height) * 0.003));
			foreach (var region in regions) {
				int x0 = Math.Clamp((int)(region.X * width), 0, width - 1);
				int y0 = Math.Clamp((int)(region.Y * height), 0, height - 1);
				int x1 = Math.Clamp((int)Math.Ceiling((region.X + region.Width) * width), x0 + 1, width);
				int y1 = Math.Clamp((int)Math.Ceiling((region.Y + region.Height) * height), y0 + 1, height);

				DrawBand(dst, fb.RowBytes, x0, y0, x1, Math.Min(y0 + thickness, y1));      // top
				DrawBand(dst, fb.RowBytes, x0, Math.Max(y1 - thickness, y0), x1, y1);      // bottom
				DrawBand(dst, fb.RowBytes, x0, y0, Math.Min(x0 + thickness, x1), y1);      // left
				DrawBand(dst, fb.RowBytes, Math.Max(x1 - thickness, x0), y0, x1, y1);      // right
			}
			return overlay;
		}

		static unsafe void DrawBand(byte* dst, int rowBytes, int x0, int y0, int x1, int y1) {
			for (int y = y0; y < y1; y++) {
				byte* row = dst + (long)y * rowBytes;
				for (int x = x0; x < x1; x++) {
					int o = x * 4;
					row[o] = 40;       // B
					row[o + 1] = 40;   // G
					row[o + 2] = 255;  // R
					row[o + 3] = 235;
				}
			}
		}
	}
}
