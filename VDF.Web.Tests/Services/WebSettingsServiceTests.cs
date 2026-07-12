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

using VDF.Core;
using VDF.Web.Services;

namespace VDF.Web.Tests.Services;

// All classes in this collection run sequentially: they share the static
// WebSettingsService.TestOverrideSettingsPath hook.
[Collection("WebSettingsOverride")]
public sealed class WebSettingsServiceTests : IDisposable {
	readonly string settingsPath;

	public WebSettingsServiceTests() {
		settingsPath = Path.Combine(Path.GetTempPath(), $"VDF.WebTests.{Guid.NewGuid():N}.json");
		WebSettingsService.TestOverrideSettingsPath = settingsPath;
	}

	public void Dispose() {
		WebSettingsService.TestOverrideSettingsPath = null;
		try { File.Delete(settingsPath); } catch { }
	}

	[Fact]
	public void SaveThenLoad_RoundTripsCoreAndWebOnlySettings() {
		var service = new WebSettingsService {
			AutoLoadThumbnails = false,
			ThumbnailWidth = 640,
			ThumbnailJpegQuality = 42,
		};
		var settings = new Settings {
			Percent = 87f,
			UseAiMatching = true,
			AiPercent = 96f,
			EnableAiPartialDetection = true,
			AiPartialHitPercent = 91f,
			UsePHashing = true,
			ThumbnailCount = 3,
		};
		settings.IncludeList.Add(@"C:\videos");
		settings.BlackList.Add(@"C:\videos\skip");

		Assert.True(service.Save(settings));
		Assert.True(File.Exists(settingsPath));

		var reloadedService = new WebSettingsService();
		var reloaded = new Settings();
		Assert.True(reloadedService.Load(reloaded));

		Assert.Equal(87f, reloaded.Percent);
		Assert.True(reloaded.UseAiMatching);
		Assert.Equal(96f, reloaded.AiPercent);
		Assert.True(reloaded.EnableAiPartialDetection);
		Assert.Equal(91f, reloaded.AiPartialHitPercent);
		Assert.True(reloaded.UsePHashing);
		Assert.Equal(3, reloaded.ThumbnailCount);
		Assert.Contains(@"C:\videos", reloaded.IncludeList);
		Assert.Contains(@"C:\videos\skip", reloaded.BlackList);
		Assert.False(reloadedService.AutoLoadThumbnails);
		Assert.Equal(640, reloadedService.ThumbnailWidth);
		Assert.Equal(42, reloadedService.ThumbnailJpegQuality);
	}

	[Fact]
	public void Load_ClampsHandEditedOutOfRangeValues() {
		// A cosine fraction instead of a percent (the exact hand-edit the clamp
		// exists for) plus web-only values far out of range.
		File.WriteAllText(settingsPath,
			"""{"AiPercent":0.94,"AiPartialHitPercent":0.89,"ThumbnailWidth":5000,"ThumbnailJpegQuality":1}""");

		var service = new WebSettingsService();
		var settings = new Settings();
		Assert.True(service.Load(settings));

		Assert.Equal(50f, settings.AiPercent);
		Assert.Equal(70f, settings.AiPartialHitPercent);
		Assert.Equal(960, service.ThumbnailWidth);
		Assert.Equal(10, service.ThumbnailJpegQuality);
	}

	[Fact]
	public void Load_MissingOrCorruptFile_ReturnsFalseAndLeavesDefaults() {
		var service = new WebSettingsService();
		var settings = new Settings();
		Assert.False(service.Load(settings)); // no file

		File.WriteAllText(settingsPath, "{ this is not json");
		Assert.False(service.Load(settings)); // corrupt file
		Assert.Equal(96f, settings.Percent); // Core default untouched
	}
}
