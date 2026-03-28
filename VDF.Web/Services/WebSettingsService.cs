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
			public int ThumbnailCount { get; set; } = 1;
			public bool IncludeSubDirectories { get; set; } = true;
			public bool IncludeImages { get; set; } = true;
			public bool UsePHashing { get; set; }
			public bool UseNativeFfmpegBinding { get; set; }
			[JsonConverter(typeof(JsonStringEnumConverter))]
			public FFHardwareAccelerationMode HardwareAccelerationMode { get; set; }
			public string CustomFFArguments { get; set; } = string.Empty;
			public string CustomDatabaseFolder { get; set; } = string.Empty;
			public bool CompareHorizontallyFlipped { get; set; }
			public bool IgnoreBlackPixels { get; set; }
			public bool IgnoreWhitePixels { get; set; }
			public bool ScanAgainstEntireDatabase { get; set; }
			public bool EnablePartialClipDetection { get; set; }
			public double PartialClipMinRatio { get; set; } = 0.10;
			public double PartialClipSimilarityThreshold { get; set; } = 0.80;
		}

		static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

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
				var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(SettingsPath), JsonOpts);
				if (dto == null) return false;
				foreach (var p in dto.IncludeList) s.IncludeList.Add(p);
				foreach (var p in dto.BlackList) s.BlackList.Add(p);
				s.Threshhold = dto.Threshhold;
				s.Percent = dto.Percent;
				s.PercentDurationDifference = dto.PercentDurationDifference;
				s.MaxDegreeOfParallelism = dto.MaxDegreeOfParallelism;
				s.ThumbnailCount = dto.ThumbnailCount;
				s.IncludeSubDirectories = dto.IncludeSubDirectories;
				s.IncludeImages = dto.IncludeImages;
				s.UsePHashing = dto.UsePHashing;
				s.UseNativeFfmpegBinding = dto.UseNativeFfmpegBinding;
				s.HardwareAccelerationMode = dto.HardwareAccelerationMode;
				s.CustomFFArguments = dto.CustomFFArguments;
				s.CustomDatabaseFolder = dto.CustomDatabaseFolder;
				s.CompareHorizontallyFlipped = dto.CompareHorizontallyFlipped;
				s.IgnoreBlackPixels = dto.IgnoreBlackPixels;
				s.IgnoreWhitePixels = dto.IgnoreWhitePixels;
				s.ScanAgainstEntireDatabase = dto.ScanAgainstEntireDatabase;
				s.EnablePartialClipDetection = dto.EnablePartialClipDetection;
				s.PartialClipMinRatio = dto.PartialClipMinRatio;
				s.PartialClipSimilarityThreshold = dto.PartialClipSimilarityThreshold;
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
					ThumbnailCount = s.ThumbnailCount,
					IncludeSubDirectories = s.IncludeSubDirectories,
					IncludeImages = s.IncludeImages,
					UsePHashing = s.UsePHashing,
					UseNativeFfmpegBinding = s.UseNativeFfmpegBinding,
					HardwareAccelerationMode = s.HardwareAccelerationMode,
					CustomFFArguments = s.CustomFFArguments,
					CustomDatabaseFolder = s.CustomDatabaseFolder,
					CompareHorizontallyFlipped = s.CompareHorizontallyFlipped,
					IgnoreBlackPixels = s.IgnoreBlackPixels,
					IgnoreWhitePixels = s.IgnoreWhitePixels,
					ScanAgainstEntireDatabase = s.ScanAgainstEntireDatabase,
					EnablePartialClipDetection = s.EnablePartialClipDetection,
					PartialClipMinRatio = s.PartialClipMinRatio,
					PartialClipSimilarityThreshold = s.PartialClipSimilarityThreshold,
				};
				File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dto, JsonOpts));
				return true;
			}
			catch { return false; }
		}
	}
}
