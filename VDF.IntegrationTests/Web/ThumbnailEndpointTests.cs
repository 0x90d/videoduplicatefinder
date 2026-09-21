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

using Microsoft.AspNetCore.Http;
using VDF.Core;
using VDF.Core.FFTools;
using VDF.Core.ViewModels;
using VDF.IntegrationTests.Fixtures;
using VDF.Web.Services;

namespace VDF.IntegrationTests.Web;

/// <summary>
/// The Web UI's frame endpoints against a real video (2 seconds of testsrc2, which
/// changes with every frame): the frame served is the one at the requested "t", and
/// without a "t" it is the middle sampled frame rather than 10 percent of the file.
/// </summary>
[Collection("Ffmpeg")]
public sealed class ThumbnailEndpointTests : IDisposable {
	readonly FfmpegFixture _fixture;
	readonly FfmpegStaticStateGuard _guard = new();
	readonly string _settingsPath;
	readonly ScanService _scan;
	readonly WebSettingsService _webSettings;

	public ThumbnailEndpointTests(FfmpegFixture fixture) {
		_fixture = fixture;
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		// Never the settings file of whoever runs the tests.
		_settingsPath = Path.Combine(Path.GetTempPath(), $"VDF.WebEndpointTests.{Guid.NewGuid():N}.json");
		WebSettingsService.TestOverrideSettingsPath = _settingsPath;
		_webSettings = new WebSettingsService();
		_scan = new ScanService(_webSettings);
		_scan.Settings.ThumbnailCount = 1;
		_scan.Settings.MaxSamplingDurationSeconds = 0;
	}

	public void Dispose() {
		_scan.Dispose();
		WebSettingsService.TestOverrideSettingsPath = null;
		try { File.Delete(_settingsPath); } catch { }
		_guard.Dispose();
	}

	string SeedVideo() {
		string path = Path.GetFullPath(_fixture.H264_8bit!);
		_scan.Engine.Duplicates.Add(new DuplicateItem { Path = path, Duration = TimeSpan.FromSeconds(2) });
		return path;
	}

	static async Task<(int Status, string? ContentType, byte[] Body)> Get(Func<HttpContext, Task> endpoint, string query) {
		var ctx = new DefaultHttpContext();
		ctx.Request.QueryString = new QueryString(query);
		using var body = new MemoryStream();
		ctx.Response.Body = body;
		await endpoint(ctx);
		return (ctx.Response.StatusCode, ctx.Response.ContentType, body.ToArray());
	}

	Task<(int Status, string? ContentType, byte[] Body)> GetFull(string query) =>
		Get(ctx => ThumbnailEndpoints.Full(ctx, _scan), query);

	Task<(int Status, string? ContentType, byte[] Body)> GetHq(string query) =>
		Get(ctx => ThumbnailEndpoints.Hq(ctx, _scan, _webSettings), query);

	static byte[] Frame(string path, double seconds, int width = 0, int quality = 0) =>
		ScanEngine.ExtractThumbnailJpeg(path, TimeSpan.FromSeconds(seconds), width, quality)!;

	void SkipWithoutVideo() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");
	}

	[SkippableFact]
	public async Task Full_ServesTheFrameAtTheRequestedSecond() {
		SkipWithoutVideo();
		string path = SeedVideo();
		string file = Uri.EscapeDataString(path);

		var early = await GetFull($"?path={file}&t=0.20");
		var late = await GetFull($"?path={file}&t=1.50");

		Assert.Equal(200, early.Status);
		Assert.Equal("image/jpeg", early.ContentType);
		Assert.Equal(Frame(path, 0.2), early.Body);
		Assert.Equal(Frame(path, 1.5), late.Body);
		Assert.NotEqual(early.Body, late.Body); // the fixture really differs between the two
	}

	[SkippableFact]
	public async Task Full_WithoutAPosition_ServesTheMiddleSampledFrame() {
		SkipWithoutVideo();
		string path = SeedVideo();

		var response = await GetFull($"?path={Uri.EscapeDataString(path)}");

		// One sampled frame = the midpoint. It used to be 10 percent of the file (0.2s here),
		// whatever had been sampled.
		Assert.Equal(Frame(path, 1.0), response.Body);
		Assert.NotEqual(Frame(path, 0.2), response.Body);
	}

	[SkippableFact]
	public async Task Full_SteppingBackServesTheSameBytesAgain() {
		SkipWithoutVideo();
		string path = SeedVideo();
		string file = Uri.EscapeDataString(path);

		var first = await GetFull($"?path={file}&t=0.20");
		await GetFull($"?path={file}&t=1.50");
		var again = await GetFull($"?path={file}&t=0.20");

		// The cache is keyed by position: the second frame must not answer for the first.
		Assert.Equal(first.Body, again.Body);
		Assert.Equal(2, _scan.FullThumbCache.Count);
	}

	[SkippableFact]
	public async Task Hq_ServesTheFrameAtTheRequestedSecond_InTheRequestedSize() {
		SkipWithoutVideo();
		string path = SeedVideo();
		string file = Uri.EscapeDataString(path);

		var early = await GetHq($"?path={file}&w=160&q=80&t=0.20");
		var late = await GetHq($"?path={file}&w=160&q=80&t=1.50");
		var byDefault = await GetHq($"?path={file}&w=160&q=80");

		Assert.Equal(200, early.Status);
		Assert.Equal("image/jpeg", early.ContentType);
		Assert.Equal(Frame(path, 0.2, 160, 80), early.Body);
		Assert.Equal(Frame(path, 1.5, 160, 80), late.Body);
		Assert.Equal(Frame(path, 1.0, 160, 80), byDefault.Body);
	}

	[Fact]
	public async Task OnlyFilesOfTheCurrentResultsAreServed() {
		var unknown = await GetFull($"?path={Uri.EscapeDataString(Path.Combine(Path.GetTempPath(), "not-a-result.mp4"))}&t=1");
		var missing = await GetHq("?t=1");

		Assert.Equal(404, unknown.Status);
		Assert.Equal(400, missing.Status);
		Assert.Empty(unknown.Body);
	}
}
