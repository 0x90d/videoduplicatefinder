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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VDF.Core.Utils;

namespace VDF.GUI.Utils {
	static class ImageUtils {
		/// <summary>
		/// Composes <paramref name="images"/> into a horizontal-strip thumbnail and returns
		/// a WriteableBitmap for immediate UI use. If <paramref name="jpegOut"/> is supplied,
		/// the JPEG is written there FIRST, before the WriteableBitmap is built — that way
		/// a failure on the Avalonia side (e.g. WriteableBitmap creation, multi-buffer
		/// pixel-row layouts) still produces a valid cache entry. Reversing this order is
		/// what caused issue #751: ImageSharp returns multi-buffer pixel storage above ~4 MB
		/// per Bgra32 image, so 5×500-wide portrait composites tripped the old early-null
		/// return before SaveAsJpeg ran, leaving the on-disk cache full of empty entries.
		/// </summary>
		public static unsafe Bitmap? JoinImages(IReadOnlyList<Image> images, Stream? jpegOut = null) {
			if (images == null || images.Count == 0) return null;

			using var img = JpegCompositor.BuildComposite(images);

			if (jpegOut != null) {
				try {
					img.SaveAsJpeg(jpegOut, JpegCompositor.SharedEncoder);
					try { jpegOut.Flush(); } catch { /* ignore */ }
					if (jpegOut.CanSeek) { try { jpegOut.Position = 0; } catch { /* ignore */ } }
				}
				catch { /* the cache write is best-effort; UI bitmap below is independent */ }
			}

			try {
				using var bgraImage = img.CloneAs<Bgra32>();

				var writeableBitmap = new WriteableBitmap(
					new Avalonia.PixelSize(bgraImage.Width, bgraImage.Height),
					new Avalonia.Vector(96, 96),
					PixelFormat.Bgra8888,
					AlphaFormat.Unpremul
				);

				using (var lockedFramebuffer = writeableBitmap.Lock()) {
					byte* destBase = (byte*)lockedFramebuffer.Address;
					int destStride = lockedFramebuffer.RowBytes;
					int rowBytes = bgraImage.Width * 4;

					// ProcessPixelRows is buffer-layout-agnostic — works whether ImageSharp
					// stored the image as a single contiguous span or split it across
					// multiple internal buffers (the latter happens above ~4 MB Bgra32,
					// which is exactly the case that broke before).
					bgraImage.ProcessPixelRows(accessor => {
						for (int y = 0; y < accessor.Height; y++) {
							Span<Bgra32> srcRow = accessor.GetRowSpan(y);
							Span<byte> srcBytes = MemoryMarshal.AsBytes(srcRow);
							Span<byte> destRow = new Span<byte>(destBase + (y * destStride), rowBytes);
							srcBytes.CopyTo(destRow);
						}
					});
				}

				return writeableBitmap;
			}
			catch {
				return null;
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
		public static byte[] ToByteArray(this SixLabors.ImageSharp.Image image) {
			using MemoryStream ms = new();
			image.Save(ms, JpegCompositor.SharedEncoder);
			return ms.ToArray();
		}

	}
}
