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
using System.Globalization;
using System.Text;

namespace VDF.Core.Utils {
	/// <summary>
	/// Minimal EXIF reader: extracts the date a photo was taken
	/// (DateTimeOriginal, falling back to DateTime) from JPEG, TIFF, PNG and
	/// WebP files. Replaces ImageSharp's ExifProfile for the one tag VDF needs.
	/// Pure managed parsing — no decoding, reads only the metadata segments.
	/// </summary>
	internal static class ExifReader {
		const ushort TagDateTime = 0x0132;          // IFD0 "DateTime" (modification date)
		const ushort TagExifIfdPointer = 0x8769;    // IFD0 pointer to the Exif sub-IFD
		const ushort TagDateTimeOriginal = 0x9003;  // Exif sub-IFD "DateTimeOriginal"
		const int MaxTiffBlob = 1 << 20;            // sanity cap for in-memory TIFF blobs

		/// <summary>
		/// Tries to read the EXIF capture date of <paramref name="path"/>.
		/// Returns false when the file has no parsable EXIF date.
		/// </summary>
		internal static bool TryGetDateTaken(string path, out DateTime dateTaken) {
			dateTaken = default;
			try {
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
				byte[]? tiff = ExtractTiffBlob(fs);
				if (tiff == null)
					return false;
				string? raw = ParseTiffForDate(tiff);
				return raw != null && TryParseExifDateTime(raw, out dateTaken);
			}
			catch {
				return false;
			}
		}

		/// <summary>
		/// Every readable EXIF and GPS tag of <paramref name="path"/> as (isGps, name, value),
		/// in file order, for the metadata comparison (#926). Pointers, the maker note and
		/// embedded blobs are left out; unknown tags only when they hold text. Empty when the
		/// file has no EXIF.
		/// </summary>
		internal static List<(bool IsGps, string Name, string Value)> ReadAllTags(string path) {
			try {
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
				byte[]? tiff = ExtractTiffBlob(fs);
				return tiff == null ? new() : ReadAllTags(tiff);
			}
			catch {
				return new();
			}
		}

		internal static List<(bool IsGps, string Name, string Value)> ReadAllTags(byte[] tiff) {
			var tags = new List<(bool, string, string)>();
			if (tiff.Length < 8) return tags;
			bool le = tiff[0] == 0x49 && tiff[1] == 0x49;
			if (!le && !(tiff[0] == 0x4D && tiff[1] == 0x4D)) return tags;
			if (ReadU16(tiff, 2, le) != 42) return tags;

			uint exifIfd = 0, gpsIfd = 0;
			void Collect(uint offset, bool gps, Dictionary<ushort, string> names) {
				foreach (var (tag, type, count, valueOffset) in EnumerateIfd(tiff, offset, le)) {
					if (!gps && tag == TagExifIfdPointer) { exifIfd = ReadPointer(tiff, type, valueOffset, le); continue; }
					if (!gps && tag == TagGpsIfdPointer) { gpsIfd = ReadPointer(tiff, type, valueOffset, le); continue; }
					if (SkippedTags.Contains(tag)) continue;
					bool known = names.TryGetValue(tag, out string? name);
					if (!known && type != 2) continue;
					string? value = FormatValue(tiff, tag, type, count, valueOffset, le);
					if (!string.IsNullOrEmpty(value))
						tags.Add((gps, name ?? $"Tag 0x{tag:X4}", value));
				}
			}
			Collect(ReadU32(tiff, 4, le), false, TagNames);
			if (exifIfd > 0) Collect(exifIfd, false, TagNames);
			if (gpsIfd > 0) Collect(gpsIfd, true, GpsTagNames);
			return tags;
		}

		static uint ReadPointer(byte[] tiff, ushort type, int valueOffset, bool le) =>
			type == 4 ? ReadU32(tiff, valueOffset, le) : type == 3 ? ReadU16(tiff, valueOffset, le) : 0u;

		const ushort TagGpsIfdPointer = 0x8825;

		// Interop pointer, maker note (vendor binary), thumbnail location, XMP, ICC, PrintIM.
		static readonly HashSet<ushort> SkippedTags = new() { 0xA005, 0x927C, 0x0201, 0x0202, 0x02BC, 0x8773, 0xC4A5 };

		static readonly Dictionary<ushort, string> TagNames = new() {
			[0x0100] = "ImageWidth", [0x0101] = "ImageLength", [0x010E] = "ImageDescription", [0x010F] = "Make",
			[0x0110] = "Model", [0x0112] = "Orientation", [0x011A] = "XResolution", [0x011B] = "YResolution",
			[0x0128] = "ResolutionUnit", [0x0131] = "Software", [0x0132] = "DateTime", [0x013B] = "Artist",
			[0x0213] = "YCbCrPositioning", [0x8298] = "Copyright",
			[0x829A] = "ExposureTime", [0x829D] = "FNumber", [0x8822] = "ExposureProgram", [0x8827] = "ISOSpeedRatings",
			[0x9000] = "ExifVersion", [0x9003] = "DateTimeOriginal", [0x9004] = "DateTimeDigitized",
			[0x9010] = "OffsetTime", [0x9011] = "OffsetTimeOriginal", [0x9012] = "OffsetTimeDigitized",
			[0x9101] = "ComponentsConfiguration", [0x9201] = "ShutterSpeedValue", [0x9202] = "ApertureValue",
			[0x9203] = "BrightnessValue", [0x9204] = "ExposureBiasValue", [0x9205] = "MaxApertureValue",
			[0x9206] = "SubjectDistance", [0x9207] = "MeteringMode", [0x9208] = "LightSource", [0x9209] = "Flash",
			[0x920A] = "FocalLength", [0x9286] = "UserComment", [0x9290] = "SubSecTime", [0x9291] = "SubSecTimeOriginal",
			[0x9292] = "SubSecTimeDigitized", [0xA000] = "FlashpixVersion", [0xA001] = "ColorSpace",
			[0xA002] = "PixelXDimension", [0xA003] = "PixelYDimension", [0xA217] = "SensingMethod",
			[0xA401] = "CustomRendered", [0xA402] = "ExposureMode", [0xA403] = "WhiteBalance", [0xA404] = "DigitalZoomRatio",
			[0xA405] = "FocalLengthIn35mmFilm", [0xA406] = "SceneCaptureType", [0xA420] = "ImageUniqueID",
			[0xA430] = "CameraOwnerName", [0xA431] = "BodySerialNumber", [0xA432] = "LensSpecification",
			[0xA433] = "LensMake", [0xA434] = "LensModel",
		};

		static readonly Dictionary<ushort, string> GpsTagNames = new() {
			[0x00] = "GPSVersionID", [0x01] = "GPSLatitudeRef", [0x02] = "GPSLatitude", [0x03] = "GPSLongitudeRef",
			[0x04] = "GPSLongitude", [0x05] = "GPSAltitudeRef", [0x06] = "GPSAltitude", [0x07] = "GPSTimeStamp",
			[0x0C] = "GPSSpeedRef", [0x0D] = "GPSSpeed", [0x10] = "GPSImgDirectionRef", [0x11] = "GPSImgDirection",
			[0x12] = "GPSMapDatum", [0x1B] = "GPSProcessingMethod", [0x1D] = "GPSDateStamp",
		};

		/// <summary>Human-readable value of one IFD entry, or null when it can't be shown sensibly.</summary>
		static string? FormatValue(byte[] tiff, ushort tag, ushort type, uint count, int valueFieldOffset, bool le) {
			int size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 => 4, 5 or 10 => 8, _ => 0 };
			if (size == 0 || count == 0 || count > 4096) return null;
			long total = (long)size * count;
			int data = total <= 4 ? valueFieldOffset : (int)ReadU32(tiff, valueFieldOffset, le);
			if (data < 0 || data + total > tiff.Length) return null;
			var inv = CultureInfo.InvariantCulture;
			switch (type) {
			case 2:
				return Encoding.UTF8.GetString(tiff, data, (int)count).TrimEnd('\0', ' ');
			case 1:
			case 6:
			case 7:
				// UserComment: 8-byte charset prefix, then the text.
				if (tag == 0x9286 && count > 8) {
					string charset = Encoding.ASCII.GetString(tiff, data, 8).TrimEnd('\0', ' ');
					var text = charset == "UNICODE"
						? (le ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(tiff, data + 8, (int)count - 8)
						: Encoding.UTF8.GetString(tiff, data + 8, (int)count - 8);
					return text.TrimEnd('\0', ' ');
				}
				var bytes = tiff.AsSpan(data, (int)count);
				bool printable = true;
				foreach (byte b in bytes) printable &= b >= 0x20 && b < 0x7F;
				if (printable) return Encoding.ASCII.GetString(bytes); // ExifVersion "0232", ProcessingMethod
				if (count <= 8) return string.Join(".", bytes.ToArray()); // GPSVersionID 2.3.0.0
				return $"({count} bytes)";
			case 3:
			case 4:
			case 8:
			case 9:
				if (count > 16) return null;
				var numbers = new string[count];
				for (int i = 0; i < count; i++) {
					int o = data + i * size;
					numbers[i] = type switch {
						3 => ReadU16(tiff, o, le).ToString(inv),
						8 => ((short)ReadU16(tiff, o, le)).ToString(inv),
						4 => ReadU32(tiff, o, le).ToString(inv),
						_ => ((int)ReadU32(tiff, o, le)).ToString(inv),
					};
				}
				return string.Join(", ", numbers);
			default: // 5 RATIONAL, 10 SRATIONAL
				if (count > 16) return null;
				var parts = new string[count];
				for (int i = 0; i < count; i++) {
					int o = data + i * 8;
					long num = type == 5 ? ReadU32(tiff, o, le) : (int)ReadU32(tiff, o, le);
					long den = type == 5 ? ReadU32(tiff, o + 4, le) : (int)ReadU32(tiff, o + 4, le);
					parts[i] = den == 0 ? "0"
						: den == 1 ? num.ToString(inv)
						: num == 1 ? $"1/{den}"
						: ((double)num / den).ToString("0.####", inv);
				}
				return string.Join(", ", parts);
			}
		}

		/// <summary>EXIF date format is "yyyy:MM:dd HH:mm:ss" (local time, stored as UTC kind to match previous behavior).</summary>
		internal static bool TryParseExifDateTime(string exifDateTime, out DateTime result) {
			result = DateTime.MinValue;
			if (DateTime.TryParseExact(exifDateTime.Trim('\0', ' '), "yyyy:MM:dd HH:mm:ss",
				CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)) {
				result = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Locates the TIFF-formatted EXIF blob inside the container:
		/// JPEG APP1 segment, raw TIFF file, PNG eXIf chunk or WebP EXIF chunk.
		/// </summary>
		static byte[]? ExtractTiffBlob(FileStream fs) {
			Span<byte> sig = stackalloc byte[12];
			if (fs.Read(sig) < 12)
				return null;

			// Raw TIFF (.tif/.tiff): "II*\0" or "MM\0*"
			if ((sig[0] == 0x49 && sig[1] == 0x49 && sig[2] == 0x2A && sig[3] == 0x00) ||
				(sig[0] == 0x4D && sig[1] == 0x4D && sig[2] == 0x00 && sig[3] == 0x2A)) {
				fs.Position = 0;
				int len = (int)Math.Min(fs.Length, MaxTiffBlob);
				byte[] blob = new byte[len];
				fs.ReadExactly(blob, 0, len);
				return blob;
			}

			// JPEG: walk segments looking for APP1 "Exif\0\0"
			if (sig[0] == 0xFF && sig[1] == 0xD8) {
				fs.Position = 2;
				Span<byte> hdr = stackalloc byte[4];
				while (true) {
					if (fs.Read(hdr) < 4) return null;
					if (hdr[0] != 0xFF) return null;
					byte marker = hdr[1];
					if (marker == 0xDA || marker == 0xD9) return null; // start of scan / EOI — no EXIF
					int segLen = (hdr[2] << 8 | hdr[3]) - 2;
					if (segLen < 0) return null;
					if (marker == 0xE1 && segLen >= 6) {
						byte[] seg = new byte[segLen];
						fs.ReadExactly(seg, 0, segLen);
						if (seg[0] == 'E' && seg[1] == 'x' && seg[2] == 'i' && seg[3] == 'f' && seg[4] == 0 && seg[5] == 0)
							return seg[6..];
						continue; // some encoders emit XMP in an earlier APP1 — keep scanning
					}
					fs.Position += segLen;
				}
			}

			// PNG: chunks after the 8-byte signature; EXIF lives in "eXIf"
			if (sig[0] == 0x89 && sig[1] == 'P' && sig[2] == 'N' && sig[3] == 'G') {
				fs.Position = 8;
				Span<byte> chunkHdr = stackalloc byte[8];
				while (fs.Read(chunkHdr) == 8) {
					int len = BinaryPrimitives.ReadInt32BigEndian(chunkHdr);
					if (len < 0 || len > MaxTiffBlob) return null;
					string type = Encoding.ASCII.GetString(chunkHdr[4..8]);
					if (type == "eXIf") {
						byte[] blob = new byte[len];
						fs.ReadExactly(blob, 0, len);
						return blob;
					}
					if (type == "IDAT" || type == "IEND") return null; // metadata chunks precede image data
					fs.Position += len + 4; // skip data + CRC
				}
				return null;
			}

			// WebP: RIFF....WEBP, then fourcc chunks; EXIF chunk holds the TIFF blob
			if (sig[0] == 'R' && sig[1] == 'I' && sig[2] == 'F' && sig[3] == 'F' &&
				sig[8] == 'W' && sig[9] == 'E' && sig[10] == 'B' && sig[11] == 'P') {
				fs.Position = 12;
				Span<byte> chunkHdr = stackalloc byte[8];
				while (fs.Read(chunkHdr) == 8) {
					int len = BinaryPrimitives.ReadInt32LittleEndian(chunkHdr[4..8]);
					if (len < 0 || len > MaxTiffBlob) return null;
					if (chunkHdr[0] == 'E' && chunkHdr[1] == 'X' && chunkHdr[2] == 'I' && chunkHdr[3] == 'F') {
						byte[] blob = new byte[len];
						fs.ReadExactly(blob, 0, len);
						// Some writers prefix the chunk payload with "Exif\0\0"
						if (len > 6 && blob[0] == 'E' && blob[1] == 'x' && blob[2] == 'i' && blob[3] == 'f' && blob[4] == 0 && blob[5] == 0)
							return blob[6..];
						return blob;
					}
					fs.Position += len + (len & 1); // RIFF chunks are word-aligned
				}
				return null;
			}

			return null;
		}

		/// <summary>
		/// Walks IFD0 of <paramref name="tiff"/> for DateTime and the Exif sub-IFD,
		/// then the sub-IFD for DateTimeOriginal. Returns the raw ASCII value,
		/// preferring DateTimeOriginal (matching the previous ImageSharp behavior).
		/// </summary>
		static string? ParseTiffForDate(byte[] tiff) {
			if (tiff.Length < 8) return null;
			bool littleEndian = tiff[0] == 0x49 && tiff[1] == 0x49;
			if (!littleEndian && !(tiff[0] == 0x4D && tiff[1] == 0x4D)) return null;
			if (ReadU16(tiff, 2, littleEndian) != 42) return null;

			uint ifd0 = ReadU32(tiff, 4, littleEndian);
			string? dateTime = null;
			uint exifIfdOffset = 0;

			foreach (var (tag, type, count, valueOffset) in EnumerateIfd(tiff, ifd0, littleEndian)) {
				if (tag == TagDateTime)
					dateTime = ReadAscii(tiff, type, count, valueOffset, littleEndian);
				else if (tag == TagExifIfdPointer && (type == 4 || type == 3))
					exifIfdOffset = ReadU32(tiff, valueOffset, littleEndian);
			}

			if (exifIfdOffset > 0) {
				foreach (var (tag, type, count, valueOffset) in EnumerateIfd(tiff, exifIfdOffset, littleEndian)) {
					if (tag == TagDateTimeOriginal) {
						string? original = ReadAscii(tiff, type, count, valueOffset, littleEndian);
						if (original != null)
							return original;
					}
				}
			}
			return dateTime;
		}

		/// <summary>Yields (tag, type, count, offsetOfValueField) for each entry of the IFD at <paramref name="offset"/>.</summary>
		static IEnumerable<(ushort tag, ushort type, uint count, int valueOffset)> EnumerateIfd(byte[] tiff, uint offset, bool le) {
			if (offset + 2 > tiff.Length) yield break;
			int entryCount = ReadU16(tiff, (int)offset, le);
			if (entryCount > 512) yield break; // corrupt
			for (int i = 0; i < entryCount; i++) {
				int entry = (int)offset + 2 + i * 12;
				if (entry + 12 > tiff.Length) yield break;
				yield return (ReadU16(tiff, entry, le), ReadU16(tiff, entry + 2, le), ReadU32(tiff, entry + 4, le), entry + 8);
			}
		}

		/// <summary>Reads an ASCII tag value; values longer than 4 bytes are stored at an offset.</summary>
		static string? ReadAscii(byte[] tiff, ushort type, uint count, int valueFieldOffset, bool le) {
			if (type != 2 || count == 0 || count > 64) return null;
			int dataOffset = count <= 4 ? valueFieldOffset : (int)ReadU32(tiff, valueFieldOffset, le);
			if (dataOffset < 0 || dataOffset + count > tiff.Length) return null;
			return Encoding.ASCII.GetString(tiff, dataOffset, (int)count).TrimEnd('\0');
		}

		static ushort ReadU16(byte[] b, int o, bool le) => le
			? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o))
			: BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));
		static uint ReadU32(byte[] b, int o, bool le) => le
			? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o))
			: BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
	}
}
