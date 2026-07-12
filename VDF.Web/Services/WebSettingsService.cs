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
using System.Text.Json.Serialization;
using VDF.Core;
using VDF.Core.FFTools;

namespace VDF.Web.Services {
	public sealed class WebSettingsService {
		/// <summary>JSON-serializable mirror of the settings relevant to VDF.Web.</summary>
		public sealed class Dto {
			public List<string> IncludeList { get; set; } = new();
			public List<string> BlackList { get; set; } = new();
			public byte Threshhold { get; set; } = 5;
			public float Percent { get; set; } = 96f;
			public double PercentDurationDifference { get; set; } = 20d;
			public int MaxDegreeOfParallelism { get; set; } = 1;
			public int MatchingMaxDegreeOfParallelism { get; set; }
			public int ThumbnailCount { get; set; } = 1;
			public bool IncludeSubDirectories { get; set; } = true;
			public bool IncludeImages { get; set; } = true;
			public bool UsePHashing { get; set; }
			public bool IgnoreReadOnlyFolders { get; set; }
			public bool IgnoreReparsePoints { get; set; }
			public bool ExcludeHardLinks { get; set; }
			public bool UseExifCreationDate { get; set; }
			public bool AlwaysRetryFailedSampling { get; set; }
			public bool ExtendedFFToolsLogging { get; set; }
			public bool LogExcludedFiles { get; set; }
			public bool UseNativeFfmpegBinding { get; set; }
			[JsonConverter(typeof(JsonStringEnumConverter<FFHardwareAccelerationMode>))]
			public FFHardwareAccelerationMode HardwareAccelerationMode { get; set; }
			public string CustomFFArguments { get; set; } = string.Empty;
			public string CustomDatabaseFolder { get; set; } = string.Empty;
			public int DatabaseCheckpointIntervalMinutes { get; set; } = 5;
			public bool CompareHorizontallyFlipped { get; set; }
			public bool IgnoreBlackPixels { get; set; }
			public bool IgnoreWhitePixels { get; set; }
			public bool IncludeNonExistingFiles { get; set; }
			public bool ScanAgainstEntireDatabase { get; set; }
			[JsonConverter(typeof(JsonStringEnumConverter<FolderMatchMode>))]
			public FolderMatchMode FolderMatchMode { get; set; }
			public int SameFolderDepth { get; set; } = 1;
			public double DurationDifferenceMinSeconds { get; set; }
			public double DurationDifferenceMaxSeconds { get; set; }
			public double MaxSamplingDurationSeconds { get; set; }
			public bool FilterByFileSize { get; set; }
			public int MinimumFileSize { get; set; }
			public int MaximumFileSize { get; set; }
			public bool FilterByFilePathContains { get; set; }
			public List<string> FilePathContainsTexts { get; set; } = new();
			public bool FilterByFilePathNotContains { get; set; }
			public List<string> FilePathNotContainsTexts { get; set; } = new();
			public bool EnablePartialClipDetection { get; set; }
			public double PartialClipMinRatio { get; set; } = 0.10;
			public double PartialClipSimilarityThreshold { get; set; } = 0.80;
			public bool PartialClipRequireVisualMatch { get; set; } = true;
			public double PartialClipVisualThreshold { get; set; } = 0.85;
			public bool UseAiMatching { get; set; }
			public float AiPercent { get; set; } = 94f;
			public bool EnableAiPartialDetection { get; set; }
			public float AiPartialHitPercent { get; set; } = 89f;

			// WebUI-only settings (not in VDF.Core Settings)
			/// <summary>Whether to automatically load HQ thumbnails on the results page.</summary>
			public bool AutoLoadThumbnails { get; set; } = true;
			/// <summary>Thumbnail resolution width in pixels (48–960). Lower = less memory, more pixelated.</summary>
			public int ThumbnailWidth { get; set; } = 480;
			/// <summary>JPEG quality for thumbnails (10–95). Lower = smaller, more artifacts.</summary>
			public int ThumbnailJpegQuality { get; set; } = 85;
		}

		/// <summary>WebUI-only settings that don't belong in VDF.Core.Settings.</summary>
		public bool AutoLoadThumbnails { get; set; } = true;
		public int ThumbnailWidth { get; set; } = 480;
		public int ThumbnailJpegQuality { get; set; } = 85;

		static string SettingsPath {
			get {
				string folder;
				if (OperatingSystem.IsWindows())
					folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VDF");
				else if (OperatingSystem.IsMacOS())
					folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences", "VDF");
				else
					folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
						?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "VDF");
				return Path.Combine(folder, "web-settings.json");
			}
		}

		public bool Load(Settings s) {
			if (!File.Exists(SettingsPath)) return false;
			try {
				var dto = JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), WebJsonContext.Default.Dto);
				if (dto == null) return false;
				foreach (var p in dto.IncludeList) s.IncludeList.Add(p);
				foreach (var p in dto.BlackList) s.BlackList.Add(p);
				s.Threshhold = dto.Threshhold;
				s.Percent = dto.Percent;
				s.PercentDurationDifference = dto.PercentDurationDifference;
				s.MaxDegreeOfParallelism = dto.MaxDegreeOfParallelism;
				s.MatchingMaxDegreeOfParallelism = dto.MatchingMaxDegreeOfParallelism;
				s.ThumbnailCount = dto.ThumbnailCount;
				s.IncludeSubDirectories = dto.IncludeSubDirectories;
				s.IncludeImages = dto.IncludeImages;
				s.UsePHashing = dto.UsePHashing;
				s.IgnoreReadOnlyFolders = dto.IgnoreReadOnlyFolders;
				s.IgnoreReparsePoints = dto.IgnoreReparsePoints;
				s.ExcludeHardLinks = dto.ExcludeHardLinks;
				s.UseExifCreationDate = dto.UseExifCreationDate;
				s.AlwaysRetryFailedSampling = dto.AlwaysRetryFailedSampling;
				s.ExtendedFFToolsLogging = dto.ExtendedFFToolsLogging;
				s.LogExcludedFiles = dto.LogExcludedFiles;
				s.UseNativeFfmpegBinding = dto.UseNativeFfmpegBinding;
				s.HardwareAccelerationMode = dto.HardwareAccelerationMode;
				s.CustomFFArguments = dto.CustomFFArguments;
				s.CustomDatabaseFolder = dto.CustomDatabaseFolder;
				s.DatabaseCheckpointIntervalMinutes = dto.DatabaseCheckpointIntervalMinutes;
				s.CompareHorizontallyFlipped = dto.CompareHorizontallyFlipped;
				s.IgnoreBlackPixels = dto.IgnoreBlackPixels;
				s.IgnoreWhitePixels = dto.IgnoreWhitePixels;
				s.IncludeNonExistingFiles = dto.IncludeNonExistingFiles;
				s.ScanAgainstEntireDatabase = dto.ScanAgainstEntireDatabase;
				s.FolderMatchMode = dto.FolderMatchMode;
				s.SameFolderDepth = dto.SameFolderDepth;
				s.DurationDifferenceMinSeconds = dto.DurationDifferenceMinSeconds;
				s.DurationDifferenceMaxSeconds = dto.DurationDifferenceMaxSeconds;
				s.MaxSamplingDurationSeconds = dto.MaxSamplingDurationSeconds;
				s.FilterByFileSize = dto.FilterByFileSize;
				s.MinimumFileSize = dto.MinimumFileSize;
				s.MaximumFileSize = dto.MaximumFileSize;
				s.FilterByFilePathContains = dto.FilterByFilePathContains;
				s.FilePathContainsTexts = dto.FilePathContainsTexts.ToList();
				s.FilterByFilePathNotContains = dto.FilterByFilePathNotContains;
				s.FilePathNotContainsTexts = dto.FilePathNotContainsTexts.ToList();
				s.EnablePartialClipDetection = dto.EnablePartialClipDetection;
				s.PartialClipMinRatio = dto.PartialClipMinRatio;
				s.PartialClipSimilarityThreshold = dto.PartialClipSimilarityThreshold;
				s.PartialClipRequireVisualMatch = dto.PartialClipRequireVisualMatch;
				s.PartialClipVisualThreshold = dto.PartialClipVisualThreshold;
				s.UseAiMatching = dto.UseAiMatching;
				// Same clamps as the GUI setters and the CLI options: a hand-edited value
				// like 0.94 (cosine fraction instead of percent) would otherwise flow into
				// the engine as a ~0.01 threshold and flag nearly every pair as AI-matched.
				s.AiPercent = Math.Clamp(dto.AiPercent, 50f, 100f);
				s.EnableAiPartialDetection = dto.EnableAiPartialDetection;
				s.AiPartialHitPercent = Math.Clamp(dto.AiPartialHitPercent, 70f, 99f);
				// WebUI-only
				AutoLoadThumbnails = dto.AutoLoadThumbnails;
				ThumbnailWidth = Math.Clamp(dto.ThumbnailWidth, 48, 960);
				ThumbnailJpegQuality = Math.Clamp(dto.ThumbnailJpegQuality, 10, 95);
				return true;
			}
			catch { return false; }
		}

		public bool Save(Settings s) {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
				var dto = new Dto {
					IncludeList = s.IncludeList.ToList(),
					BlackList = s.BlackList.ToList(),
					Threshhold = s.Threshhold,
					Percent = s.Percent,
					PercentDurationDifference = s.PercentDurationDifference,
					MaxDegreeOfParallelism = s.MaxDegreeOfParallelism,
					MatchingMaxDegreeOfParallelism = s.MatchingMaxDegreeOfParallelism,
					ThumbnailCount = s.ThumbnailCount,
					IncludeSubDirectories = s.IncludeSubDirectories,
					IncludeImages = s.IncludeImages,
					UsePHashing = s.UsePHashing,
					IgnoreReadOnlyFolders = s.IgnoreReadOnlyFolders,
					IgnoreReparsePoints = s.IgnoreReparsePoints,
					ExcludeHardLinks = s.ExcludeHardLinks,
					UseExifCreationDate = s.UseExifCreationDate,
					AlwaysRetryFailedSampling = s.AlwaysRetryFailedSampling,
					ExtendedFFToolsLogging = s.ExtendedFFToolsLogging,
					LogExcludedFiles = s.LogExcludedFiles,
					UseNativeFfmpegBinding = s.UseNativeFfmpegBinding,
					HardwareAccelerationMode = s.HardwareAccelerationMode,
					CustomFFArguments = s.CustomFFArguments,
					CustomDatabaseFolder = s.CustomDatabaseFolder,
					DatabaseCheckpointIntervalMinutes = s.DatabaseCheckpointIntervalMinutes,
					CompareHorizontallyFlipped = s.CompareHorizontallyFlipped,
					IgnoreBlackPixels = s.IgnoreBlackPixels,
					IgnoreWhitePixels = s.IgnoreWhitePixels,
					IncludeNonExistingFiles = s.IncludeNonExistingFiles,
					ScanAgainstEntireDatabase = s.ScanAgainstEntireDatabase,
					FolderMatchMode = s.FolderMatchMode,
					SameFolderDepth = s.SameFolderDepth,
					DurationDifferenceMinSeconds = s.DurationDifferenceMinSeconds,
					DurationDifferenceMaxSeconds = s.DurationDifferenceMaxSeconds,
					MaxSamplingDurationSeconds = s.MaxSamplingDurationSeconds,
					FilterByFileSize = s.FilterByFileSize,
					MinimumFileSize = s.MinimumFileSize,
					MaximumFileSize = s.MaximumFileSize,
					FilterByFilePathContains = s.FilterByFilePathContains,
					FilePathContainsTexts = s.FilePathContainsTexts.ToList(),
					FilterByFilePathNotContains = s.FilterByFilePathNotContains,
					FilePathNotContainsTexts = s.FilePathNotContainsTexts.ToList(),
					EnablePartialClipDetection = s.EnablePartialClipDetection,
					PartialClipMinRatio = s.PartialClipMinRatio,
					PartialClipSimilarityThreshold = s.PartialClipSimilarityThreshold,
					PartialClipRequireVisualMatch = s.PartialClipRequireVisualMatch,
					PartialClipVisualThreshold = s.PartialClipVisualThreshold,
					UseAiMatching = s.UseAiMatching,
					AiPercent = s.AiPercent,
					EnableAiPartialDetection = s.EnableAiPartialDetection,
					AiPartialHitPercent = s.AiPartialHitPercent,
					// WebUI-only
					AutoLoadThumbnails = AutoLoadThumbnails,
					ThumbnailWidth = ThumbnailWidth,
					ThumbnailJpegQuality = ThumbnailJpegQuality,
				};
				File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dto, WebJsonContext.Default.Dto));
				return true;
			}
			catch { return false; }
		}
	}
}
