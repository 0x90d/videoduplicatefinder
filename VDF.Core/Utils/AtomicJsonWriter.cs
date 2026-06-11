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

using System.Text.Json;

namespace VDF.Core.Utils {
	/// <summary>
	/// Writes JSON to a file via a temporary sibling and an atomic rename so a crash
	/// mid-write cannot leave a truncated or empty real file. On Windows
	/// <c>File.Move(overwrite: true)</c> is implemented atomically (MoveFileEx with
	/// REPLACE_EXISTING); on POSIX <c>rename(2)</c> is atomic.
	/// </summary>
	public static class AtomicJsonWriter {
		// Only the JsonTypeInfo overload exists: the JsonSerializerOptions-based one
		// carried RequiresUnreferencedCode and was unused.
		public static async Task WriteAsync<T>(
			string path,
			T value,
			System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
			CancellationToken ct = default) {

			string dir = Path.GetDirectoryName(path)!;
			string tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp");

			try {
				await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true)) {
					await JsonSerializer.SerializeAsync(fs, value, typeInfo, ct);
				}
				File.Move(tmp, path, overwrite: true);
			}
			finally {
				try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
			}
		}
	}
}
