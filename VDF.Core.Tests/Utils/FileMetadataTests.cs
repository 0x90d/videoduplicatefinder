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
// #926: compare every metadata tag of a group's files and point at the one that differs.

using System.Buffers.Binary;
using System.Text;
using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class FileMetadataTests {

	// ===== TIFF/EXIF blobs =====

	sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Data);

	static Entry Ascii(ushort tag, string s) { var b = Encoding.ASCII.GetBytes(s + "\0"); return new(tag, 2, (uint)b.Length, b); }
	static Entry Undefined(ushort tag, byte[] b) => new(tag, 7, (uint)b.Length, b);

	/// <summary>
	/// A TIFF blob with IFD0, an Exif sub-IFD and a GPS IFD (pointers added automatically),
	/// every value stored the way the byte order says; values over 4 bytes go to a data area.
	/// </summary>
	static byte[] Tiff(bool littleEndian, Entry[] ifd0, Entry[] exif, Entry[] gps) {
		var buf = new byte[4096];
		void U16(int o, int v) { if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(o), (ushort)v); else BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(o), (ushort)v); }
		void U32(int o, uint v) { if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o), v); else BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(o), v); }
		buf[0] = buf[1] = (byte)(littleEndian ? 'I' : 'M');
		U16(2, 42);
		U32(4, 8);

		int IfdSize(int n) => 2 + n * 12 + 4;
		var all0 = ifd0.ToList();
		if (exif.Length > 0) all0.Add(new(0x8769, 4, 1, new byte[4]));
		if (gps.Length > 0) all0.Add(new(0x8825, 4, 1, new byte[4]));
		int exifAt = 8 + IfdSize(all0.Count);
		int gpsAt = exifAt + (exif.Length > 0 ? IfdSize(exif.Length) : 0);
		int dataAt = gpsAt + (gps.Length > 0 ? IfdSize(gps.Length) : 0);

		void WriteIfd(int at, List<Entry> entries) {
			U16(at, entries.Count);
			for (int i = 0; i < entries.Count; i++) {
				var e = entries[i];
				int o = at + 2 + i * 12;
				U16(o, e.Tag); U16(o + 2, e.Type); U32(o + 4, e.Count);
				byte[] data = e.Tag == 0x8769 ? Ptr(exifAt) : e.Tag == 0x8825 ? Ptr(gpsAt) : e.Data;
				if (data.Length <= 4) data.CopyTo(buf, o + 8);
				else { U32(o + 8, (uint)dataAt); data.CopyTo(buf, dataAt); dataAt += data.Length + (data.Length & 1); }
			}
			U32(at + 2 + entries.Count * 12, 0);
		}
		byte[] Ptr(int v) { var b = new byte[4]; if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)v); else BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); return b; }

		WriteIfd(8, all0);
		if (exif.Length > 0) WriteIfd(exifAt, exif.ToList());
		if (gps.Length > 0) WriteIfd(gpsAt, gps.ToList());
		return buf[..dataAt];
	}

	static byte[] Rationals(bool le, params (uint Num, uint Den)[] values) {
		var b = new byte[values.Length * 8];
		for (int i = 0; i < values.Length; i++) {
			if (le) { BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i * 8), values[i].Num); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i * 8 + 4), values[i].Den); }
			else { BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(i * 8), values[i].Num); BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(i * 8 + 4), values[i].Den); }
		}
		return b;
	}

	static byte[] Short(bool le, ushort v) {
		var b = new byte[2];
		if (le) BinaryPrimitives.WriteUInt16LittleEndian(b, v); else BinaryPrimitives.WriteUInt16BigEndian(b, v);
		return b;
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Exif_EveryReadableTag_InBothByteOrders(bool le) {
		var tiff = Tiff(le,
			ifd0: new[] {
				Ascii(0x010F, "Apple"), Ascii(0x0110, "iPhone 12"), new(0x0112, 3, 1, Short(le, 6)),
				Ascii(0x0132, "2023:08:15 14:34:56"),
				new(0x927C, 7, 3, new byte[] { 1, 2, 3 }), // maker note: left out
				Ascii(0xC000, "vendor text"),               // unknown but text: kept by number
				new(0xC001, 3, 1, Short(le, 7)),             // unknown number: left out
			},
			exif: new[] {
				Ascii(0x9003, "2023:08:15 14:34:56"), Ascii(0x9011, "+02:00"),
				new(0x829A, 5, 1, Rationals(le, (1, 250))), new(0x829D, 5, 1, Rationals(le, (18, 10))),
				Undefined(0x9000, Encoding.ASCII.GetBytes("0232")),
				Undefined(0x9286, Encoding.ASCII.GetBytes("ASCII\0\0\0holiday")),
			},
			gps: new[] {
				new Entry(0x00, 1, 4, new byte[] { 2, 3, 0, 0 }), Ascii(0x01, "N"),
				new(0x02, 5, 3, Rationals(le, (48, 1), (8, 1), (131234, 10000))),
			});

		var tags = ExifReader.ReadAllTags(tiff).ToDictionary(t => (t.IsGps, t.Name), t => t.Value);

		Assert.Equal("Apple", tags[(false, "Make")]);
		Assert.Equal("iPhone 12", tags[(false, "Model")]);
		Assert.Equal("6", tags[(false, "Orientation")]);
		Assert.Equal("2023:08:15 14:34:56", tags[(false, "DateTimeOriginal")]);
		Assert.Equal("+02:00", tags[(false, "OffsetTimeOriginal")]);
		Assert.Equal("1/250", tags[(false, "ExposureTime")]);
		Assert.Equal("1.8", tags[(false, "FNumber")]);
		Assert.Equal("0232", tags[(false, "ExifVersion")]);
		Assert.Equal("holiday", tags[(false, "UserComment")]);
		Assert.Equal("vendor text", tags[(false, "Tag 0xC000")]);
		Assert.Equal("2.3.0.0", tags[(true, "GPSVersionID")]);
		Assert.Equal("N", tags[(true, "GPSLatitudeRef")]);
		Assert.Equal("48, 8, 13.1234", tags[(true, "GPSLatitude")]);
		Assert.DoesNotContain(tags.Keys, k => k.Name is "Tag 0x927C" or "Tag 0xC001" or "Tag 0x8769" or "Tag 0x8825");
	}

	[Fact]
	public void Exif_GarbageAndTruncatedBlobs_YieldNothingOrWhatIsIntact() {
		Assert.Empty(ExifReader.ReadAllTags(new byte[] { 1, 2, 3 }));
		Assert.Empty(ExifReader.ReadAllTags(Encoding.ASCII.GetBytes("II*\0garbage-garbage")));
		var tiff = Tiff(true, new[] { Ascii(0x010F, "Canon"), Ascii(0x0110, "a model name longer than four bytes") }, Array.Empty<Entry>(), Array.Empty<Entry>());
		var cut = tiff[..(tiff.Length - 10)]; // the model's text runs past the end
		var tags = ExifReader.ReadAllTags(cut);
		Assert.Equal(("Make", "Canon"), (tags.Single().Name, tags.Single().Value));
	}

	[Fact]
	public void Exif_ReadFromAJpegFile() {
		var tiff = Tiff(true, new[] { Ascii(0x010F, "Canon") }, new[] { Ascii(0x9003, "2021:04:28 10:00:00") }, Array.Empty<Entry>());
		var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1 };
		int segLen = 2 + 6 + tiff.Length;
		jpeg.Add((byte)(segLen >> 8)); jpeg.Add((byte)segLen);
		jpeg.AddRange("Exif\0\0"u8.ToArray());
		jpeg.AddRange(tiff);
		jpeg.AddRange(new byte[] { 0xFF, 0xD9 });
		string path = Path.Combine(Path.GetTempPath(), $"vdf-meta-{Guid.NewGuid():N}.jpg");
		File.WriteAllBytes(path, jpeg.ToArray());
		try {
			var fields = FileMetadata.Read(path)!;
			Assert.Contains(fields, f => f.Section.Kind == MetadataSectionKind.Exif && f.Name == "Make" && f.Value == "Canon");
			Assert.Contains(fields, f => f.Section.Kind == MetadataSectionKind.Exif && f.Name == "DateTimeOriginal" && f.Value == "2021:04:28 10:00:00");
		}
		finally { File.Delete(path); }
	}

	[Fact]
	public void MissingFile_ReadsAsNull() =>
		Assert.Null(FileMetadata.Read(Path.Combine(Path.GetTempPath(), $"vdf-missing-{Guid.NewGuid():N}.mp4")));

	// ===== ffprobe tags =====

	// An iPhone mov as ffprobe prints it; the Apple creation date is the one with a time zone.
	const string ProbeJson = """
		{
		  "programs": [],
		  "streams": [
		    { "index": 0, "codec_type": "video",
		      "tags": { "creation_time": "2023-08-15T12:34:56.000000Z", "language": "und", "handler_name": "Core Media Video" } },
		    { "index": 1, "codec_type": "audio",
		      "tags": { "creation_time": "2023-08-15T12:34:56.000000Z", "language": "und" } },
		    { "index": 2, "codec_type": "data" }
		  ],
		  "format": {
		    "tags": {
		      "major_brand": "qt  ",
		      "creation_time": "2023-08-15T12:34:56.000000Z",
		      "com.apple.quicktime.make": "Apple",
		      "com.apple.quicktime.creationdate": "2023-08-15T14:34:56+0200"
		    }
		  }
		}
		""";

	[Fact]
	public void ProbeTags_KeepSectionAndOrder() {
		var fields = FileMetadata.ParseProbeTags(Encoding.UTF8.GetBytes(ProbeJson));

		var container = fields.Where(f => f.Section.Kind == MetadataSectionKind.Container).Select(f => f.Name).ToArray();
		Assert.Equal(new[] { "major_brand", "creation_time", "com.apple.quicktime.make", "com.apple.quicktime.creationdate" }, container);
		Assert.Equal("2023-08-15T14:34:56+0200", fields.Single(f => f.Name == "com.apple.quicktime.creationdate").Value);
		var audio = fields.Where(f => f.Section == new MetadataSection(MetadataSectionKind.Stream, 1, "audio")).Select(f => f.Name);
		Assert.Equal(new[] { "creation_time", "language" }, audio);
		Assert.DoesNotContain(fields, f => f.Section.StreamIndex == 2);
	}

	[Fact]
	public void ProbeTags_BrokenJson_YieldsNothing() =>
		Assert.Empty(FileMetadata.ParseProbeTags(Encoding.UTF8.GetBytes("{ \"format\": { \"tags\": ")));

	// ===== comparison =====

	static MetadataField C(string name, string value) => new(new(MetadataSectionKind.Container), name, value);
	static MetadataField S(int index, string type, string name, string value) => new(new(MetadataSectionKind.Stream, index, type), name, value);
	static MetadataField E(string name, string value) => new(new(MetadataSectionKind.Exif), name, value);

	// The reporter's case: identical videos, one carries the right date, the others don't.
	[Fact]
	public void Comparison_MarksTheOneFileThatDiffers() {
		var copy = new List<MetadataField> { C("creation_time", "2023-08-15T12:34:56Z"), C("com.apple.quicktime.creationdate", "2023-08-15T12:34:56") };
		var right = new List<MetadataField> { C("creation_time", "2023-08-15T12:34:56Z"), C("com.apple.quicktime.creationdate", "2023-08-15T14:34:56+0200") };

		var rows = MetadataComparison.Build(new[] { copy, right, copy });

		var same = rows.Single(r => r.Name == "creation_time");
		Assert.False(same.Differs);
		Assert.Equal(new[] { false, false, false }, same.IsOdd);
		var date = rows.Single(r => r.Name == "com.apple.quicktime.creationdate");
		Assert.True(date.Differs);
		Assert.Equal(new[] { false, true, false }, date.IsOdd);
	}

	[Fact]
	public void Comparison_MissingCountsAsAValue_TwoWayTieMarksBoth() {
		var a = new List<MetadataField> { C("title", "Holiday"), C("encoder", "x") };
		var b = new List<MetadataField> { C("encoder", "y") };

		var rows = MetadataComparison.Build(new[] { a, b });

		var title = rows.Single(r => r.Name == "title");
		Assert.Equal(new string?[] { "Holiday", null }, title.Values);
		Assert.True(title.Differs);
		Assert.Equal(new[] { true, true }, title.IsOdd); // one against one: no majority to point from
		Assert.Equal(new[] { true, true }, rows.Single(r => r.Name == "encoder").IsOdd);
	}

	[Fact]
	public void Comparison_MissingFieldInTheMinority_IsTheOddOne() {
		var tagged = new List<MetadataField> { C("location", "+48.1+011.5/") };
		var rows = MetadataComparison.Build(new[] { tagged, tagged, new List<MetadataField>() });
		Assert.Equal(new[] { false, false, true }, rows.Single().IsOdd);
	}

	[Fact]
	public void Comparison_UnreadableFile_TakesNoPartInTheVote() {
		var a = new List<MetadataField> { C("creation_time", "1") };
		var rows = MetadataComparison.Build(new IReadOnlyList<MetadataField>?[] { a, null, a });
		var row = rows.Single();
		Assert.False(row.Differs);
		Assert.Equal(new[] { false, false, false }, row.IsOdd);
		Assert.Null(row.Values[1]);
	}

	[Fact]
	public void Comparison_OrdersContainerStreamsByIndexThenExif() {
		var a = new List<MetadataField> { E("Make", "Canon"), S(1, "audio", "language", "ger"), C("title", "t"), S(0, "video", "handler_name", "v") };
		var b = new List<MetadataField> { S(0, "video", "language", "und") };

		var names = MetadataComparison.Build(new[] { a, b }).Select(r => r.Name).ToArray();

		Assert.Equal(new[] { "title", "handler_name", "language", "language", "Make" }, names);
	}
}
