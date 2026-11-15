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
// The Web UI's half of "native binding on, but the FFmpeg is another version": said on the scan
// page with the way out, and gone again once it no longer applies.

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VDF.Core.FFTools;
using VDF.Core.FFTools.FFmpegNative;
using VDF.Web.Services;

namespace VDF.Web.Tests.Services;

public class FFmpegSetupServiceTests {

	internal static readonly NativeLibraryMismatch Ffmpeg8 = new(62, 63, Path.Combine(Path.GetTempPath(), "old-vdf", "bin"));

	[Fact]
	public void NativeBindingWithAnotherFfmpeg_SaysWhichAndOffersTheDownload() {
		var setup = new FFmpegSetupService(() => true, () => Ffmpeg8, inDocker: () => false);

		setup.CheckNativeBinding();

		Assert.Contains(Ffmpeg8.Folder, setup.NativeMismatchMessage);
		Assert.Contains("FFmpeg 8", setup.NativeMismatchMessage);
		Assert.Contains("FFmpeg 9", setup.NativeMismatchMessage);
		Assert.Contains("turn off the native FFmpeg binding", setup.NativeMismatchMessage);
		Assert.Equal("FFmpeg 9", setup.NativeBindingNeeds);
		// Offered wherever a build is published (Windows and Linux), never pretended elsewhere.
		Assert.Equal(FfmpegDownloader.GetDownloadPlans().Count > 0, setup.CanDownloadForNativeBinding);
	}

	[Fact]
	public void InDocker_NoDownload_TheImageIsTheWayOut() {
		var setup = new FFmpegSetupService(() => true, () => Ffmpeg8, inDocker: () => true);

		setup.CheckNativeBinding();

		Assert.False(setup.CanDownloadForNativeBinding);
		Assert.Contains("image", setup.NativeMismatchMessage);
	}

	[Fact]
	public void TurningTheBindingOff_ClearsTheNotice() {
		bool native = true;
		var setup = new FFmpegSetupService(() => native, () => Ffmpeg8, inDocker: () => false);
		setup.CheckNativeBinding();
		int changes = 0;
		setup.StateChanged += () => changes++;

		native = false;
		setup.CheckNativeBinding();

		Assert.Null(setup.NativeMismatchMessage);
		Assert.Equal(1, changes);
		setup.CheckNativeBinding(); // nothing new: no re-render
		Assert.Equal(1, changes);
	}

	[Fact]
	public void TheRightFfmpeg_IsNoNotice() {
		var setup = new FFmpegSetupService(() => true, () => null, inDocker: () => false);
		setup.CheckNativeBinding();
		Assert.Null(setup.NativeMismatchMessage);
	}
}

[Collection("WebSettingsOverride")]
public sealed class ScanPageNativeNoticeTests : BunitContext {

	readonly ScanService scan;

	public ScanPageNativeNoticeTests() {
		WebSettingsService.TestOverrideSettingsPath = Path.Combine(Path.GetTempPath(), $"VDF.WebTests.{Guid.NewGuid():N}.json");
		var webSettings = new WebSettingsService();
		scan = new ScanService(webSettings);
		Services.AddSingleton(webSettings);
		Services.AddSingleton(scan);
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	protected override void Dispose(bool disposing) {
		base.Dispose(disposing);
		scan.Dispose();
		WebSettingsService.TestOverrideSettingsPath = null;
	}

	[Fact]
	public void ScanPage_TellsAndOffersTheMatchingDownload() {
		scan.Settings.UseNativeFfmpegBinding = true;
		Services.AddSingleton(new FFmpegSetupService(() => scan.Settings.UseNativeFfmpegBinding,
			() => FFmpegSetupServiceTests.Ffmpeg8, inDocker: () => false));

		var page = Render<VDF.Web.Components.Pages.Index>();

		var banner = page.Find(".native-mismatch");
		Assert.Contains("cannot start", banner.TextContent);
		Assert.Contains("FFmpeg 8", banner.TextContent);
		if (FfmpegDownloader.GetDownloadPlans().Count > 0)
			Assert.Equal("Download FFmpeg 9", banner.QuerySelector("button")!.TextContent);
	}

	[Fact]
	public void ScanPage_NativeBindingOff_NoNotice() {
		scan.Settings.UseNativeFfmpegBinding = false;
		Services.AddSingleton(new FFmpegSetupService(() => scan.Settings.UseNativeFfmpegBinding,
			() => FFmpegSetupServiceTests.Ffmpeg8, inDocker: () => false));

		var page = Render<VDF.Web.Components.Pages.Index>();

		Assert.Empty(page.FindAll(".native-mismatch"));
	}
}
