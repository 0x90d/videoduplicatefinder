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
public class GrayBytesParityTests {
	readonly FfmpegFixture _fixture;

	public GrayBytesParityTests(FfmpegFixture fixture) => _fixture = fixture;

	byte[]? ExtractGrayBytes(string file, bool native) {
		FfmpegEngine.UseNativeBinding = native;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		return FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = file,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 1,
		}, extendedLogging: false);
	}

	[SkippableFact]
	public void GrayBytes_NativeVsProcess_H264_ProduceSimilarResults() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		var nativeBytes = ExtractGrayBytes(_fixture.H264_8bit!, native: true);
		var processBytes = ExtractGrayBytes(_fixture.H264_8bit!, native: false);

		Assert.NotNull(nativeBytes);
		Assert.NotNull(processBytes);
		Assert.Equal(1024, nativeBytes.Length);
		Assert.Equal(1024, processBytes.Length);

		float diff = GrayBytesUtils.PercentageDifference(nativeBytes, processBytes);
		Assert.True(diff < 0.05f,
			$"Native vs process graybytes differ by {diff:P2}, expected < 5%");
	}

	[SkippableFact]
	public void GrayBytes_NativeVsProcess_HEVC10bit_ProduceSimilarResults() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.HEVC_10bit == null, "HEVC 10-bit test video not generated (libx265 unavailable?)");

		using var guard = new FfmpegStaticStateGuard();
		var nativeBytes = ExtractGrayBytes(_fixture.HEVC_10bit!, native: true);
		var processBytes = ExtractGrayBytes(_fixture.HEVC_10bit!, native: false);

		Assert.NotNull(nativeBytes);
		Assert.NotNull(processBytes);
		Assert.Equal(1024, nativeBytes.Length);
		Assert.Equal(1024, processBytes.Length);

		float diff = GrayBytesUtils.PercentageDifference(nativeBytes, processBytes);
		Assert.True(diff < 0.05f,
			$"Native vs process graybytes differ by {diff:P2} for 10-bit HEVC, expected < 5%");
	}

	[SkippableFact]
	public void GrayBytes_ProcessMode_SameInput_ProducesIdenticalOutput() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		var run1 = ExtractGrayBytes(_fixture.H264_8bit!, native: false);
		var run2 = ExtractGrayBytes(_fixture.H264_8bit!, native: false);

		Assert.NotNull(run1);
		Assert.NotNull(run2);
		Assert.Equal(run1, run2);
	}

	[SkippableTheory]
	[InlineData(false)] // JPEG (MJPEG): buffers its single frame, requires draining
	[InlineData(true)]  // PNG: emits immediately, guards against a seek regression
	public void GrayBytes_NativeStillImage_DecodesAndMatchesProcess(bool png) {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		string? image = png ? _fixture.SamplePng : _fixture.SampleJpeg;
		Skip.If(image == null, "Still image fixture not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		// Native still-image fast path. Before the #810 fix this returned false for MJPEG
		// (the lone intra frame was never received), silently falling back to the CLI.
		FfmpegEngine.UseNativeBinding = true;
		bool ok = FfmpegEngine.TryGetImageInfoAndGrayBytes(image!, out var nativeBytes, out int w, out int h, extendedLogging: false);
		Assert.True(ok, "Native still-image decode failed");
		Assert.NotNull(nativeBytes);
		Assert.Equal(1024, nativeBytes!.Length);
		Assert.True(w > 0 && h > 0, $"Native decode reported invalid dimensions {w}x{h}");

		FfmpegEngine.UseNativeBinding = false;
		var processBytes = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = image!, Position = TimeSpan.Zero, GrayScale = 1, SoftwareDecodeOnly = true,
		}, extendedLogging: false);
		Assert.NotNull(processBytes);

		float diff = GrayBytesUtils.PercentageDifference(nativeBytes, processBytes!);
		Assert.True(diff < 0.05f, $"Native vs process still-image graybytes differ by {diff:P2}, expected < 5%");
	}

	[SkippableFact]
	public void GrayBytes_NativeMode_SameInput_ProducesIdenticalOutput() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		var run1 = ExtractGrayBytes(_fixture.H264_8bit!, native: true);
		var run2 = ExtractGrayBytes(_fixture.H264_8bit!, native: true);

		Assert.NotNull(run1);
		Assert.NotNull(run2);
		Assert.Equal(run1, run2);
	}
}
