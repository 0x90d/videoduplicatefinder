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

using System.Buffers.Binary;

namespace VDF.Core.Utils {
	/// <summary>
	/// Reads pixel dimensions straight from an image file's header, without decoding it.
	/// Used as a fast, dependency-free fallback for still images when the FFmpeg/FFprobe
	/// path can't supply dimensions — notably PNGs that trip FFprobe's demuxer with a
	/// bogus "chunk too big" error (issue #805). Covers the common still formats; returns
	/// false for anything it doesn't recognise so the caller can fall back to FFprobe.
	/// </summary>
	internal static class ImageHeader {
		internal static bool TryGetDimensions(string path, out int width, out int height) {
			width = 0;
			height = 0;
			try {
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				Span<byte> head = stackalloc byte[32];
				int read = fs.Read(head);
				if (read < 8)
					return false;

				// PNG: 8-byte signature, then IHDR (width @16 BE, height @20 BE).
				if (read >= 24 &&
					head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
					head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A) {
					width = BinaryPrimitives.ReadInt32BigEndian(head.Slice(16, 4));
					height = BinaryPrimitives.ReadInt32BigEndian(head.Slice(20, 4));
					return width > 0 && height > 0;
				}

				// GIF: "GIF87a"/"GIF89a", logical screen width @6 LE, height @8 LE.
				if (read >= 10 && head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F') {
					width = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(6, 2));
					height = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(8, 2));
					return width > 0 && height > 0;
				}

				// BMP: "BM", BITMAPINFOHEADER width @18 LE, height @22 LE (height may be negative for top-down).
				if (read >= 26 && head[0] == (byte)'B' && head[1] == (byte)'M') {
					width = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(18, 4));
					height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head.Slice(22, 4)));
					return width > 0 && height > 0;
				}

				// JPEG: scan segment markers for an SOF block carrying the dimensions.
				if (head[0] == 0xFF && head[1] == 0xD8)
					return TryGetJpegDimensions(fs, out width, out height);

				return false;
			}
			catch {
				return false;
			}
		}

		static bool TryGetJpegDimensions(FileStream fs, out int width, out int height) {
			width = 0;
			height = 0;
			fs.Position = 2; // past SOI (FF D8)
			Span<byte> b = stackalloc byte[8];
			while (true) {
				// Find the next marker (0xFF, then a non-0xFF, non-0x00 marker byte).
				int prev = fs.ReadByte();
				if (prev < 0)
					return false;
				if (prev != 0xFF)
					continue;
				int marker;
				do {
					marker = fs.ReadByte();
					if (marker < 0)
						return false;
				} while (marker == 0xFF);
				if (marker == 0x00 || marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7))
					continue; // padding / standalone markers carry no length
				if (marker == 0xD9)
					return false; // EOI

				if (fs.Read(b.Slice(0, 2)) < 2)
					return false;
				int segLen = (b[0] << 8) | b[1];
				if (segLen < 2)
					return false;

				// SOF0..SOF15 (0xC0-0xCF) except DHT(C4), JPG(C8), DAC(CC) carry frame dimensions.
				bool isSof = marker >= 0xC0 && marker <= 0xCF &&
					marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
				if (isSof) {
					// SOF payload: precision(1), height(2 BE), width(2 BE).
					if (fs.Read(b.Slice(0, 5)) < 5)
						return false;
					height = (b[1] << 8) | b[2];
					width = (b[3] << 8) | b[4];
					return width > 0 && height > 0;
				}

				fs.Position += segLen - 2; // skip this segment's payload
			}
		}
	}
}
