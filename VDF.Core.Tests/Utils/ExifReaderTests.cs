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

using System.Text;
using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class ExifReaderTests : IDisposable {
	readonly List<string> _tempFiles = new();

	public void Dispose() {
		foreach (var f in _tempFiles)
			try { File.Delete(f); } catch { }
	}

	string WriteTemp(byte[] content, string extension) {
		string path = Path.Combine(Path.GetTempPath(), $"vdf-exif-test-{Guid.NewGuid():N}{extension}");
		File.WriteAllBytes(path, content);
		_tempFiles.Add(path);
		return path;
	}

	/// <summary>
	/// Builds a little-endian TIFF blob with IFD0 (DateTime + optional Exif-IFD pointer)
	/// and an optional Exif sub-IFD carrying DateTimeOriginal.
	/// </summary>
	static byte[] BuildTiffBlob(string? ifd0DateTime, string? dateTimeOriginal) {
		var ms = new MemoryStream();
		var w = new BinaryWriter(ms);
		w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42);
		w.Write(8u); // IFD0 offset

		var stringData = new List<(long offsetField, string value)>();

		int ifd0Entries = (ifd0DateTime != null ? 1 : 0) + (dateTimeOriginal != null ? 1 : 0);
		w.Write((ushort)ifd0Entries);
		if (ifd0DateTime != null) {
			w.Write((ushort)0x0132); w.Write((ushort)2); w.Write(20u); // ASCII, 20 bytes
			stringData.Add((ms.Position, ifd0DateTime));
			w.Write(0u); // patched later
		}
		long exifPointerValuePos = -1;
		if (dateTimeOriginal != null) {
			w.Write((ushort)0x8769); w.Write((ushort)4); w.Write(1u); // LONG, 1
			exifPointerValuePos = ms.Position;
			w.Write(0u); // patched later
		}
		w.Write(0u); // next-IFD offset

		if (dateTimeOriginal != null) {
			uint exifIfdOffset = (uint)ms.Position;
			long patchPos = ms.Position;
			ms.Position = exifPointerValuePos; w.Write(exifIfdOffset); ms.Position = patchPos;

			w.Write((ushort)1);
			w.Write((ushort)0x9003); w.Write((ushort)2); w.Write(20u);
			stringData.Add((ms.Position, dateTimeOriginal));
			w.Write(0u);
			w.Write(0u); // next-IFD offset
		}

		foreach (var (offsetField, value) in stringData) {
			uint dataOffset = (uint)ms.Position;
			w.Write(Encoding.ASCII.GetBytes(value));
			w.Write((byte)0);
			long cur = ms.Position;
			ms.Position = offsetField; w.Write(dataOffset); ms.Position = cur;
		}
		return ms.ToArray();
	}

	static byte[] BuildJpegWithExif(byte[] tiffBlob) {
		var ms = new MemoryStream();
		var w = new BinaryWriter(ms);
		w.Write((byte)0xFF); w.Write((byte)0xD8); // SOI
		byte[] exifHeader = { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };
		int segLen = 2 + exifHeader.Length + tiffBlob.Length;
		w.Write((byte)0xFF); w.Write((byte)0xE1);
		w.Write((byte)(segLen >> 8)); w.Write((byte)(segLen & 0xFF));
		w.Write(exifHeader);
		w.Write(tiffBlob);
		w.Write((byte)0xFF); w.Write((byte)0xD9); // EOI
		return ms.ToArray();
	}

	[Fact]
	public void Jpeg_DateTimeOriginal_IsPreferred() {
		var tiff = BuildTiffBlob("2020:01:02 03:04:05", "2019:06:07 08:09:10");
		string path = WriteTemp(BuildJpegWithExif(tiff), ".jpg");

		Assert.True(ExifReader.TryGetDateTaken(path, out DateTime date));
		Assert.Equal(new DateTime(2019, 6, 7, 8, 9, 10, DateTimeKind.Utc), date);
	}

	[Fact]
	public void Jpeg_FallsBackToIfd0DateTime() {
		var tiff = BuildTiffBlob("2020:01:02 03:04:05", null);
		string path = WriteTemp(BuildJpegWithExif(tiff), ".jpg");

		Assert.True(ExifReader.TryGetDateTaken(path, out DateTime date));
		Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), date);
	}

	[Fact]
	public void RawTiff_DateIsRead() {
		string path = WriteTemp(BuildTiffBlob(null, "2021:12:31 23:59:58"), ".tif");

		Assert.True(ExifReader.TryGetDateTaken(path, out DateTime date));
		Assert.Equal(new DateTime(2021, 12, 31, 23, 59, 58, DateTimeKind.Utc), date);
	}

	[Fact]
	public void Png_ExifChunk_IsRead() {
		var tiff = BuildTiffBlob(null, "2022:05:06 07:08:09");
		var ms = new MemoryStream();
		var w = new BinaryWriter(ms);
		w.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
		// eXIf chunk: big-endian length + type + data + dummy CRC
		w.Write((byte)(tiff.Length >> 24)); w.Write((byte)(tiff.Length >> 16));
		w.Write((byte)(tiff.Length >> 8)); w.Write((byte)tiff.Length);
		w.Write(Encoding.ASCII.GetBytes("eXIf"));
		w.Write(tiff);
		w.Write(0u); // CRC (unchecked)
		string path = WriteTemp(ms.ToArray(), ".png");

		Assert.True(ExifReader.TryGetDateTaken(path, out DateTime date));
		Assert.Equal(new DateTime(2022, 5, 6, 7, 8, 9, DateTimeKind.Utc), date);
	}

	[Fact]
	public void Jpeg_WithoutExif_ReturnsFalse() {
		var ms = new MemoryStream();
		var w = new BinaryWriter(ms);
		w.Write((byte)0xFF); w.Write((byte)0xD8);
		w.Write((byte)0xFF); w.Write((byte)0xDA); w.Write((byte)0); w.Write((byte)4); // SOS
		w.Write((ushort)0);
		string path = WriteTemp(ms.ToArray(), ".jpg");

		Assert.False(ExifReader.TryGetDateTaken(path, out _));
	}

	[Theory]
	[InlineData(new byte[0])]
	[InlineData(new byte[] { 0xFF, 0xD8 })]
	[InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 })] // TIFF header only, no IFD
	[InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })]
	public void TruncatedOrGarbageFiles_ReturnFalseWithoutThrowing(byte[] content) {
		string path = WriteTemp(content, ".jpg");
		Assert.False(ExifReader.TryGetDateTaken(path, out _));
	}

	[Fact]
	public void UnparsableDateValue_ReturnsFalse() {
		var tiff = BuildTiffBlob("not a real date....", null);
		string path = WriteTemp(BuildJpegWithExif(tiff), ".jpg");

		Assert.False(ExifReader.TryGetDateTaken(path, out _));
	}
}
