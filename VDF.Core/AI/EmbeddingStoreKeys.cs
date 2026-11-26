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

using System.Collections.Concurrent;
using System.Linq;
using VDF.Core.Utils;

namespace VDF.Core.AI {
	static class EmbeddingStoreKeys {
		/// <summary>
		/// Re-keys records whose path is not a database path exactly but is one when case and Unicode
		/// normalization are ignored. The scan database respells entries to their on-disk spelling
		/// (typically a whole library at once), and records stay valid across that: they still check
		/// size and modification time. Several candidate spellings are ambiguous and left alone, as is
		/// a spelling that already has its own record. Returns the number of records re-keyed.
		/// </summary>
		internal static int Rekey<T>(ConcurrentDictionary<string, T> records, IReadOnlySet<string> databasePaths) where T : class {
			var unmatched = records.Keys.Where(k => !databasePaths.Contains(k)).ToList();
			if (unmatched.Count == 0)
				return 0;
			var byLooseKey = databasePaths.ToLookup(FileUtils.NormalizeOrSelf, StringComparer.OrdinalIgnoreCase);
			int rekeyed = 0;
			foreach (string old in unmatched) {
				var candidates = byLooseKey[FileUtils.NormalizeOrSelf(old)].Take(2).ToList();
				if (candidates.Count == 1 && !records.ContainsKey(candidates[0]) && records.TryRemove(old, out T? record) && records.TryAdd(candidates[0], record))
					rekeyed++;
			}
			return rekeyed;
		}
	}
}
