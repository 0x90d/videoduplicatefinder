// /*
//     Copyright (C) 2025 0x90d
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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VDF.Core;
using VDF.Core.Utils;

namespace VDF.GUI.Data {
	internal static class PathRelocator {
		public static string NormalizePrefixPublic(string prefix) {
			if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
			var full = Path.GetFullPath(prefix);
			if (!full.EndsWith(Path.DirectorySeparatorChar))
				full += Path.DirectorySeparatorChar;
			return full;
		}

		/// <summary>
		/// Replace a path prefix for all entries whose path starts with oldPrefix.
		/// </summary>
		public static int RelocateByPrefix(IEnumerable<FileEntry> entries, string oldPrefix, string newPrefix) {
			var comparison = (CoreUtils.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
			oldPrefix = NormalizePrefixPublic(oldPrefix);
			newPrefix = NormalizePrefixPublic(newPrefix);

			int changed = 0;
			foreach (var fe in entries) {
				var full = Path.GetFullPath(fe.Path);
				if (full.StartsWith(oldPrefix, comparison)) {
					var suffix = full.Substring(oldPrefix.Length);
					var newPath = Path.Combine(newPrefix, suffix);
					fe.Path = newPath;
					changed++;
				}
			}
			return changed;
		}
	}
}
