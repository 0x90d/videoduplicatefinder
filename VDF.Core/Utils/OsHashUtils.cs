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
	// OpenSubtitles-style "oshash": a content fingerprint that is cheap enough to compute during a
	// scan yet stable across a move/rename. Value = file size + 64-bit little-endian checksum of the
	// first and last 64 KiB. This is the same algorithm stash uses, so a file keeps its identity when
	// it is moved on disk — letting a rescan relink it and reuse its analysis instead of re-decoding.
	public static class OsHashUtils {
		const int ChunkSize = 64 * 1024;

		// Best-effort. Returns null if the file is missing, locked, smaller than one chunk, or any IO
		// error occurs — callers treat null as "not fingerprintable" and fall back to full analysis,
		// never a wrong match. Requiring size >= 64 KiB keeps the head/tail chunks non-overlapping;
		// videos are always far larger, and tiny files aren't worth relinking (re-analysis is cheap).
		public static string? TryCompute(string path) {
			try {
				using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				long size = fs.Length;
				if (size < ChunkSize)
					return null;

				Span<byte> buf = stackalloc byte[ChunkSize];
				ulong sum = (ulong)size;

				fs.ReadExactly(buf);                          // first 64 KiB
				sum += SumLittleEndianU64(buf);

				fs.Seek(size - ChunkSize, SeekOrigin.Begin);  // last 64 KiB
				fs.ReadExactly(buf);
				sum += SumLittleEndianU64(buf);

				return sum.ToString("x16");
			}
			catch {
				return null;
			}
		}

		static ulong SumLittleEndianU64(ReadOnlySpan<byte> buf) {
			ulong sum = 0;
			for (int i = 0; i + 8 <= buf.Length; i += 8)
				sum += BinaryPrimitives.ReadUInt64LittleEndian(buf.Slice(i, 8));
			return sum;
		}
	}
}
