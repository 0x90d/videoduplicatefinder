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

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using ReactiveUI;
using VDF.Core.Utils;

namespace VDF.GUI.Data {
	public class SettingsFile : ReactiveObject {
		private static SettingsFile? instance;

		[JsonIgnore]
		public static SettingsFile Instance => instance ??= new SettingsFile();

		public class CustomActionCommands {
			public string OpenItemInFolder { get; set; } = string.Empty;
			public string OpenMultipleInFolder { get; set; } = string.Empty;
			public string OpenItem { get; set; } = string.Empty;
			public string OpenMultiple { get; set; } = string.Empty;
		}

		[JsonPropertyName("Includes")]
		public ObservableCollection<string> Includes { get; set; } = new();
		[JsonPropertyName("Blacklists")]
		public ObservableCollection<string> Blacklists { get; set; } = new();

		string _LastCustomSelectExpression = string.Empty;
		[JsonPropertyName("LastCustomSelectExpression")]
		public string LastCustomSelectExpression {
			get => _LastCustomSelectExpression;
			set => this.RaiseAndSetIfChanged(ref _LastCustomSelectExpression, value);
		}
		bool _IgnoreReadOnlyFolders;
		[JsonPropertyName("IgnoreReadOnlyFolders")]
		public bool IgnoreReadOnlyFolders {
			get => _IgnoreReadOnlyFolders;
			set => this.RaiseAndSetIfChanged(ref _IgnoreReadOnlyFolders, value);
		}
		bool _ExcludeHardLinks;
		[JsonPropertyName("ExcludeHardLinks")]
		public bool ExcludeHardLinks {
			get => _ExcludeHardLinks;
			set => this.RaiseAndSetIfChanged(ref _ExcludeHardLinks, value);
		}
		bool _IgnoreReparsePoints;
		[JsonPropertyName("IgnoreReparsePoints")]
		public bool IgnoreReparsePoints {
			get => _IgnoreReparsePoints;
			set => this.RaiseAndSetIfChanged(ref _IgnoreReparsePoints, value);
		}
		bool _IgnoreBlackPixels;
		[JsonPropertyName("IgnoreBlackPixels")]
		public bool IgnoreBlackPixels {
			get => _IgnoreBlackPixels;
			set => this.RaiseAndSetIfChanged(ref _IgnoreBlackPixels, value);
		}
		bool _IgnoreWhitePixels;
		[JsonPropertyName("IgnoreWhitePixels")]
		public bool IgnoreWhitePixels {
			get => _IgnoreWhitePixels;
			set => this.RaiseAndSetIfChanged(ref _IgnoreWhitePixels, value);
		}
		int _MaxDegreeOfParallelism = -1;
		[JsonPropertyName("MaxDegreeOfParallelism")]
		public int MaxDegreeOfParallelism {
			get => _MaxDegreeOfParallelism;
			set => this.RaiseAndSetIfChanged(ref _MaxDegreeOfParallelism, value);
		}
		Core.FFTools.FFHardwareAccelerationMode _HardwareAccelerationMode = Core.FFTools.FFHardwareAccelerationMode.auto;
		[JsonPropertyName("HardwareAccelerationMode")]
		public Core.FFTools.FFHardwareAccelerationMode HardwareAccelerationMode {
			get => _HardwareAccelerationMode;
			set => this.RaiseAndSetIfChanged(ref _HardwareAccelerationMode, value);
		}
		bool _CompareHorizontallyFlipped = false;
		[JsonPropertyName("CompareHorizontallyFlipped")]
		public bool CompareHorizontallyFlipped {
			get => _CompareHorizontallyFlipped;
			set => this.RaiseAndSetIfChanged(ref _CompareHorizontallyFlipped, value);
		}
		bool _IncludeSubDirectories = true;
		[JsonPropertyName("IncludeSubDirectories")]
		public bool IncludeSubDirectories {
			get => _IncludeSubDirectories;
			set => this.RaiseAndSetIfChanged(ref _IncludeSubDirectories, value);
		}
		bool _IncludeImages = true;
		[JsonPropertyName("IncludeImages")]
		public bool IncludeImages {
			get => _IncludeImages;
			set => this.RaiseAndSetIfChanged(ref _IncludeImages, value);
		}
		bool _GeneratePreviewThumbnails = true;
		[JsonPropertyName("GeneratePreviewThumbnails")]
		public bool GeneratePreviewThumbnails {
			get => _GeneratePreviewThumbnails;
			set => this.RaiseAndSetIfChanged(ref _GeneratePreviewThumbnails, value);
		}
		bool _ExtendedFFToolsLogging;
		[JsonPropertyName("ExtendedFFToolsLogging")]
		public bool ExtendedFFToolsLogging {
			get => _ExtendedFFToolsLogging;
			set => this.RaiseAndSetIfChanged(ref _ExtendedFFToolsLogging, value);
		}
		bool _AlwaysRetryFailedSampling = false;
		[JsonPropertyName("AlwaysRetryFailedSampling")]
		public bool AlwaysRetryFailedSampling {
			get => _AlwaysRetryFailedSampling;
			set => this.RaiseAndSetIfChanged(ref _AlwaysRetryFailedSampling, value);
		}
		bool _UseNativeFfmpegBinding;
		[JsonPropertyName("UseNativeFfmpegBinding")]
		public bool UseNativeFfmpegBinding {
			get => _UseNativeFfmpegBinding;
			set => this.RaiseAndSetIfChanged(ref _UseNativeFfmpegBinding, value);
		}
		string _CustomFFArguments = string.Empty;
		[JsonPropertyName("CustomFFArguments")]
		public string CustomFFArguments {
			get => _CustomFFArguments;
			set => this.RaiseAndSetIfChanged(ref _CustomFFArguments, value);
		}
		bool _BackupAfterListChanged = true;
		[JsonPropertyName("BackupAfterListChanged")]
		public bool BackupAfterListChanged {
			get => _BackupAfterListChanged;
			set => this.RaiseAndSetIfChanged(ref _BackupAfterListChanged, value);
		}
		bool _AskToSaveResultsOnExit = true;
		[JsonPropertyName("AskToSaveResultsOnExit")]
		public bool AskToSaveResultsOnExit {
			get => _AskToSaveResultsOnExit;
			set => this.RaiseAndSetIfChanged(ref _AskToSaveResultsOnExit, value);
		}
		bool _IncludeNonExistingFiles;
		[JsonPropertyName("IncludeNonExistingFiles")]
		public bool IncludeNonExistingFiles {
			get => _IncludeNonExistingFiles;
			set => this.RaiseAndSetIfChanged(ref _IncludeNonExistingFiles, value);
		}
		bool _ScanAgainstEntireDatabase;
		[JsonPropertyName("ScanAgainstEntireDatabase")]
		public bool ScanAgainstEntireDatabase {
			get => _ScanAgainstEntireDatabase;
			set => this.RaiseAndSetIfChanged(ref _ScanAgainstEntireDatabase, value);
		}
		bool _UsePHash;
		[JsonPropertyName("UsePHash")]
		public bool UsePHash {
			get => _UsePHash;
			set => this.RaiseAndSetIfChanged(ref _UsePHash, value);
		}
		int _Percent = 95;
		[JsonPropertyName("Percent")]
		public int Percent {
			get => _Percent;
			set => this.RaiseAndSetIfChanged(ref _Percent, value);
		}
		int _PercentDurationDifference = 20;
		[JsonPropertyName("PercentDurationDifference")]
		public int PercentDurationDifference {
			get => _PercentDurationDifference;
			set => this.RaiseAndSetIfChanged(ref _PercentDurationDifference, value);
		}
		int _Thumbnails = 1;
		[JsonPropertyName("Thumbnails")]
		public int Thumbnails {
			get => _Thumbnails;
			set => this.RaiseAndSetIfChanged(ref _Thumbnails, value);
		}
		[JsonPropertyName("CustomCommands")]
		public CustomActionCommands CustomCommands { get; set; } = new();
		string _CustomDatabaseFolder = string.Empty;
		[JsonPropertyName("CustomDatabaseFolder")]
		public string CustomDatabaseFolder {
			get => _CustomDatabaseFolder;
			set => this.RaiseAndSetIfChanged(ref _CustomDatabaseFolder, value);
		}

		public static void SaveSettings(string? path = null) {
			path ??= FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Settings.json");
			File.WriteAllText(path, JsonSerializer.Serialize(instance));
		}

		bool _UseMica = false;
		[JsonPropertyName("UseMica")]
		public bool UseMica {
			get => _UseMica;
			set => this.RaiseAndSetIfChanged(ref _UseMica, value);
		}
		bool _DarkMode = true;
		[JsonPropertyName("DarkMode")]
		public bool DarkMode {
			get => _DarkMode;
			set => this.RaiseAndSetIfChanged(ref _DarkMode, value);
		}
		bool _ShowThumbnailColumn = true;
		[JsonPropertyName("ShowThumbnailColumn")]
		public bool ShowThumbnailColumn {
			get => _ShowThumbnailColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowThumbnailColumn, value);
		}
		bool _ShowPathColumn = true;
		[JsonPropertyName("ShowPathColumn")]
		public bool ShowPathColumn {
			get => _ShowPathColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowPathColumn, value);
		}
		bool _ShowDurationColumn = true;
		[JsonPropertyName("ShowDurationColumn")]
		public bool ShowDurationColumn {
			get => _ShowDurationColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowDurationColumn, value);
		}
		bool _ShowFormatColumn = true;
		[JsonPropertyName("ShowFormatColumn")]
		public bool ShowFormatColumn {
			get => _ShowFormatColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowFormatColumn, value);
		}
		bool _ShowAudioColumn = true;
		[JsonPropertyName("ShowAudioColumn")]
		public bool ShowAudioColumn {
			get => _ShowAudioColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowAudioColumn, value);
		}
		bool _ShowSimilarityColumn = true;
		[JsonPropertyName("ShowSimilarityColumn")]
		public bool ShowSimilarityColumn {
			get => _ShowSimilarityColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowSimilarityColumn, value);
		}
		bool _FilterByFilePathContains;
		[JsonPropertyName("FilterByFilePathContains")]
		public bool FilterByFilePathContains {
			get => _FilterByFilePathContains;
			set => this.RaiseAndSetIfChanged(ref _FilterByFilePathContains, value);
		}
		ObservableCollection<string> _FilePathContainsTexts = new();
		[JsonPropertyName("FilePathContainsTexts")]
		public ObservableCollection<string> FilePathContainsTexts {
			get => _FilePathContainsTexts;
			set => this.RaiseAndSetIfChanged(ref _FilePathContainsTexts, value);
		}
		bool _FilterByFilePathNotContains;
		[JsonPropertyName("FilterByFilePathNotContains")]
		public bool FilterByFilePathNotContains {
			get => _FilterByFilePathNotContains;
			set => this.RaiseAndSetIfChanged(ref _FilterByFilePathNotContains, value);
		}
		ObservableCollection<string> _FilePathNotContainsTexts = new();
		[JsonPropertyName("FilePathNotContainsTexts")]
		public ObservableCollection<string> FilePathNotContainsTexts {
			get => _FilePathNotContainsTexts;
			set => this.RaiseAndSetIfChanged(ref _FilePathNotContainsTexts, value);
		}
		bool _FilterByFileSize;
		[JsonPropertyName("FilterByFileSize")]
		public bool FilterByFileSize {
			get => _FilterByFileSize;
			set => this.RaiseAndSetIfChanged(ref _FilterByFileSize, value);
		}
		int _MaximumFileSize = 999999999;
		[JsonPropertyName("MaximumFileSize")]
		public int MaximumFileSize {
			get => _MaximumFileSize;
			set => this.RaiseAndSetIfChanged(ref _MaximumFileSize, value);
		}
		int _MinimumFileSize = 0;
		[JsonPropertyName("MinimumFileSize")]
		public int MinimumFileSize {
			get => _MinimumFileSize;
			set => this.RaiseAndSetIfChanged(ref _MinimumFileSize, value);
		}

		public static void LoadSettings(string? path = null) {
			if ((path == null || path.EndsWith(".xml")) && LoadOldSettings(path))
				return;

			path ??= FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Settings.json");
			if (!File.Exists(path)) return;
			instance = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllBytes(path));
		}

		static bool LoadOldSettings(string? path) {
			path ??= FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Settings.xml");
			if (!File.Exists(path)) return false;
			var xDoc = XDocument.Load(path);
			foreach (var n in xDoc.Descendants("Include"))
				Instance.Includes.Add(n.Value);
			foreach (var n in xDoc.Descendants("Exclude"))
				Instance.Blacklists.Add(n.Value);
			foreach (var n in xDoc.Descendants("Percent"))
				if (int.TryParse(n.Value, out var value))
					Instance.Percent = value;
			foreach (var n in xDoc.Descendants("MaxDegreeOfParallelism"))
				if (int.TryParse(n.Value, out var value))
					Instance.MaxDegreeOfParallelism = value;
			foreach (var n in xDoc.Descendants("Thumbnails"))
				if (int.TryParse(n.Value, out var value))
					Instance.Thumbnails = value;
			foreach (var n in xDoc.Descendants("IncludeSubDirectories"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IncludeSubDirectories = value;
			foreach (var n in xDoc.Descendants("IncludeImages"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IncludeImages = value;
			foreach (var n in xDoc.Descendants("IgnoreReadOnlyFolders"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreReadOnlyFolders = value;
			//09.03.21: UseCuda is obsolete and has been replaced with UseHardwareAcceleration.
			foreach (var n in xDoc.Descendants("UseCuda"))
				if (bool.TryParse(n.Value, out var value))
					Instance.HardwareAccelerationMode = value ? Core.FFTools.FFHardwareAccelerationMode.auto : Core.FFTools.FFHardwareAccelerationMode.none;
			foreach (var n in xDoc.Descendants("HardwareAccelerationMode"))
				if (Enum.TryParse<Core.FFTools.FFHardwareAccelerationMode>(n.Value, out var value))
					Instance.HardwareAccelerationMode = value;
			foreach (var n in xDoc.Descendants("GeneratePreviewThumbnails"))
				if (bool.TryParse(n.Value, out var value))
					Instance.GeneratePreviewThumbnails = value;
			foreach (var n in xDoc.Descendants("IgnoreHardlinks"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreReparsePoints = value;
			foreach (var n in xDoc.Descendants("ExtendedFFToolsLogging"))
				if (bool.TryParse(n.Value, out var value))
					Instance.ExtendedFFToolsLogging = value;
			foreach (var n in xDoc.Descendants("AlwaysRetryFailedSampling"))
				if (bool.TryParse(n.Value, out var value))
					Instance.AlwaysRetryFailedSampling = value;
			foreach (var n in xDoc.Descendants("UseNativeFfmpegBinding"))
				if (bool.TryParse(n.Value, out var value))
					Instance.UseNativeFfmpegBinding = value;
			foreach (var n in xDoc.Descendants("BackupAfterListChanged"))
				if (bool.TryParse(n.Value, out var value))
					Instance.BackupAfterListChanged = value;
			foreach (var n in xDoc.Descendants("IgnoreBlackPixels"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreBlackPixels = value;
			foreach (var n in xDoc.Descendants("IgnoreWhitePixels"))
				if (bool.TryParse(n.Value, out var value))
					Instance.IgnoreWhitePixels = value;
			foreach (var n in xDoc.Descendants("CustomFFArguments"))
				Instance.CustomFFArguments = n.Value;
			foreach (var n in xDoc.Descendants("LastCustomSelectExpression"))
				Instance.LastCustomSelectExpression = n.Value;
			foreach (var n in xDoc.Descendants("CompareHorizontallyFlipped"))
				if (bool.TryParse(n.Value, out var value))
					Instance.CompareHorizontallyFlipped = value;
			SaveSettings(Path.ChangeExtension(path, "json"));
			File.Delete(path);
			return true;
		}
	}
}
