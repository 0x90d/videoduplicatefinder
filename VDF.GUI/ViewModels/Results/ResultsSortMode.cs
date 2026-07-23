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

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// Group-level sort modes for the results list. Every mode orders whole groups by an
	/// aggregate and, where meaningful, the members inside each group by the same dimension.
	/// </summary>
	public enum ResultsSortMode {
		/// <summary>Bytes freed if only the largest member of the group were kept. The default.</summary>
		WastedSpace,
		/// <summary>Sum of all member file sizes.</summary>
		TotalSize,
		/// <summary>Size of the largest member.</summary>
		LargestFile,
		/// <summary>Number of files in the group.</summary>
		FileCount,
		/// <summary>Highest similarity among the members.</summary>
		Similarity,
		/// <summary>Newest member creation date.</summary>
		DateCreated,
		/// <summary>Longest member duration.</summary>
		Duration,
		/// <summary>Highest member resolution (width + height, like the quality criterion). Back from v4.0 (#846).</summary>
		Resolution,
		/// <summary>Alphabetical by the first member path.</summary>
		FolderPath,
		/// <summary>Groups that contain checked items first.</summary>
		GroupsWithCheckedItems,
	}
}
