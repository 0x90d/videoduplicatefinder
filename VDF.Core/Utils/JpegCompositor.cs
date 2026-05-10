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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VDF.Core.Utils {
	/// <summary>
	/// Composes multiple thumbnails into a single horizontal-strip JPEG.
	///
	/// Lives in VDF.Core (not the GUI) so the JPEG path is testable without
	/// Avalonia, and so the GUI can call it BEFORE attempting the
	/// WriteableBitmap conversion. That ordering matters: if the WriteableBitmap
	/// path fails (large multi-buffer images break ImageSharp's
	/// DangerousTryGetSinglePixelMemory contract), the JPEG must still get
	/// written or the on-disk thumbnail cache records empty entries that the
	/// retry filter then skips forever (issue #751).
	/// </summary>
	public static class JpegCompositor {
		// Mirrors GUI behaviour: anything beyond this gets resampled to keep the
		// composite bitmap inside Avalonia/UI texture limits. Keep the constants
		// here so the JPEG-side and the Bitmap-side stay in agreement when the
		// GUI also resizes its in-memory copy.
		public const int MaxDisplayableCompositeWidth = 4096;
		public const int AbsoluteMaxWidth = 32767;

		static readonly JpegEncoder Encoder = new() { Quality = 90 };

		/// <summary>
		/// Concatenates <paramref name="images"/> horizontally and writes a JPEG
		/// to <paramref name="jpegOut"/>. Returns false (and writes nothing) if
		/// the input is empty.
		/// </summary>
		public static bool TryWriteJoinedJpeg(IReadOnlyList<Image> images, Stream jpegOut) {
			if (images == null || images.Count == 0) return false;

			using var composite = BuildComposite(images);
			composite.SaveAsJpeg(jpegOut, Encoder);
			try { jpegOut.Flush(); } catch { /* ignore */ }
			if (jpegOut.CanSeek) {
				try { jpegOut.Position = 0; } catch { /* ignore */ }
			}
			return true;
		}

		/// <summary>
		/// Shared encoder for callers that need a quality-90 baseline JPEG.
		/// Encoders are stateless across writes, so a single instance is safe to share.
		/// </summary>
		public static SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder SharedEncoder => Encoder;

		/// <summary>
		/// Builds the in-memory composite without encoding. Caller owns disposal.
		/// Used by the GUI to also build a WriteableBitmap from the same pixels.
		/// </summary>
		public static Image<Rgba32> BuildComposite(IReadOnlyList<Image> images) {
			if (images == null || images.Count == 0)
				throw new ArgumentException("images must contain at least one element", nameof(images));

			int height = images[0].Height;
			int width = 0;
			for (int i = 0; i < images.Count; i++) width += images[i].Width;

			var img = new Image<Rgba32>(width, height);
			img.Mutate(ctx => {
				int offsetX = 0;
				foreach (var src in images) {
					ctx.DrawImage(src, new Point(offsetX, 0), 1f);
					offsetX += src.Width;
				}
			});

			if (img.Width > AbsoluteMaxWidth) {
				img.Mutate(x => x.Resize(new ResizeOptions {
					Size = new Size(AbsoluteMaxWidth, 0),
					Mode = ResizeMode.Max,
					Sampler = KnownResamplers.Lanczos3
				}));
			}
			if (img.Width > MaxDisplayableCompositeWidth) {
				img.Mutate(x => x.Resize(new ResizeOptions {
					Size = new Size(MaxDisplayableCompositeWidth, 0),
					Mode = ResizeMode.Max,
					Sampler = KnownResamplers.Lanczos3
				}));
			}
			return img;
		}
	}
}
