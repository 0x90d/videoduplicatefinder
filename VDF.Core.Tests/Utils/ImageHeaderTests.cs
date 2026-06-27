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
using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class ImageHeaderTests {
	static string WriteTemp(byte[] bytes) {
		string path = Path.GetTempFileName();
		File.WriteAllBytes(path, bytes);
		return path;
	}

	static void Roundtrip(byte[] header, int expectedW, int expectedH) {
		string path = WriteTemp(header);
		try {
			Assert.True(ImageHeader.TryGetDimensions(path, out int w, out int h));
			Assert.Equal(expectedW, w);
			Assert.Equal(expectedH, h);
		}
		finally {
			File.Delete(path);
		}
	}

	[Fact]
	public void Png_ReadsDimensions() {
		byte[] b = new byte[24];
		new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
		BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16, 4), 1920);
		BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20, 4), 1080);
		Roundtrip(b, 1920, 1080);
	}

	[Fact]
	public void Gif_ReadsDimensions() {
		byte[] b = new byte[10];
		"GIF89a"u8.CopyTo(b);
		BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6, 2), 640);
		BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8, 2), 480);
		Roundtrip(b, 640, 480);
	}

	[Fact]
	public void Bmp_ReadsDimensions_TopDownNegativeHeight() {
		byte[] b = new byte[26];
		b[0] = (byte)'B';
		b[1] = (byte)'M';
		BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18, 4), 320);
		BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22, 4), -240); // top-down DIB
		Roundtrip(b, 320, 240);
	}

	[Fact]
	public void Jpeg_ReadsDimensions_SkippingAppSegments() {
		using var ms = new MemoryStream();
		ms.Write(new byte[] { 0xFF, 0xD8 }); // SOI
		// APP0 segment (length 4, two payload bytes) to ensure segment skipping works.
		ms.Write(new byte[] { 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00 });
		// SOF0: length 0x0011, precision 8, height 0x0200 (512), width 0x0400 (1024).
		ms.Write(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x02, 0x00, 0x04, 0x00 });
		Roundtrip(ms.ToArray(), 1024, 512);
	}

	[Fact]
	public void UnknownFormat_ReturnsFalse() {
		string path = WriteTemp(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });
		try {
			Assert.False(ImageHeader.TryGetDimensions(path, out _, out _));
		}
		finally {
			File.Delete(path);
		}
	}
}
