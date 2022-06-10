// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//


namespace VDF.Core {
	internal readonly struct FileEnumerationSettings {
		internal readonly bool IgnoreReadOnlyFolders;
		internal readonly bool IgnoreReparsePoints;
		internal readonly bool IncludeSubDirectories;
		internal readonly bool IncludeImages;
		internal readonly List<string> BlackList;
		internal readonly bool FilterByFilePathContains;
		internal readonly string FilePathContainsText;
		internal readonly bool FilterByFilePathNotContains;
		internal readonly string FilePathNotContainsText;
		internal readonly bool FilterByFileSize;
		internal readonly int MaximumFileSize;
		internal readonly int MinimumFileSize;

		public FileEnumerationSettings(bool ignoreReadOnlyFolders,
			bool ignoreReparsePoints,
			bool includeSubDirectories,
			bool includeImages,
			List<string> blackList,
			bool filterByFilePathContains,
			string filePathContainsText,
			bool filterByFilePathNotContains,
			string filePathNotContainsText,
			bool filterByFileSize,
			int maximumFileSize,
			int minimumFileSize) {

			IgnoreReadOnlyFolders = ignoreReadOnlyFolders;
			IgnoreReparsePoints = ignoreReparsePoints;
			IncludeSubDirectories = includeSubDirectories;
			IncludeImages = includeImages;
			BlackList = blackList;
			FilterByFilePathContains = filterByFilePathContains;
			FilePathContainsText = filePathContainsText;
			FilterByFilePathNotContains = filterByFilePathNotContains;
			FilePathNotContainsText = filePathNotContainsText;
			FilterByFileSize = filterByFileSize;
			MaximumFileSize = maximumFileSize;
			MinimumFileSize = minimumFileSize;
		}
	}
}
