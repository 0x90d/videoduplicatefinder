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
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

[Collection("Ffmpeg")]
public class CodecCoverageTests {
	readonly FfmpegFixture _fixture;

	public CodecCoverageTests(FfmpegFixture fixture) => _fixture = fixture;

	public static IEnumerable<object[]> CodecNames => new[] {
		new object[] { "H264_8bit" },
		new object[] { "HEVC_10bit" },
		new object[] { "VP9" },
	};

	string? GetVideoPath(string codecName) => codecName switch {
		"H264_8bit" => _fixture.H264_8bit,
		"HEVC_10bit" => _fixture.HEVC_10bit,
		"VP9" => _fixture.VP9,
		_ => null,
	};

	[SkippableTheory]
	[MemberData(nameof(CodecNames))]
	public void GrayBytes_ProcessMode_ReturnsValidData(string codecName) {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		string? path = GetVideoPath(codecName);
		Skip.If(path == null, $"{codecName} test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = path!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 1,
		}, extendedLogging: false);

		Assert.NotNull(result);
		Assert.Equal(1024, result.Length);
		Assert.True(GrayBytesUtils.VerifyGrayScaleValues(result),
			$"{codecName}: graybytes too dark (failed VerifyGrayScaleValues)");
	}

	[SkippableTheory]
	[MemberData(nameof(CodecNames))]
	public void GrayBytes_NativeMode_ReturnsValidData(string codecName) {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		string? path = GetVideoPath(codecName);
		Skip.If(path == null, $"{codecName} test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = path!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 1,
		}, extendedLogging: false);

		Assert.NotNull(result);
		Assert.Equal(1024, result.Length);
		Assert.True(GrayBytesUtils.VerifyGrayScaleValues(result),
			$"{codecName}: native graybytes too dark (failed VerifyGrayScaleValues)");
	}

	// JPEG thumbnail path (GrayScale = 0). This exercises JpegFrameEncoder, which the
	// graybytes tests above never touch — the gap that let the native MJPEG encoder ship
	// broken on FFmpeg 8.x (issue #795: 'Invalid argument' in JpegFrameEncoder.Encode).
	static void AssertValidJpeg(byte[]? jpeg, string label) {
		Assert.NotNull(jpeg);
		Assert.True(jpeg!.Length > 4, $"{label}: JPEG too small ({jpeg.Length} bytes)");
		Assert.True(jpeg[0] == 0xFF && jpeg[1] == 0xD8, $"{label}: missing JPEG SOI marker");
		Assert.True(jpeg[^2] == 0xFF && jpeg[^1] == 0xD9, $"{label}: missing JPEG EOI marker");
	}

	[SkippableTheory]
	[MemberData(nameof(CodecNames))]
	public void ThumbnailJpeg_ProcessMode_ReturnsValidJpeg(string codecName) {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		string? path = GetVideoPath(codecName);
		Skip.If(path == null, $"{codecName} test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var jpeg = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = path!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 0,
			MaxWidth = 100,
		}, extendedLogging: false);

		AssertValidJpeg(jpeg, $"{codecName} process-mode JPEG");
	}

	// NOTE: a GetThumbnail/EncodeJpegFromBgra "native" test would NOT guard the native
	// encoder — both silently fall back to the FFmpeg process on native failure, so the
	// test passes either way. The real native-encode regression test lives in
	// JpegFrameEncoderTests, which calls JpegFrameEncoder.Encode directly (no fallback).

	[SkippableTheory]
	[MemberData(nameof(CodecNames))]
	public void GrayBytes_ProcessMode_IsDeterministic(string codecName) {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		string? path = GetVideoPath(codecName);
		Skip.If(path == null, $"{codecName} test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var settings = new FfmpegSettings {
			File = path!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 1,
		};

		var run1 = FfmpegEngine.GetThumbnail(settings, extendedLogging: false);
		var run2 = FfmpegEngine.GetThumbnail(settings, extendedLogging: false);

		Assert.NotNull(run1);
		Assert.NotNull(run2);
		Assert.Equal(run1, run2);
	}
}
