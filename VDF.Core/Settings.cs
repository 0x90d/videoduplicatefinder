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
	public enum FolderMatchMode { None, SameFolderOnly, DifferentFolderOnly }

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
		public bool LogExcludedFiles;
		public bool AlwaysRetryFailedSampling;
		public bool IgnoreBlackPixels;
		public bool IgnoreWhitePixels;
		public bool CompareHorizontallyFlipped;
		public bool IncludeNonExistingFiles;
		public bool ScanAgainstEntireDatabase;
		public FolderMatchMode FolderMatchMode;
		public int SameFolderDepth = 1;
		public bool UsePHashing;
		public bool UseExifCreationDate;
		public string LanguageCode = "en";

		public FFTools.FFHardwareAccelerationMode HardwareAccelerationMode;

		public byte Threshhold = 5;
		public float Percent = 96f;
		public double PercentDurationDifference = 20d;
		public double DurationDifferenceMinSeconds;
		public double DurationDifferenceMaxSeconds;
		public double MaxSamplingDurationSeconds;

		public int ThumbnailCount = 1;
		/// <summary>Maximum width in pixels for display thumbnails (0 = original resolution).</summary>
		public int ThumbnailMaxWidth = 100;
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

		// ── Partial clip detection ──────────────────────────────────────────────
		/// <summary>Enable audio-fingerprint-based partial clip detection.</summary>
		public bool EnablePartialClipDetection;
		/// <summary>
		/// Minimum ratio of clip-duration / source-duration for a pair to be a candidate.
		/// Default 0.10 (clip must be at least 10% of the longer video).
		/// </summary>
		public double PartialClipMinRatio = 0.10;
		/// <summary>
		/// Minimum average Hamming similarity (0–1) for a sliding-window match to be
		/// accepted as a partial clip.  Default 0.80.
		/// </summary>
		public double PartialClipSimilarityThreshold = 0.80;

		// ── Database checkpoints ────────────────────────────────────────────
		/// <summary>
		/// Interval in minutes between automatic database saves during scanning.
		/// 0 = disabled (only save at phase boundaries). Default 5.
		/// </summary>
		public int DatabaseCheckpointIntervalMinutes = 5;
	}
}
