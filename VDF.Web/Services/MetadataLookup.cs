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

using VDF.Core.Utils;

namespace VDF.Web.Services {
	/// <summary>
	/// Reads the metadata tags of a group's files for the results page (#926), on the server
	/// where the files are, a few at a time and off the render thread.
	/// </summary>
	internal static class MetadataLookup {
		/// <summary>Test seam: what reads one file (null = not readable).</summary>
		internal static Func<string, IReadOnlyList<MetadataField>?> Reader = FileMetadata.Read;

		internal static Task<IReadOnlyList<MetadataField>?[]> ReadAllAsync(IReadOnlyList<string> paths) {
			var read = Reader;
			return Task.Run(() => {
				var results = new IReadOnlyList<MetadataField>?[paths.Count];
				Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => results[i] = read(paths[i]));
				return results;
			});
		}
	}
}
