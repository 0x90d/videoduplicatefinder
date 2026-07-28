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

using VDF.Core.FFTools;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// Tiled (Apple-style) HEIC coverage for issue #869: the photo only exists as a tile-grid
/// stream group, assembled by FFmpeg through an internal complex filtergraph. On FFmpeg
/// 8.1+ a plain -vf against that stream is rejected ("Simple and complex filtering cannot
/// be used together"), which broke every iPhone photo; and [0:v] / av_find_best_stream
/// address a single tile or an aux depth/gain-map stream, never the picture.
///
/// FFmpeg cannot WRITE tiled HEIF, so no fixture can be generated or checked in (a real
/// iPhone photo is personal data). These tests run against a real tiled HEIC supplied via
/// the VDF_TEST_TILED_HEIC environment variable and skip when it is unset.
/// </summary>
[Collection("Ffmpeg")]
public class TiledHeicTests {
	readonly FfmpegFixture _fixture;

	public TiledHeicTests(FfmpegFixture fixture) => _fixture = fixture;

	static string? TiledHeicPath {
		get {
			string? path = Environment.GetEnvironmentVariable("VDF_TEST_TILED_HEIC");
			return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
		}
	}

	const string SkipReason = "set VDF_TEST_TILED_HEIC to a tiled (iPhone) HEIC to run";

	[SkippableFact]
	public void GrayBytes_TiledHeic_ProcessMode_Succeeds() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(TiledHeicPath == null, SkipReason);

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;

		var gray = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = TiledHeicPath!,
			Position = TimeSpan.Zero,
			GrayScale = 1,
			SoftwareDecodeOnly = true,
		}, extendedLogging: true);

		Assert.NotNull(gray);
		Assert.Equal(32 * 32, gray!.Length);
	}

	[SkippableFact]
	public void CombinedGrayAndRgb_TiledHeic_Succeeds() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(TiledHeicPath == null, SkipReason);

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;

		(byte[]? gray, byte[]? rgb) = FfmpegEngine.GetGrayAndRgb224Cli(
			TiledHeicPath!, TimeSpan.Zero, softwareDecodeOnly: true, extendedLogging: true);

		Assert.NotNull(gray);
		Assert.Equal(32 * 32, gray!.Length);
		Assert.NotNull(rgb);
		Assert.Equal(VDF.Core.AI.OnnxEmbedder.InputSide * VDF.Core.AI.OnnxEmbedder.InputSide * 3, rgb!.Length);
	}

	[SkippableFact]
	public void DisplayThumbnail_TiledHeic_ReturnsValidJpeg() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(TiledHeicPath == null, SkipReason);

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;

		var jpeg = FfmpegEngine.ExtractThumbnailJpeg(TiledHeicPath!, TimeSpan.Zero);

		Assert.NotNull(jpeg);
		Assert.True(jpeg!.Length > 2);
		Assert.Equal(0xFF, jpeg[0]); // JPEG SOI marker
		Assert.Equal(0xD8, jpeg[1]);
	}

	[SkippableFact]
	public void NativeBinding_TiledHeic_RefusesSingleStreamDecode_AndCliLadderStillDelivers() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(TiledHeicPath == null, SkipReason);

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;

		// The native binding can only decode a single coded stream — one tile or an aux
		// map, never the assembled photo — so it must hand tiled HEIFs to the process path.
		bool ok = FfmpegEngine.TryGetImageInfoAndGrayBytes(TiledHeicPath!,
			out byte[]? gray, out _, out _, extendedLogging: true);
		Assert.False(ok);
		Assert.Null(gray);

		// The full ladder (native refusal -> CLI, with grid-assembly retry) still delivers.
		var cliGray = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = TiledHeicPath!,
			Position = TimeSpan.Zero,
			GrayScale = 1,
			SoftwareDecodeOnly = true,
		}, extendedLogging: true);
		Assert.NotNull(cliGray);
		Assert.Equal(32 * 32, cliGray!.Length);
	}
}
