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
		static SettingsFile? instance;
		static string? settingsPath;

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
		[JsonPropertyName("LastCustomSelectExpression")]
		public string LastCustomSelectExpression {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = string.Empty;

		[JsonPropertyName("ExpressionHistory")]
		public ObservableCollection<string> ExpressionHistory {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new();

		[JsonPropertyName("ExpressionPresets")]
		public ObservableCollection<ExpressionPreset> ExpressionPresets {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new();

		[JsonPropertyName("CustomSelectionPresets")]
		public ObservableCollection<CustomSelectionPreset> CustomSelectionPresets {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new();

		[JsonPropertyName("AutoApplySelectionPresetEnabled")]
		public bool AutoApplySelectionPresetEnabled {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		/// <summary>Name of the custom-selection preset applied automatically after every scan.</summary>
		[JsonPropertyName("AutoApplySelectionPreset")]
		public string AutoApplySelectionPreset {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = string.Empty;

		[JsonPropertyName("MainWindowWidth")]
		public double? MainWindowWidth {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("MainWindowHeight")]
		public double? MainWindowHeight {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("MainWindowPositionX")]
		public int? MainWindowPositionX {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("MainWindowPositionY")]
		public int? MainWindowPositionY {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("MainWindowMaximized")]
		public bool MainWindowMaximized {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("LanguageCode")]
		public string LanguageCode {
			get;
			set => this.RaiseAndSetIfChanged(ref field, ResolveLanguageCode(value));
		} = ResolveDefaultLanguageCode();
		[JsonPropertyName("IgnoreReadOnlyFolders")]
		public bool IgnoreReadOnlyFolders {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ExcludeHardLinks")]
		public bool ExcludeHardLinks {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("IgnoreReparsePoints")]
		public bool IgnoreReparsePoints {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		// IgnoreBlackPixels/IgnoreWhitePixels/CompareHorizontallyFlipped/Percent defaults
		// form the "Edited & altered copies" scan profile — the recommended default for
		// fresh installs (redesign stage 2). Existing settings files carry explicit
		// values for every key, so nobody's configuration changes.
		[JsonPropertyName("IgnoreBlackPixels")]
		public bool IgnoreBlackPixels {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("IgnoreWhitePixels")]
		public bool IgnoreWhitePixels {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("MaxDegreeOfParallelism")]
		public int MaxDegreeOfParallelism {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = -1;
		/// <summary>Worker cap for the CPU-bound matching phases; 0 or less = automatic CPU-headroom cap — see Core setting.</summary>
		[JsonPropertyName("MatchingMaxDegreeOfParallelism")]
		public int MatchingMaxDegreeOfParallelism {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		/// <summary>Per-drive cap for slow drives (spindle HDDs / network shares) — see Core setting.</summary>
		[JsonPropertyName("HddMaxDegreeOfParallelism")]
		public int HddMaxDegreeOfParallelism {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 2;
		/// <summary>Drive root → "SSD"/"HDD" scan-concurrency overrides. No editor UI yet
		/// (planned with the per-drive scan rows); power users can edit Settings.json.</summary>
		[JsonPropertyName("DriveTypeOverrides")]
		public Dictionary<string, string> DriveTypeOverrides {
			get;
			// STJ drops the comparer on deserialization — re-wrap so drive letters stay case-insensitive.
			set => field = value == null
				? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
		} = new(StringComparer.OrdinalIgnoreCase);
		/// <summary>The Setup screen's "New here?" hint strip, dismissible once.</summary>
		[JsonPropertyName("WelcomeStripDismissed")]
		public bool WelcomeStripDismissed {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		/// <summary>Snapshot of the profile-managed knobs from when the user last left a
		/// custom configuration; selecting the Custom profile restores it.</summary>
		[JsonPropertyName("CustomScanKnobs")]
		public ScanKnobs? CustomScanKnobs {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("HardwareAccelerationMode")]
		public Core.FFTools.FFHardwareAccelerationMode HardwareAccelerationMode {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = Core.FFTools.FFHardwareAccelerationMode.auto;
		// Part of the "Edited & altered copies" default profile (see IgnoreBlackPixels note).
		[JsonPropertyName("CompareHorizontallyFlipped")]
		public bool CompareHorizontallyFlipped {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("IncludeSubDirectories")]
		public bool IncludeSubDirectories {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("IncludeImages")]
		public bool IncludeImages {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("GeneratePreviewThumbnails")]
		public bool GeneratePreviewThumbnails {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("ThumbnailMaxWidth")]
		public int ThumbnailMaxWidth {
			get;
			set {
				int clamped = Math.Clamp(value, 48, 960);
				if (clamped == field) return;
				this.RaiseAndSetIfChanged(ref field, clamped);
				// The old view sized its layout from this extraction width; keep that
				// behavior by moving the results Preview column along. The drag grip can
				// still diverge afterwards (a persisted ResultsPreviewWidth is loaded
				// after this property and wins on startup).
				ResultsPreviewWidth = clamped;
			}
		} = 100;
		[JsonPropertyName("ExtendedFFToolsLogging")]
		public bool ExtendedFFToolsLogging {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("LogExcludedFiles")]
		public bool LogExcludedFiles {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("AlwaysRetryFailedSampling")]
		public bool AlwaysRetryFailedSampling {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;
		[JsonPropertyName("UseNativeFfmpegBinding")]
		public bool UseNativeFfmpegBinding {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("CustomFFArguments")]
		public string CustomFFArguments {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = string.Empty;

		[JsonPropertyName("BackupAfterListChanged")]
		public bool BackupAfterListChanged {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("AskToSaveResultsOnExit")]
		public bool AskToSaveResultsOnExit {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("IncludeNonExistingFiles")]
		public bool IncludeNonExistingFiles {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("RememberDeletedContent")]
		public bool RememberDeletedContent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("AutoCheckDeletedContentMatches")]
		public bool AutoCheckDeletedContentMatches {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ScanAgainstEntireDatabase")]
		public bool ScanAgainstEntireDatabase {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("FolderMatchMode")]
		public Core.FolderMatchMode FolderMatchMode {
			get;
			set {
				this.RaiseAndSetIfChanged(ref field, value);
				this.RaisePropertyChanged(nameof(IsFolderMatchModeActive));
			}
		}
		public bool IsFolderMatchModeActive => FolderMatchMode != Core.FolderMatchMode.None;
		[JsonPropertyName("SameFolderDepth")]
		public int SameFolderDepth {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1;
		[JsonPropertyName("UsePHash")]
		public bool UsePHash {
			get;
			set {
				this.RaiseAndSetIfChanged(ref field, value);
				this.RaisePropertyChanged(nameof(PHashComparisonActive));
			}
		}
		/// <summary>#842: run grayscale AND pHash in one comparison pass and badge which algorithm found each group. Takes precedence over UsePHash.</summary>
		[JsonPropertyName("CombineGrayPHash")]
		public bool CombineGrayPHash {
			get;
			set {
				this.RaiseAndSetIfChanged(ref field, value);
				this.RaisePropertyChanged(nameof(PHashComparisonActive));
			}
		}
		/// <summary>The pHash comparison (and its sample-quorum setting) is in play - alone or combined.</summary>
		[JsonIgnore]
		public bool PHashComparisonActive => UsePHash || CombineGrayPHash;
		/// <summary>Percentage of sampled frame positions that must individually pass the pHash threshold — see Core's PHashRequiredMatchingSampleRatio (0..1).</summary>
		[JsonPropertyName("PHashSampleRatioPercent")]
		public float PHashSampleRatioPercent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, Math.Clamp(value, 1f, 100f));
		} = 60f;
		[JsonPropertyName("UseAiMatching")]
		public bool UseAiMatching {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		/// <summary>Similarity threshold (percent = cosine·100) for the AI matching pass.</summary>
		[JsonPropertyName("AiPercent")]
		public float AiPercent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, Math.Clamp(value, 50f, 100f));
		} = 94f;
		[JsonPropertyName("EnableAiPartialDetection")]
		public bool EnableAiPartialDetection {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		/// <summary>Per-frame hit threshold (percent) for the visual partial-duplicate pass.</summary>
		[JsonPropertyName("AiPartialHitPercent")]
		public float AiPartialHitPercent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, Math.Clamp(value, 70f, 99f));
		} = 89f;
		/// <summary>GUI mirror of Core Settings.NeedsAiComponents — keep the two in sync.</summary>
		[JsonIgnore]
		public bool NeedsAiComponents => UseAiMatching || EnableAiPartialDetection;
		[JsonPropertyName("UseExifCreationDate")]
		public bool UseExifCreationDate {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		// Part of the "Edited & altered copies" default profile (see IgnoreBlackPixels note).
		[JsonPropertyName("Percent")]
		public float Percent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 92f;
		[JsonPropertyName("PercentDurationDifference")]
		public double PercentDurationDifference {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 20d;
		[JsonPropertyName("DurationDifferenceMinSeconds")]
		public int DurationDifferenceMinSeconds {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("DurationDifferenceMaxSeconds")]
		public int DurationDifferenceMaxSeconds {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("MaxSamplingDurationSeconds")]
		public int MaxSamplingDurationSeconds {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("Thumbnails")]
		public int Thumbnails {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 1;
		[JsonPropertyName("CustomCommands")]
		public CustomActionCommands CustomCommands { get; set; } = new();
		[JsonPropertyName("CustomDatabaseFolder")]
		public string CustomDatabaseFolder {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = string.Empty;
		[JsonPropertyName("DatabaseCheckpointIntervalMinutes")]
		public int DatabaseCheckpointIntervalMinutes {
			get;
			set => this.RaiseAndSetIfChanged(ref field, Math.Max(0, value));
		} = 5;

		public static void SaveSettings(string? path = null) {
			path = ResolveSettingsPath(path);
			File.WriteAllText(path, JsonSerializer.Serialize(instance, GuiJsonContext.Default.SettingsFile));
		}

		[JsonPropertyName("UseMica")]
		public bool UseMica {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = false;
		[JsonPropertyName("DarkMode")]
		public bool DarkMode {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("ThumbnailComparerWindowWidth")]
		public double? ThumbnailComparerWindowWidth {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ThumbnailComparerWindowHeight")]
		public double? ThumbnailComparerWindowHeight {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ThumbnailComparerWindowPositionX")]
		public double? ThumbnailComparerWindowPositionX {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ThumbnailComparerWindowPositionY")]
		public double? ThumbnailComparerWindowPositionY {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ThumbnailComparerWindowScreenIndex")]
		public int? ThumbnailComparerWindowScreenIndex {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		// Side-by-side first (redesign stage 4, maintainer directive on comparer view modes).
		[JsonPropertyName("ThumbnailComparerMode")]
		public CompareMode ThumbnailComparerMode {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = CompareMode.SideBySide;
		[JsonPropertyName("ThumbnailComparerDiffSensitivity")]
		public double ThumbnailComparerDiffSensitivity {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0.5;
		[JsonPropertyName("ThumbnailComparerHighlightDifferences")]
		public bool ThumbnailComparerHighlightDifferences {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ShowThumbnailColumn")]
		public bool ShowThumbnailColumn {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("ShowDurationColumn")]
		public bool ShowDurationColumn {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("ShowFormatColumn")]
		public bool ShowFormatColumn {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("ShowBitrateColumn")]
		public bool ShowBitrateColumn {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		// First-results hint: "drag the Preview handle / raise the thumbnail width" (one-shot)
		[JsonPropertyName("ResultsHintDismissed")]
		public bool ResultsHintDismissed {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ShowSimilarityColumn")]
		public bool ShowSimilarityColumn {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("ShowSizeDateColumn")]
		public bool ShowSizeDateColumn {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("ResultsSortMode")]
		public ViewModels.ResultsSortMode ResultsSortMode {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = ViewModels.ResultsSortMode.WastedSpace;
		[JsonPropertyName("ResultsSortDescending")]
		public bool ResultsSortDescending {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		/// <summary>Show the BEST-badged file at the top of each group, ahead of the sort order (#846).</summary>
		[JsonPropertyName("ResultsBestFirst")]
		public bool ResultsBestFirst {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		/// <summary>Width of the Preview column in the results list; scales the preview frames.
		/// The old 480 cap made thumbnails unresizable past a quarter of a 1080p screen (#834).</summary>
		[JsonPropertyName("ResultsPreviewWidth")]
		public double ResultsPreviewWidth {
			get;
			set => this.RaiseAndSetIfChanged(ref field, Math.Clamp(value, 56, 1600));
		} = 160;
		[JsonPropertyName("ResultsCompactRows")]
		public bool ResultsCompactRows {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ThumbnailDoubleClickAction")]
		public ThumbnailDoubleClickAction ThumbnailDoubleClickAction {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = ThumbnailDoubleClickAction.OpenFile;
		[JsonPropertyName("FilterByFilePathContains")]
		public bool FilterByFilePathContains {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("FilePathContainsTexts")]
		public ObservableCollection<string> FilePathContainsTexts {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new();
		[JsonPropertyName("FilterByFilePathNotContains")]
		public bool FilterByFilePathNotContains {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("FilePathNotContainsTexts")]
		public ObservableCollection<string> FilePathNotContainsTexts {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new();
		[JsonPropertyName("FilterByFileSize")]
		public bool FilterByFileSize {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("MaximumFileSize")]
		public int MaximumFileSize {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 999999999;
		[JsonPropertyName("MinimumFileSize")]
		public int MinimumFileSize {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 0;

		[JsonPropertyName("EnablePartialClipDetection")]
		public bool EnablePartialClipDetection {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("PartialClipMinRatioPercent")]
		public double PartialClipMinRatioPercent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 10d;
		[JsonPropertyName("PartialClipSimilarityThresholdPercent")]
		public double PartialClipSimilarityThresholdPercent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 80d;
		[JsonPropertyName("PartialClipRequireVisualMatch")]
		public bool PartialClipRequireVisualMatch {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("PartialClipVisualThresholdPercent")]
		public double PartialClipVisualThresholdPercent {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = 85d;

		// Video bitrate ranks above FPS: among equal-resolution re-encodes bitrate is the
		// stronger quality signal, and a marginally higher framerate must not outrank a
		// much better encode (#839). Saved user orders are untouched.
		[JsonPropertyName("QualityCriteriaOrder")]
		public List<string> QualityCriteriaOrder {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = ["Duration", "Resolution", "Bitrate", "FPS", "Bits per pixel", "Audio Bitrate", "Size"];

		[JsonPropertyName("EnableScheduledScan")]
		public bool EnableScheduledScan {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}
		[JsonPropertyName("ScheduledScanTime")]
		public string ScheduledScanTime {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = "02:00";
		[JsonPropertyName("NotifyOnScheduledScanComplete")]
		public bool NotifyOnScheduledScanComplete {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = true;
		[JsonPropertyName("NotifyOnScanComplete")]
		public bool NotifyOnScanComplete {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		}

		[JsonPropertyName("KeyboardShortcuts")]
		public Dictionary<string, string> KeyboardShortcuts {
			get;
			set => this.RaiseAndSetIfChanged(ref field, value);
		} = new();

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
