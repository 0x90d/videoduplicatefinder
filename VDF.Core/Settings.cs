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
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//


namespace VDF.Core {
	public sealed class Settings {
		public HashSet<string> IncludeList { get; } = new HashSet<string>();
		public HashSet<string> BlackList { get; } = new HashSet<string>();

		public bool IgnoreReadOnlyFolders;
		public bool IgnoreReparsePoints;
		public bool ExcludeHardLinks;
		public bool GeneratePreviewThumbnails;
		public bool UseNativeFfmpegBinding;
		public bool IncludeSubDirectories = true;
		public bool IncludeImages = true;
		public bool ExtendedFFToolsLogging;
		public bool AlwaysRetryFailedSampling;
		public bool IgnoreBlackPixels;
		public bool IgnoreWhitePixels;
		public bool CompareHorizontallyFlipped;
		public bool IncludeNonExistingFiles = true;
		public bool ScanAgainstEntireDatabase;
		public bool UsePHashing;

		public FFTools.FFHardwareAccelerationMode HardwareAccelerationMode;

		public byte Threshhold = 5;
		public float Percent = 96f;
		public double PercentDurationDifference = 20d;

		public int ThumbnailCount = 1;
		public int MaxDegreeOfParallelism = 1;

		public string CustomFFArguments = string.Empty;
		public string CustomDatabaseFolder = string.Empty;

		public bool FilterByFilePathContains;
		public List<string> FilePathContainsTexts = new();
		public bool FilterByFilePathNotContains;
		public List<string> FilePathNotContainsTexts = new();
		public bool FilterByFileSize;
		public int MaximumFileSize;
		public int MinimumFileSize;
	}
}
