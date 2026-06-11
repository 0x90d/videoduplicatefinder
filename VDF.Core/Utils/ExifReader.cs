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
