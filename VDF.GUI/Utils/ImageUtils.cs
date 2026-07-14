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


using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VDF.Core.FFTools;

namespace VDF.GUI.Utils {
	static class ImageUtils {
		// Anything beyond this gets downscaled to keep the composite inside
		// Avalonia/UI texture limits.
		public const int MaxDisplayableCompositeWidth = 4096;
		// Hard sanity cap for the intermediate compose buffer (BGRA bytes).
		const long MaxCompositeBufferBytes = 256_000_000;

		/// <summary>
		/// Composes <paramref name="encodedImages"/> (JPEG/PNG bytes) into a composite
		/// thumbnail and returns a Bitmap for immediate UI use. The frames are packed
		/// into a grid purely for storage (texture-size limits); the results view slices
		/// the cells back out and re-wraps them to the Preview column width at display
		/// time (WrappedFilmstrip, #834/#847). <paramref name="gridColumns"/> is the
		/// caller's column count (from ThumbnailGridLayout, persisted on the item) so
		/// compose and display always agree on the cell geometry; each frame occupies
		/// the cell of its list index, even when a neighbor fails to decode. Pass 0 to
		/// let the layout be derived from the decoded frames.
		/// If <paramref name="jpegOut"/> is supplied, the composite JPEG is written there
		/// FIRST, before the UI bitmap is built — that way a failure on the Avalonia side
		/// still produces a valid cache entry (issue #751).
		/// Decoding uses Avalonia/Skia; the composite JPEG is encoded via FFmpeg.
		/// </summary>
		public static unsafe Bitmap? JoinImages(IReadOnlyList<byte[]> encodedImages, Stream? jpegOut = null, int gridColumns = 0) {
			if (encodedImages == null || encodedImages.Count == 0) return null;

			var parts = new WriteableBitmap?[encodedImages.Count];
			try {
				int cellWidth = 0, cellHeight = 0, decoded = 0;
				for (int i = 0; i < encodedImages.Count; i++) {
					var bytes = encodedImages[i];
					if (bytes == null || bytes.Length == 0) continue;
					using var ms = new MemoryStream(bytes);
					var part = WriteableBitmap.Decode(ms);
					parts[i] = part;
					decoded++;
					cellWidth = Math.Max(cellWidth, part.PixelSize.Width);
					cellHeight = Math.Max(cellHeight, part.PixelSize.Height);
				}
				if (decoded == 0 || cellWidth <= 0 || cellHeight <= 0) return null;

				int frameCount = encodedImages.Count;
				int columns = gridColumns >= 1
					? Math.Min(gridColumns, frameCount)
					: ThumbnailGridLayout.Columns(frameCount, (double)cellWidth / cellHeight);
				int rows = ThumbnailGridLayout.Rows(frameCount, columns);
				int gridWidth = columns * cellWidth, gridHeight = rows * cellHeight;
				if ((long)gridWidth * gridHeight * 4 > MaxCompositeBufferBytes) return null;

				// Compose raw BGRA grid (cells smaller than the grid cell stay transparent).
				byte[] strip = new byte[(long)gridWidth * gridHeight * 4];
				for (int i = 0; i < parts.Length; i++) {
					var part = parts[i];
					if (part == null) continue;
					int xOffset = (i % columns) * cellWidth;
					int yOffset = (i / columns) * cellHeight;
					using var fb = part.Lock();
					int w = fb.Size.Width, h = fb.Size.Height;
					bool isRgba = fb.Format == PixelFormat.Rgba8888;
					if (fb.Format != PixelFormat.Bgra8888 && !isRgba)
						return null; // unexpected decoder output — give up rather than show garbage
					byte* src = (byte*)fb.Address;
					for (int y = 0; y < h; y++) {
						var srcRow = new ReadOnlySpan<byte>(src + (long)y * fb.RowBytes, w * 4);
						var dstRow = strip.AsSpan((((yOffset + y) * gridWidth) + xOffset) * 4, w * 4);
						if (!isRgba) {
							srcRow.CopyTo(dstRow);
						}
						else {
							for (int x = 0; x < w * 4; x += 4) {
								dstRow[x] = srcRow[x + 2];     // B
								dstRow[x + 1] = srcRow[x + 1]; // G
								dstRow[x + 2] = srcRow[x];     // R
								dstRow[x + 3] = srcRow[x + 3]; // A
							}
						}
					}
				}

				// Encode the composite via FFmpeg. The cache write happens before the UI bitmap
				// is decoded, preserving the cache-first guarantee.
				byte[]? jpeg = FfmpegEngine.EncodeJpegFromBgra(strip, gridWidth, gridHeight, MaxDisplayableCompositeWidth);
				if (jpeg != null) {
					if (jpegOut != null) {
						try {
							jpegOut.Write(jpeg, 0, jpeg.Length);
							try { jpegOut.Flush(); } catch { /* ignore */ }
							if (jpegOut.CanSeek) { try { jpegOut.Position = 0; } catch { /* ignore */ } }
						}
						catch { /* the cache write is best-effort; UI bitmap below is independent */ }
					}
					using var jpegMs = new MemoryStream(jpeg);
					return new Bitmap(jpegMs);
				}

				// FFmpeg encode failed — still give the UI something (no cache entry written).
				if (gridWidth > MaxDisplayableCompositeWidth) return null;
				var fallback = new WriteableBitmap(
					new PixelSize(gridWidth, gridHeight),
					new Vector(96, 96),
					PixelFormat.Bgra8888,
					AlphaFormat.Unpremul);
				using (var fb = fallback.Lock()) {
					byte* dest = (byte*)fb.Address;
					int rowBytes = gridWidth * 4;
					fixed (byte* src = strip) {
						for (int y = 0; y < gridHeight; y++)
							Buffer.MemoryCopy(src + (long)y * rowBytes, dest + (long)y * fb.RowBytes, rowBytes, rowBytes);
					}
				}
				return fallback;
			}
			catch {
				return null;
			}
			finally {
				foreach (var part in parts)
					part?.Dispose();
			}
		}
		public static unsafe Bitmap? JoinImages(IReadOnlyList<Bitmap> images, Stream? jpegOut = null) {
			if (images == null || images.Count == 0) return null;

			int h = images[0].PixelSize.Height;
			int w = 0; for (int i = 0; i < images.Count; i++) w += images[i].PixelSize.Width;

			RenderTargetBitmap rtb = new(new PixelSize(w, h));

			using var dc = rtb.CreateDrawingContext();
			//dc.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));

			double x = 0;
			foreach (var bmp in images) {
				var src = new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
				var dst = new Rect(x, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
				dc.DrawImage(bmp, src, dst);
				x += bmp.PixelSize.Width;
			}
			return rtb;
		}

		public static byte[] ToByteArray(this Bitmap image) {
			using MemoryStream ms = new();
			image.Save(ms);
			return ms.ToArray();
		}

	}
}
