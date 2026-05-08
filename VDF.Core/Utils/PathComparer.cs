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
	/// Equality comparer for filesystem paths that follows the typical case
	/// sensitivity of the running OS — Windows (NTFS) and macOS (default APFS) are
	/// case-insensitive; Linux is case-sensitive. Used by string-keyed collections
	/// where the keys are paths the user can't be expected to type with consistent
	/// casing across scans.
	/// </summary>
	public static class PathComparer {
		public static StringComparer ForCurrentPlatform { get; } =
			OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
				? StringComparer.OrdinalIgnoreCase
				: StringComparer.Ordinal;
	}
}
