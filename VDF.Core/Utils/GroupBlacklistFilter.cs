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

namespace VDF.Core.Utils {
	/// <summary>
	/// Pure helpers for the user-managed "not a match" group blacklist. The blacklist
	/// is a list of path-sets that the user has marked as not real duplicates; a group
	/// from a scan is considered blacklisted if every current path in the group is
	/// covered by some single blacklist entry.
	/// </summary>
	public static class GroupBlacklistFilter {
		/// <summary>
		/// Returns the set of GroupIds that are fully covered by some entry in
		/// <paramref name="blacklist"/> (i.e. every current path in the group appears
		/// in that blacklist entry). Subset semantics are intentional: if the user
		/// marked {A,B,C} as not a match, a later scan finding only {A,B} as
		/// duplicates is still treated as not a match.
		/// </summary>
		public static HashSet<Guid> ComputeBlacklistedGroupIds(
			IEnumerable<(Guid GroupId, string Path)> items,
			IReadOnlyList<HashSet<string>> blacklist) {

			if (blacklist == null || blacklist.Count == 0)
				return new HashSet<Guid>();

			var groupPaths = new Dictionary<Guid, List<string>>();
			foreach (var (gid, path) in items) {
				if (!groupPaths.TryGetValue(gid, out var list))
					groupPaths[gid] = list = new List<string>();
				list.Add(path);
			}

			// Manual subset check via blackListedGroup.Contains so we always defer
			// to the blacklist set's comparer (which BlacklistStore configures with
			// the platform's path comparer for case sensitivity).
			var result = new HashSet<Guid>();
			foreach (var kv in groupPaths) {
				foreach (var blackListedGroup in blacklist) {
					bool covered = true;
					foreach (var path in kv.Value) {
						if (!blackListedGroup.Contains(path)) {
							covered = false;
							break;
						}
					}
					if (covered) {
						result.Add(kv.Key);
						break;
					}
				}
			}
			return result;
		}
	}
}
