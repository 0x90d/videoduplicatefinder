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


using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Runtime.InteropServices; // For MemoryMarshal

namespace VDF.GUI.Utils {
	static class ImageUtils {
		public static Bitmap? JoinImages(List<Image> pImgList) {
			if (pImgList == null || pImgList.Count == 0) return null;

			int height = pImgList[0].Height;
			int width = 0;
			for (int i = 0; i <= pImgList.Count - 1; i++)
				width += pImgList[i].Width;

			// Assuming pImgList contains Rgba32 images, the composite image will also be Rgba32
			using var img = new Image<Rgba32>(width, height);

			// List<Point> locations = new(pImgList.Count); // Not used
			int tmpwidth = 0;
			for (int i = 0; i <= pImgList.Count - 1; i++) {
				img.Mutate(a => a.DrawImage(pImgList[i], new Point(tmpwidth, 0), 1f));
				tmpwidth += pImgList[i].Width;
			}

			// Check if the resulting image exceeds the maximum width or memory
			// Max byte array size for ImageSharp pixel data is typically int.MaxValue / component_size.
			// Resize logic for UI display and hard limits
			const int MaxDisplayableCompositeWidth = 4096; // Max width for the thumbnail strip in UI
			const int AbsoluteMaxWidth = 32767; // Absolute hard limit (e.g. common texture limits)

			if (img.Width > AbsoluteMaxWidth) {
				// This is a hard clamp, aspect ratio will be maintained by setting height to 0.
				// Consider logging this event if a logger was available.
				img.Mutate(x => x.Resize(new ResizeOptions {
					Size = new Size(AbsoluteMaxWidth, 0),
					Mode = ResizeMode.Max, // Ensures width is clamped, height adjusts by aspect ratio
					Sampler = KnownResamplers.Lanczos3
				}));
			}

			// Main resize logic for UI display consistency
			if (img.Width > MaxDisplayableCompositeWidth) {
				// Consider logging this event if a logger was available.
				img.Mutate(x => x.Resize(new ResizeOptions {
					Size = new Size(MaxDisplayableCompositeWidth, 0), // Setting height to 0 maintains aspect ratio
					Mode = ResizeMode.Max,
					Sampler = KnownResamplers.Lanczos3
				}));
			}

			try {
				// Convert to Bgra32 for direct compatibility with Avalonia's PixelFormat.Bgra8888
				using var bgraImage = img.CloneAs<Bgra32>();

				if (!bgraImage.DangerousTryGetSinglePixelMemory(out var pixelMemory)) {
					// VDF.Core.Utils.Logger.Instance.Error("JoinImages: Unable to get pixel memory from bgraImage.");
					return null;
				}
				Span<byte> sourcePixelData = MemoryMarshal.AsBytes(pixelMemory.Span);

				var writeableBitmap = new WriteableBitmap(
					new Avalonia.PixelSize(bgraImage.Width, bgraImage.Height),
					new Avalonia.Vector(96, 96), // Standard DPI
					PixelFormat.Bgra8888,
					AlphaFormat.Unpremul // ImageSharp Bgra32 is typically unpremultiplied
				);

				using (var lockedFramebuffer = writeableBitmap.Lock()) {
					Span<byte> destinationSpan = new Span<byte>((void*)lockedFramebuffer.Address, lockedFramebuffer.Size.Height * lockedFramebuffer.RowBytes);

					// Check if the source data will fit into the destination.
					// This is crucial if sourceStride != destinationStride or if there's padding.
					// For Bgra32 (4 bytes/pixel) and Bgra8888 (4 bytes/pixel), if widths are same, strides should match.
					int expectedSourceLength = bgraImage.Width * bgraImage.Height * 4;
					if (sourcePixelData.Length == expectedSourceLength && destinationSpan.Length == expectedSourceLength) {
						sourcePixelData.CopyTo(destinationSpan);
					} else {
						// Fallback or error if direct copy is not possible due to size mismatch
						// This might indicate stride differences or padding.
						// A row-by-row copy would be needed here if strides differ but data per row is same.
						// For now, log if this less common path is hit and return null if buffers don't match.
						// VDF.Core.Utils.Logger.Instance.Error($"JoinImages: Buffer size mismatch. Source: {sourcePixelData.Length}, Expected Source: {expectedSourceLength}, Dest: {destinationSpan.Length}");
                        // Attempt row-by-row if total data is same but strides might differ (though less likely for Bgra32)
                        if (sourcePixelData.Length == destinationSpan.Length) {
                             int sourceStride = bgraImage.Width * 4; // bytes per row in source
                             int destStride = lockedFramebuffer.RowBytes;
                             for(int y=0; y < bgraImage.Height; y++) {
                                 Span<byte> sourceRow = sourcePixelData.Slice(y * sourceStride, sourceStride);
                                 Span<byte> destRow = destinationSpan.Slice(y * destStride, sourceStride); // Assuming dest can take sourceStride bytes
                                 sourceRow.CopyTo(destRow);
                             }
                        } else {
						    return null; // Or throw an exception
                        }
					}
				}
				return writeableBitmap;
			} catch (Exception ex) {
				// VDF.Core.Utils.Logger.Instance.Error($"Error in JoinImages during pixel data conversion: {ex.Message}");
				return null; // Fallback or error handling
			}
		}

		public static byte[] ToByteArray(this Bitmap image) {
			using MemoryStream ms = new();
			image.Save(ms);
			return ms.ToArray();
		}
		public static byte[] ToByteArray(this Image image) {
			using MemoryStream ms = new();
			image.Save(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
			return ms.ToArray();
		}
	}
}
