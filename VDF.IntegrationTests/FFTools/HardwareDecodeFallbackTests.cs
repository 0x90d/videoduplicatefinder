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
using VDF.Core.FFTools;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// A file the hardware decoder rejects is decoded natively in software instead of going to
/// the FFmpeg process, which would use the same hardware decoder and fail the same way.
/// </summary>
[Collection("Ffmpeg")]
public class HardwareDecodeFallbackTests {
	readonly FfmpegFixture _fixture;

	public HardwareDecodeFallbackTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public void HardwareDecoderFailure_IsDecodedNativelyInSoftware() {
		Skip.If(!OperatingSystem.IsMacOS(), "needs a hardware decoder that cannot exist here: CUDA on macOS");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.cuda;
		FfmpegEngine.CustomFFArguments = string.Empty;

		var entry = new FileEntry(_fixture.H264_8bit!);
		entry.mediaInfo = FFProbeEngine.GetMediaInfo(_fixture.H264_8bit!, extendedLogging: false);
		var positions = new List<float> { 0.3f, 0.7f };

		using var log = new LogCollector();
		var progress = new List<int>();
		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false, onSampleComplete: progress.Add));
		Assert.Equal(positions.Count, entry.grayBytes.Count);
		Assert.Equal(new[] { 1, 2 }, progress); // the software retry does not restart the file's progress
		Assert.True(log.Any("decoded it in software instead"));
		Assert.False(log.Any("Failed using native FFmpeg binding")); // the software attempt worked: no failure recorded
		Assert.False(FfmpegEngine.NativeDisabledForSession);
	}

	[SkippableFact]
	public void HardwareDecoderThatNeverWorks_IsSkippedAfterFiveRescues() {
		Skip.If(!OperatingSystem.IsMacOS(), "needs a hardware decoder that cannot exist here: CUDA on macOS");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.cuda;
		FfmpegEngine.CustomFFArguments = string.Empty;

		using var log = new LogCollector();
		for (int i = 0; i < 6; i++) {
			var entry = new FileEntry(_fixture.H264_8bit!);
			entry.mediaInfo = FFProbeEngine.GetMediaInfo(_fixture.H264_8bit!, extendedLogging: false);
			Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, new List<float> { 0.3f, 0.7f }, 0, extendedLogging: false));
		}

		Assert.True(FfmpegEngine.HardwareDisabledForSession);
		Assert.Equal(5, log.Count("decoded it in software instead")); // the 6th file never tried hardware
		Assert.Equal(1, log.Count("Hardware decoding failed on 5 files"));
		Assert.False(FfmpegEngine.NativeDisabledForSession);
	}
}
