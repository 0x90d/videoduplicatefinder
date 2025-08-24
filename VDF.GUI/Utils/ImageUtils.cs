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


using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VDF.GUI.Utils {
	static class ImageUtils {
		private static readonly JpegEncoder JpegEncoder = new() { Quality = 90 };
		public static unsafe Bitmap? JoinImages(IReadOnlyList<Image> images) {
			if (images == null || images.Count == 0) return null;

			int height = images[0].Height;
			int width = 0;
			for (int i = 0; i <= images.Count - 1; i++)
				width += images[i].Width;

			using var img = new Image<Rgba32>(width, height);

			img.Mutate(ctx =>
			{
				int offsetX = 0;
				foreach (var img in images) {
					ctx.DrawImage(img, new Point(offsetX, 0), 1f);
					offsetX += img.Width;
				}
			});

			// Resize-Limits
			const int MaxDisplayableCompositeWidth = 4096; // UI-Limit
			const int AbsoluteMaxWidth = 32767;            // Hard-Limit (z.B. Texture-Limits)

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

			try {
				using var bgraImage = img.CloneAs<Bgra32>();

				if (!bgraImage.DangerousTryGetSinglePixelMemory(out var pixelMemory))
					return null;

				Span<byte> sourcePixelData = MemoryMarshal.AsBytes(pixelMemory.Span);

				var writeableBitmap = new WriteableBitmap(
					new Avalonia.PixelSize(bgraImage.Width, bgraImage.Height),
					new Avalonia.Vector(96, 96),
					PixelFormat.Bgra8888,
					AlphaFormat.Unpremul
				);

				using (var lockedFramebuffer = writeableBitmap.Lock()) {
					Span<byte> destinationSpan = new Span<byte>(
						(void*)lockedFramebuffer.Address,
						lockedFramebuffer.Size.Height * lockedFramebuffer.RowBytes
					);

					int expectedSourceLength = bgraImage.Width * bgraImage.Height * 4;

					if (sourcePixelData.Length == expectedSourceLength && destinationSpan.Length == expectedSourceLength) {
						sourcePixelData.CopyTo(destinationSpan);
					}
					else if (sourcePixelData.Length == destinationSpan.Length) {
						int sourceStride = bgraImage.Width * 4;
						int destStride = lockedFramebuffer.RowBytes;
						for (int y = 0; y < bgraImage.Height; y++) {
							Span<byte> sourceRow = sourcePixelData.Slice(y * sourceStride, sourceStride);
							Span<byte> destRow = destinationSpan.Slice(y * destStride, sourceStride);
							sourceRow.CopyTo(destRow);
						}
					}
					else {
						return null; // sizes do not fit
					}
				}

				return writeableBitmap;
			}
			catch {
				return null;
			}
		}

		public static byte[] ToByteArray(this Bitmap image) {
			using MemoryStream ms = new();
			image.Save(ms);
			return ms.ToArray();
		}
		public static byte[] ToByteArray(this Image image) {
			using MemoryStream ms = new();
			image.Save(ms, JpegEncoder);
			return ms.ToArray();
		}
	}
}
