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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using ReactiveUI;
using VDF.Core.Utils;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Data {
	public enum ThumbnailDoubleClickAction { OpenFile, OpenThumbnailComparer }

	public class SettingsFile : ReactiveObject {
		private static SettingsFile? instance;
		private static string? settingsPath;

		[JsonIgnore]
		public static SettingsFile Instance => instance ??= new SettingsFile();

		public SettingsFile() { }


		public static void SetSettingsPath(string? path) {
			settingsPath = string.IsNullOrWhiteSpace(path) ? null : path;
		}

		static string ResolveSettingsPath(string? path) {
			if (!string.IsNullOrWhiteSpace(path))
				return path;
			if (!string.IsNullOrWhiteSpace(settingsPath))
				return settingsPath;

			return FileUtils.SafePathCombine(CoreUtils.SettingsFolder, "Settings.json");
		}
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

		ObservableCollection<string> _ExpressionHistory = new();
		[JsonPropertyName("ExpressionHistory")]
		public ObservableCollection<string> ExpressionHistory {
			get => _ExpressionHistory;
			set => this.RaiseAndSetIfChanged(ref _ExpressionHistory, value);
		}

		ObservableCollection<ExpressionPreset> _ExpressionPresets = new();
		[JsonPropertyName("ExpressionPresets")]
		public ObservableCollection<ExpressionPreset> ExpressionPresets {
			get => _ExpressionPresets;
			set => this.RaiseAndSetIfChanged(ref _ExpressionPresets, value);
		}

		ObservableCollection<CustomSelectionPreset> _CustomSelectionPresets = new();
		[JsonPropertyName("CustomSelectionPresets")]
		public ObservableCollection<CustomSelectionPreset> CustomSelectionPresets {
			get => _CustomSelectionPresets;
			set => this.RaiseAndSetIfChanged(ref _CustomSelectionPresets, value);
		}

		bool _AutoApplySelectionPresetEnabled;
		[JsonPropertyName("AutoApplySelectionPresetEnabled")]
		public bool AutoApplySelectionPresetEnabled {
			get => _AutoApplySelectionPresetEnabled;
			set => this.RaiseAndSetIfChanged(ref _AutoApplySelectionPresetEnabled, value);
		}
		string _AutoApplySelectionPreset = string.Empty;
		/// <summary>Name of the custom-selection preset applied automatically after every scan.</summary>
		[JsonPropertyName("AutoApplySelectionPreset")]
		public string AutoApplySelectionPreset {
			get => _AutoApplySelectionPreset;
			set => this.RaiseAndSetIfChanged(ref _AutoApplySelectionPreset, value);
		}

		double? _MainWindowWidth;
		[JsonPropertyName("MainWindowWidth")]
		public double? MainWindowWidth {
			get => _MainWindowWidth;
			set => this.RaiseAndSetIfChanged(ref _MainWindowWidth, value);
		}
		double? _MainWindowHeight;
		[JsonPropertyName("MainWindowHeight")]
		public double? MainWindowHeight {
			get => _MainWindowHeight;
			set => this.RaiseAndSetIfChanged(ref _MainWindowHeight, value);
		}
		int? _MainWindowPositionX;
		[JsonPropertyName("MainWindowPositionX")]
		public int? MainWindowPositionX {
			get => _MainWindowPositionX;
			set => this.RaiseAndSetIfChanged(ref _MainWindowPositionX, value);
		}
		int? _MainWindowPositionY;
		[JsonPropertyName("MainWindowPositionY")]
		public int? MainWindowPositionY {
			get => _MainWindowPositionY;
			set => this.RaiseAndSetIfChanged(ref _MainWindowPositionY, value);
		}
		bool _MainWindowMaximized;
		[JsonPropertyName("MainWindowMaximized")]
		public bool MainWindowMaximized {
			get => _MainWindowMaximized;
			set => this.RaiseAndSetIfChanged(ref _MainWindowMaximized, value);
		}
		string _LanguageCode = ResolveDefaultLanguageCode();
		[JsonPropertyName("LanguageCode")]
		public string LanguageCode {
			get => _LanguageCode;
			set => this.RaiseAndSetIfChanged(ref _LanguageCode, ResolveLanguageCode(value));
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
		// IgnoreBlackPixels/IgnoreWhitePixels/CompareHorizontallyFlipped/Percent defaults
		// form the "Edited & altered copies" scan profile — the recommended default for
		// fresh installs (redesign stage 2). Existing settings files carry explicit
		// values for every key, so nobody's configuration changes.
		bool _IgnoreBlackPixels = true;
		[JsonPropertyName("IgnoreBlackPixels")]
		public bool IgnoreBlackPixels {
			get => _IgnoreBlackPixels;
			set => this.RaiseAndSetIfChanged(ref _IgnoreBlackPixels, value);
		}
		bool _IgnoreWhitePixels = true;
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
		int _MatchingMaxDegreeOfParallelism;
		/// <summary>Worker cap for the CPU-bound matching phases; 0 or less = automatic CPU-headroom cap — see Core setting.</summary>
		[JsonPropertyName("MatchingMaxDegreeOfParallelism")]
		public int MatchingMaxDegreeOfParallelism {
			get => _MatchingMaxDegreeOfParallelism;
			set => this.RaiseAndSetIfChanged(ref _MatchingMaxDegreeOfParallelism, value);
		}
		int _HddMaxDegreeOfParallelism = 2;
		/// <summary>Per-drive cap for slow drives (spindle HDDs / network shares) — see Core setting.</summary>
		[JsonPropertyName("HddMaxDegreeOfParallelism")]
		public int HddMaxDegreeOfParallelism {
			get => _HddMaxDegreeOfParallelism;
			set => this.RaiseAndSetIfChanged(ref _HddMaxDegreeOfParallelism, value);
		}
		Dictionary<string, string> _DriveTypeOverrides = new(StringComparer.OrdinalIgnoreCase);
		/// <summary>Drive root → "SSD"/"HDD" scan-concurrency overrides. No editor UI yet
		/// (planned with the per-drive scan rows); power users can edit Settings.json.</summary>
		[JsonPropertyName("DriveTypeOverrides")]
		public Dictionary<string, string> DriveTypeOverrides {
			get => _DriveTypeOverrides;
			// STJ drops the comparer on deserialization — re-wrap so drive letters stay case-insensitive.
			set => _DriveTypeOverrides = value == null
				? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
		}
		bool _WelcomeStripDismissed;
		/// <summary>The Setup screen's "New here?" hint strip, dismissible once.</summary>
		[JsonPropertyName("WelcomeStripDismissed")]
		public bool WelcomeStripDismissed {
			get => _WelcomeStripDismissed;
			set => this.RaiseAndSetIfChanged(ref _WelcomeStripDismissed, value);
		}
		ScanKnobs? _CustomScanKnobs;
		/// <summary>Snapshot of the profile-managed knobs from when the user last left a
		/// custom configuration; selecting the Custom profile restores it.</summary>
		[JsonPropertyName("CustomScanKnobs")]
		public ScanKnobs? CustomScanKnobs {
			get => _CustomScanKnobs;
			set => this.RaiseAndSetIfChanged(ref _CustomScanKnobs, value);
		}
		Core.FFTools.FFHardwareAccelerationMode _HardwareAccelerationMode = Core.FFTools.FFHardwareAccelerationMode.auto;
		[JsonPropertyName("HardwareAccelerationMode")]
		public Core.FFTools.FFHardwareAccelerationMode HardwareAccelerationMode {
			get => _HardwareAccelerationMode;
			set => this.RaiseAndSetIfChanged(ref _HardwareAccelerationMode, value);
		}
		// Part of the "Edited & altered copies" default profile (see IgnoreBlackPixels note).
		bool _CompareHorizontallyFlipped = true;
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
		int _ThumbnailMaxWidth = 100;
		[JsonPropertyName("ThumbnailMaxWidth")]
		public int ThumbnailMaxWidth {
			get => _ThumbnailMaxWidth;
			set {
				int clamped = Math.Clamp(value, 48, 960);
				if (clamped == _ThumbnailMaxWidth) return;
				this.RaiseAndSetIfChanged(ref _ThumbnailMaxWidth, clamped);
				// The old view sized its layout from this extraction width; keep that
				// behavior by moving the results Preview column along. The drag grip can
				// still diverge afterwards (a persisted ResultsPreviewWidth is loaded
				// after this property and wins on startup).
				ResultsPreviewWidth = clamped;
			}
		}
		bool _ExtendedFFToolsLogging;
		[JsonPropertyName("ExtendedFFToolsLogging")]
		public bool ExtendedFFToolsLogging {
			get => _ExtendedFFToolsLogging;
			set => this.RaiseAndSetIfChanged(ref _ExtendedFFToolsLogging, value);
		}
		bool _LogExcludedFiles;
		[JsonPropertyName("LogExcludedFiles")]
		public bool LogExcludedFiles {
			get => _LogExcludedFiles;
			set => this.RaiseAndSetIfChanged(ref _LogExcludedFiles, value);
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
		bool _RememberDeletedContent;
		[JsonPropertyName("RememberDeletedContent")]
		public bool RememberDeletedContent {
			get => _RememberDeletedContent;
			set => this.RaiseAndSetIfChanged(ref _RememberDeletedContent, value);
		}
		bool _AutoCheckDeletedContentMatches;
		[JsonPropertyName("AutoCheckDeletedContentMatches")]
		public bool AutoCheckDeletedContentMatches {
			get => _AutoCheckDeletedContentMatches;
			set => this.RaiseAndSetIfChanged(ref _AutoCheckDeletedContentMatches, value);
		}
		bool _ScanAgainstEntireDatabase;
		[JsonPropertyName("ScanAgainstEntireDatabase")]
		public bool ScanAgainstEntireDatabase {
			get => _ScanAgainstEntireDatabase;
			set => this.RaiseAndSetIfChanged(ref _ScanAgainstEntireDatabase, value);
		}
		Core.FolderMatchMode _FolderMatchMode;
		[JsonPropertyName("FolderMatchMode")]
		public Core.FolderMatchMode FolderMatchMode {
			get => _FolderMatchMode;
			set {
				this.RaiseAndSetIfChanged(ref _FolderMatchMode, value);
				this.RaisePropertyChanged(nameof(IsFolderMatchModeActive));
			}
		}
		public bool IsFolderMatchModeActive => FolderMatchMode != Core.FolderMatchMode.None;
		int _SameFolderDepth = 1;
		[JsonPropertyName("SameFolderDepth")]
		public int SameFolderDepth {
			get => _SameFolderDepth;
			set => this.RaiseAndSetIfChanged(ref _SameFolderDepth, value);
		}
		bool _UsePHash;
		[JsonPropertyName("UsePHash")]
		public bool UsePHash {
			get => _UsePHash;
			set => this.RaiseAndSetIfChanged(ref _UsePHash, value);
		}
		float _PHashSampleRatioPercent = 60f;
		/// <summary>Percentage of sampled frame positions that must individually pass the pHash threshold — see Core's PHashRequiredMatchingSampleRatio (0..1).</summary>
		[JsonPropertyName("PHashSampleRatioPercent")]
		public float PHashSampleRatioPercent {
			get => _PHashSampleRatioPercent;
			set => this.RaiseAndSetIfChanged(ref _PHashSampleRatioPercent, Math.Clamp(value, 1f, 100f));
		}
		bool _UseAiMatching;
		[JsonPropertyName("UseAiMatching")]
		public bool UseAiMatching {
			get => _UseAiMatching;
			set => this.RaiseAndSetIfChanged(ref _UseAiMatching, value);
		}
		float _AiPercent = 94f;
		/// <summary>Similarity threshold (percent = cosine·100) for the AI matching pass.</summary>
		[JsonPropertyName("AiPercent")]
		public float AiPercent {
			get => _AiPercent;
			set => this.RaiseAndSetIfChanged(ref _AiPercent, Math.Clamp(value, 50f, 100f));
		}
		bool _EnableAiPartialDetection;
		[JsonPropertyName("EnableAiPartialDetection")]
		public bool EnableAiPartialDetection {
			get => _EnableAiPartialDetection;
			set => this.RaiseAndSetIfChanged(ref _EnableAiPartialDetection, value);
		}
		float _AiPartialHitPercent = 89f;
		/// <summary>Per-frame hit threshold (percent) for the visual partial-duplicate pass.</summary>
		[JsonPropertyName("AiPartialHitPercent")]
		public float AiPartialHitPercent {
			get => _AiPartialHitPercent;
			set => this.RaiseAndSetIfChanged(ref _AiPartialHitPercent, Math.Clamp(value, 70f, 99f));
		}
		/// <summary>GUI mirror of Core Settings.NeedsAiComponents — keep the two in sync.</summary>
		[JsonIgnore]
		public bool NeedsAiComponents => UseAiMatching || EnableAiPartialDetection;
		bool _UseExifCreationDate;
		[JsonPropertyName("UseExifCreationDate")]
		public bool UseExifCreationDate {
			get => _UseExifCreationDate;
			set => this.RaiseAndSetIfChanged(ref _UseExifCreationDate, value);
		}
		// Part of the "Edited & altered copies" default profile (see IgnoreBlackPixels note).
		float _Percent = 92f;
		[JsonPropertyName("Percent")]
		public float Percent {
			get => _Percent;
			set => this.RaiseAndSetIfChanged(ref _Percent, value);
		}
		double _PercentDurationDifference = 20d;
		[JsonPropertyName("PercentDurationDifference")]
		public double PercentDurationDifference {
			get => _PercentDurationDifference;
			set => this.RaiseAndSetIfChanged(ref _PercentDurationDifference, value);
		}
		int _DurationDifferenceMinSeconds = 0;
		[JsonPropertyName("DurationDifferenceMinSeconds")]
		public int DurationDifferenceMinSeconds {
			get => _DurationDifferenceMinSeconds;
			set => this.RaiseAndSetIfChanged(ref _DurationDifferenceMinSeconds, value);
		}
		int _DurationDifferenceMaxSeconds = 0;
		[JsonPropertyName("DurationDifferenceMaxSeconds")]
		public int DurationDifferenceMaxSeconds {
			get => _DurationDifferenceMaxSeconds;
			set => this.RaiseAndSetIfChanged(ref _DurationDifferenceMaxSeconds, value);
		}
		int _MaxSamplingDurationSeconds = 0;
		[JsonPropertyName("MaxSamplingDurationSeconds")]
		public int MaxSamplingDurationSeconds {
			get => _MaxSamplingDurationSeconds;
			set => this.RaiseAndSetIfChanged(ref _MaxSamplingDurationSeconds, value);
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
		int _DatabaseCheckpointIntervalMinutes = 5;
		[JsonPropertyName("DatabaseCheckpointIntervalMinutes")]
		public int DatabaseCheckpointIntervalMinutes {
			get => _DatabaseCheckpointIntervalMinutes;
			set => this.RaiseAndSetIfChanged(ref _DatabaseCheckpointIntervalMinutes, Math.Max(0, value));
		}

		public static void SaveSettings(string? path = null) {
			path = ResolveSettingsPath(path);
			File.WriteAllText(path, JsonSerializer.Serialize(instance, GuiJsonContext.Default.SettingsFile));
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
		double? _ThumbnailComparerWindowWidth;
		[JsonPropertyName("ThumbnailComparerWindowWidth")]
		public double? ThumbnailComparerWindowWidth {
			get => _ThumbnailComparerWindowWidth;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowWidth, value);
		}
		double? _ThumbnailComparerWindowHeight;
		[JsonPropertyName("ThumbnailComparerWindowHeight")]
		public double? ThumbnailComparerWindowHeight {
			get => _ThumbnailComparerWindowHeight;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowHeight, value);
		}
		double? _ThumbnailComparerWindowPositionX;
		[JsonPropertyName("ThumbnailComparerWindowPositionX")]
		public double? ThumbnailComparerWindowPositionX {
			get => _ThumbnailComparerWindowPositionX;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowPositionX, value);
		}
		double? _ThumbnailComparerWindowPositionY;
		[JsonPropertyName("ThumbnailComparerWindowPositionY")]
		public double? ThumbnailComparerWindowPositionY {
			get => _ThumbnailComparerWindowPositionY;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowPositionY, value);
		}
		int? _ThumbnailComparerWindowScreenIndex;
		[JsonPropertyName("ThumbnailComparerWindowScreenIndex")]
		public int? ThumbnailComparerWindowScreenIndex {
			get => _ThumbnailComparerWindowScreenIndex;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerWindowScreenIndex, value);
		}
		// Side-by-side first (redesign stage 4, maintainer directive on comparer view modes).
		CompareMode _ThumbnailComparerMode = CompareMode.SideBySide;
		[JsonPropertyName("ThumbnailComparerMode")]
		public CompareMode ThumbnailComparerMode {
			get => _ThumbnailComparerMode;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailComparerMode, value);
		}
		bool _ShowThumbnailColumn = true;
		[JsonPropertyName("ShowThumbnailColumn")]
		public bool ShowThumbnailColumn {
			get => _ShowThumbnailColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowThumbnailColumn, value);
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
		bool _ShowBitrateColumn = true;
		[JsonPropertyName("ShowBitrateColumn")]
		public bool ShowBitrateColumn {
			get => _ShowBitrateColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowBitrateColumn, value);
		}
		// First-results hint: "drag the Preview handle / raise the thumbnail width" (one-shot)
		bool _ResultsHintDismissed;
		[JsonPropertyName("ResultsHintDismissed")]
		public bool ResultsHintDismissed {
			get => _ResultsHintDismissed;
			set => this.RaiseAndSetIfChanged(ref _ResultsHintDismissed, value);
		}
		bool _ShowSimilarityColumn = true;
		[JsonPropertyName("ShowSimilarityColumn")]
		public bool ShowSimilarityColumn {
			get => _ShowSimilarityColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowSimilarityColumn, value);
		}
		bool _ShowSizeDateColumn = true;
		[JsonPropertyName("ShowSizeDateColumn")]
		public bool ShowSizeDateColumn {
			get => _ShowSizeDateColumn;
			set => this.RaiseAndSetIfChanged(ref _ShowSizeDateColumn, value);
		}
		ViewModels.ResultsSortMode _ResultsSortMode = ViewModels.ResultsSortMode.WastedSpace;
		[JsonPropertyName("ResultsSortMode")]
		public ViewModels.ResultsSortMode ResultsSortMode {
			get => _ResultsSortMode;
			set => this.RaiseAndSetIfChanged(ref _ResultsSortMode, value);
		}
		bool _ResultsSortDescending = true;
		[JsonPropertyName("ResultsSortDescending")]
		public bool ResultsSortDescending {
			get => _ResultsSortDescending;
			set => this.RaiseAndSetIfChanged(ref _ResultsSortDescending, value);
		}
		double _ResultsPreviewWidth = 160;
		/// <summary>Width of the Preview column in the results list; scales the preview frames.
		/// The old 480 cap made thumbnails unresizable past a quarter of a 1080p screen (#834).</summary>
		[JsonPropertyName("ResultsPreviewWidth")]
		public double ResultsPreviewWidth {
			get => _ResultsPreviewWidth;
			set => this.RaiseAndSetIfChanged(ref _ResultsPreviewWidth, Math.Clamp(value, 56, 1600));
		}
		bool _ResultsCompactRows;
		[JsonPropertyName("ResultsCompactRows")]
		public bool ResultsCompactRows {
			get => _ResultsCompactRows;
			set => this.RaiseAndSetIfChanged(ref _ResultsCompactRows, value);
		}
		ThumbnailDoubleClickAction _ThumbnailDoubleClickAction = ThumbnailDoubleClickAction.OpenFile;
		[JsonPropertyName("ThumbnailDoubleClickAction")]
		public ThumbnailDoubleClickAction ThumbnailDoubleClickAction {
			get => _ThumbnailDoubleClickAction;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailDoubleClickAction, value);
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

		bool _EnablePartialClipDetection;
		[JsonPropertyName("EnablePartialClipDetection")]
		public bool EnablePartialClipDetection {
			get => _EnablePartialClipDetection;
			set => this.RaiseAndSetIfChanged(ref _EnablePartialClipDetection, value);
		}
		double _PartialClipMinRatioPercent = 10d;
		[JsonPropertyName("PartialClipMinRatioPercent")]
		public double PartialClipMinRatioPercent {
			get => _PartialClipMinRatioPercent;
			set => this.RaiseAndSetIfChanged(ref _PartialClipMinRatioPercent, value);
		}
		double _PartialClipSimilarityThresholdPercent = 80d;
		[JsonPropertyName("PartialClipSimilarityThresholdPercent")]
		public double PartialClipSimilarityThresholdPercent {
			get => _PartialClipSimilarityThresholdPercent;
			set => this.RaiseAndSetIfChanged(ref _PartialClipSimilarityThresholdPercent, value);
		}
		bool _PartialClipRequireVisualMatch = true;
		[JsonPropertyName("PartialClipRequireVisualMatch")]
		public bool PartialClipRequireVisualMatch {
			get => _PartialClipRequireVisualMatch;
			set => this.RaiseAndSetIfChanged(ref _PartialClipRequireVisualMatch, value);
		}
		double _PartialClipVisualThresholdPercent = 85d;
		[JsonPropertyName("PartialClipVisualThresholdPercent")]
		public double PartialClipVisualThresholdPercent {
			get => _PartialClipVisualThresholdPercent;
			set => this.RaiseAndSetIfChanged(ref _PartialClipVisualThresholdPercent, value);
		}

		// Video bitrate ranks above FPS: among equal-resolution re-encodes bitrate is the
		// stronger quality signal, and a marginally higher framerate must not outrank a
		// much better encode (#839). Saved user orders are untouched.
		List<string> _QualityCriteriaOrder = ["Duration", "Resolution", "Bitrate", "FPS", "Audio Bitrate", "Size"];
		[JsonPropertyName("QualityCriteriaOrder")]
		public List<string> QualityCriteriaOrder {
			get => _QualityCriteriaOrder;
			set => this.RaiseAndSetIfChanged(ref _QualityCriteriaOrder, value);
		}

		bool _EnableScheduledScan;
		[JsonPropertyName("EnableScheduledScan")]
		public bool EnableScheduledScan {
			get => _EnableScheduledScan;
			set => this.RaiseAndSetIfChanged(ref _EnableScheduledScan, value);
		}
		string _ScheduledScanTime = "02:00";
		[JsonPropertyName("ScheduledScanTime")]
		public string ScheduledScanTime {
			get => _ScheduledScanTime;
			set => this.RaiseAndSetIfChanged(ref _ScheduledScanTime, value);
		}
		bool _NotifyOnScheduledScanComplete = true;
		[JsonPropertyName("NotifyOnScheduledScanComplete")]
		public bool NotifyOnScheduledScanComplete {
			get => _NotifyOnScheduledScanComplete;
			set => this.RaiseAndSetIfChanged(ref _NotifyOnScheduledScanComplete, value);
		}
		bool _NotifyOnScanComplete;
		[JsonPropertyName("NotifyOnScanComplete")]
		public bool NotifyOnScanComplete {
			get => _NotifyOnScanComplete;
			set => this.RaiseAndSetIfChanged(ref _NotifyOnScanComplete, value);
		}

		Dictionary<string, string> _KeyboardShortcuts = new();
		[JsonPropertyName("KeyboardShortcuts")]
		public Dictionary<string, string> KeyboardShortcuts {
			get => _KeyboardShortcuts;
			set => this.RaiseAndSetIfChanged(ref _KeyboardShortcuts, value);
		}

		public static void LoadSettings(string? path = null) {
			path ??= settingsPath;
			if ((path == null || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) && LoadOldSettings(path))
				return;

			path = ResolveSettingsPath(path);
			if (!File.Exists(path)) return;
			instance = JsonSerializer.Deserialize(File.ReadAllBytes(path), GuiJsonContext.Default.SettingsFile)
				?? throw new JsonException($"'{path}' does not contain a settings object.");
		}

		/// <summary>
		/// Set when <see cref="LoadSettingsAtStartup"/> had to fall back to default settings;
		/// the GUI shows it once the main window is up.
		/// </summary>
		[JsonIgnore]
		public static string? StartupLoadError { get; private set; }

		/// <summary>
		/// Startup counterpart of <see cref="LoadSettings"/> that never throws. An unreadable
		/// settings file (torn write during save, disk corruption) used to abort startup inside
		/// the MainWindow constructor — before any exception handler or window existed — so the
		/// app silently never opened again (#830). Keep the broken file as "*.corrupt" for
		/// diagnosis and start with default settings instead.
		/// </summary>
		public static void LoadSettingsAtStartup() {
			try {
				LoadSettings();
				StartupLoadError = null;
			}
			catch (Exception ex) {
				string message = $"Settings could not be loaded: {ex.Message}";
				string jsonPath = ResolveSettingsPath(null);
				if (File.Exists(jsonPath)) {
					try {
						File.Copy(jsonPath, jsonPath + ".corrupt", overwrite: true);
						message += $" The unreadable file was kept as '{jsonPath}.corrupt'.";
					}
					catch { /* keeping the evidence must never abort startup */ }
				}
				StartupLoadError = message;
				Logger.Instance.Error(message);
			}
		}

		static bool LoadOldSettings(string? path) {
			path ??= FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Settings.xml");
			if (!File.Exists(path)) return false;
			var xmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
			using var reader = XmlReader.Create(path, xmlSettings);
			var xDoc = XDocument.Load(reader);
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

		static string ResolveDefaultLanguageCode() => ResolveLanguageCode(null);

		static string ResolveLanguageCode(string? languageCode) {
			if (!string.IsNullOrWhiteSpace(languageCode))
				return languageCode;

			var culture = CultureInfo.CurrentUICulture;
			if (!string.IsNullOrWhiteSpace(culture.TwoLetterISOLanguageName))
				return culture.TwoLetterISOLanguageName;

			return "en";
		}
	}
}
