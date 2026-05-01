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

using VDF.CLI.Commands;

namespace VDF.CLI.Tests.Commands;

/// <summary>
/// Pins JSON deserialization of the CLI's <c>--settings</c> file. Most members of
/// <c>VDF.Core.Settings</c> are public fields rather than properties, so before the
/// fix System.Text.Json (fields-off by default) silently dropped them — including
/// load-bearing ones like <c>ThumbnailCount</c> and <c>MaxSamplingDurationSeconds</c>.
/// These tests catch a regression of either removing <c>IncludeFields = true</c> or
/// converting a setting from field to property without verifying the round-trip.
/// </summary>
public class ScanRunnerSettingsTests : IDisposable {
	readonly string _tempPath;

	public ScanRunnerSettingsTests() {
		_tempPath = Path.Combine(Path.GetTempPath(), $"vdf_settings_{Guid.NewGuid():N}.json");
	}

	public void Dispose() {
		try { File.Delete(_tempPath); } catch { }
	}

	[Fact]
	public void LoadOrCreateSettings_NullFile_ReturnsDefaults() {
		var s = ScanRunner.LoadOrCreateSettings(null);

		Assert.Equal(1, s.ThumbnailCount);
		Assert.Equal(0d, s.MaxSamplingDurationSeconds);
		Assert.False(s.IgnoreReadOnlyFolders);
		Assert.Empty(s.IncludeList);
	}

	[Fact]
	public void LoadOrCreateSettings_NonExistentFile_ReturnsDefaults() {
		var fi = new FileInfo(Path.Combine(Path.GetTempPath(), $"vdf_does_not_exist_{Guid.NewGuid():N}.json"));

		var s = ScanRunner.LoadOrCreateSettings(fi);

		Assert.Equal(1, s.ThumbnailCount);
		Assert.False(s.IgnoreReadOnlyFolders);
	}

	[Fact]
	public void LoadOrCreateSettings_MalformedJson_ReturnsDefaultsWithoutThrowing() {
		File.WriteAllText(_tempPath, "{ not valid json");

		var s = ScanRunner.LoadOrCreateSettings(new FileInfo(_tempPath));

		// Defaults preserved; warning is written to stderr by LoadOrCreateSettings.
		Assert.Equal(1, s.ThumbnailCount);
		Assert.False(s.IgnoreReadOnlyFolders);
	}

	[Fact]
	public void LoadOrCreateSettings_FieldTypedMembers_AreDeserialized() {
		// Every member named here is a public *field* on Settings, not a property.
		// Without IncludeFields=true System.Text.Json would skip them silently.
		File.WriteAllText(_tempPath, """
		{
		  "ThumbnailCount": 7,
		  "ThumbnailMaxWidth": 250,
		  "MaxDegreeOfParallelism": 4,
		  "MaxSamplingDurationSeconds": 300.5,
		  "IgnoreReadOnlyFolders": true,
		  "ExcludeHardLinks": true,
		  "AlwaysRetryFailedSampling": true,
		  "Threshhold": 8,
		  "Percent": 92.5,
		  "PercentDurationDifference": 15.0,
		  "DurationDifferenceMinSeconds": 2.0,
		  "DurationDifferenceMaxSeconds": 10.0,
		  "DatabaseCheckpointIntervalMinutes": 10,
		  "MinimumFileSize": 1024,
		  "MaximumFileSize": 1048576,
		  "FilterByFileSize": true,
		  "EnablePartialClipDetection": true,
		  "PartialClipMinRatio": 0.25,
		  "PartialClipSimilarityThreshold": 0.75,
		  "LanguageCode": "de",
		  "CustomFFArguments": "-foo bar",
		  "FilePathContainsTexts": ["alpha", "beta"]
		}
		""");

		var s = ScanRunner.LoadOrCreateSettings(new FileInfo(_tempPath));

		Assert.Equal(7, s.ThumbnailCount);
		Assert.Equal(250, s.ThumbnailMaxWidth);
		Assert.Equal(4, s.MaxDegreeOfParallelism);
		Assert.Equal(300.5, s.MaxSamplingDurationSeconds);
		Assert.True(s.IgnoreReadOnlyFolders);
		Assert.True(s.ExcludeHardLinks);
		Assert.True(s.AlwaysRetryFailedSampling);
		Assert.Equal(8, s.Threshhold);
		Assert.Equal(92.5f, s.Percent);
		Assert.Equal(15.0, s.PercentDurationDifference);
		Assert.Equal(2.0, s.DurationDifferenceMinSeconds);
		Assert.Equal(10.0, s.DurationDifferenceMaxSeconds);
		Assert.Equal(10, s.DatabaseCheckpointIntervalMinutes);
		Assert.Equal(1024, s.MinimumFileSize);
		Assert.Equal(1048576, s.MaximumFileSize);
		Assert.True(s.FilterByFileSize);
		Assert.True(s.EnablePartialClipDetection);
		Assert.Equal(0.25, s.PartialClipMinRatio);
		Assert.Equal(0.75, s.PartialClipSimilarityThreshold);
		Assert.Equal("de", s.LanguageCode);
		Assert.Equal("-foo bar", s.CustomFFArguments);
		Assert.Equal(new[] { "alpha", "beta" }, s.FilePathContainsTexts);
	}

	[Fact]
	public void LoadOrCreateSettings_PartialJson_LeavesUnsetFieldsAtDefault() {
		// JSON only sets one field; everything else must keep its initialized default.
		File.WriteAllText(_tempPath, "{ \"ThumbnailCount\": 12 }");

		var s = ScanRunner.LoadOrCreateSettings(new FileInfo(_tempPath));

		Assert.Equal(12, s.ThumbnailCount);
		Assert.Equal(100, s.ThumbnailMaxWidth);              // default
		Assert.Equal(1, s.MaxDegreeOfParallelism);           // default
		Assert.Equal((byte)5, s.Threshhold);                 // default
		Assert.Equal(96f, s.Percent);                        // default
		Assert.True(s.IncludeSubDirectories);                // default true
		Assert.True(s.IncludeImages);                        // default true
	}

	[Fact]
	public void LoadOrCreateSettings_CollectionMembers_AreDeserialized() {
		// IncludeList/BlackList are HashSet properties; FilePathContainsTexts/
		// FilePathNotContainsTexts are List fields. All four were silently empty
		// before this PR — HashSets needed `{ get; set; }` (STJ won't repopulate
		// read-only collection properties) and Lists needed IncludeFields=true.
		File.WriteAllText(_tempPath, """
		{
		  "IncludeList": ["C:/videos", "D:/more"],
		  "BlackList": ["C:/videos/skip"],
		  "FilePathContainsTexts": ["foo", "bar"],
		  "FilePathNotContainsTexts": ["baz"]
		}
		""");

		var s = ScanRunner.LoadOrCreateSettings(new FileInfo(_tempPath));

		Assert.Equal(2, s.IncludeList.Count);
		Assert.Contains("C:/videos", s.IncludeList);
		Assert.Contains("D:/more", s.IncludeList);
		Assert.Single(s.BlackList);
		Assert.Contains("C:/videos/skip", s.BlackList);
		Assert.Equal(new[] { "foo", "bar" }, s.FilePathContainsTexts);
		Assert.Equal(new[] { "baz" }, s.FilePathNotContainsTexts);
	}

	[Fact]
	public void LoadOrCreateSettings_CaseInsensitivePropertyNames() {
		// PropertyNameCaseInsensitive=true; users hand-editing the JSON shouldn't
		// have to match the exact field casing for it to work.
		File.WriteAllText(_tempPath, "{ \"thumbnailcount\": 5, \"INCLUDESUBDIRECTORIES\": false }");

		var s = ScanRunner.LoadOrCreateSettings(new FileInfo(_tempPath));

		Assert.Equal(5, s.ThumbnailCount);
		Assert.False(s.IncludeSubDirectories);
	}
}
