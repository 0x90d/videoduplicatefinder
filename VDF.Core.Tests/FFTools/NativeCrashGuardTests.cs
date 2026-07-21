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

using System.Drawing;
using FFmpeg.AutoGen;
using VDF.Core.FFTools;
using VDF.Core.FFTools.FFmpegNative;

namespace VDF.Core.Tests.FFTools;

// Pins the #861 guards: a corrupt file's frames can diverge from the layout the
// native converters were configured with (open-time container metadata), and feeding
// a diverged frame into swscale/swresample is a native access violation that kills
// the process with no managed error path. These decisions must stay pure so they are
// testable without the native FFmpeg libraries.
public class AudioResamplerGuardTests {
	// Raw values of AVSampleFormat.AV_SAMPLE_FMT_FLTP / AV_SAMPLE_FMT_S16 — literals so
	// the test does not depend on native library presence.
	const int Fltp = 8;
	const int S16 = 1;

	[Fact]
	public void Matches_WhenLayoutUnchanged() {
		Assert.True(AudioStreamDecoder.ResamplerInputMatches(Fltp, 44100, 2, Fltp, 44100, 2));
	}

	[Theory]
	[InlineData(S16, 44100, 2)]   // sample format changed
	[InlineData(Fltp, 48000, 2)]  // sample rate changed (e.g. HE-AAC SBR detection)
	[InlineData(Fltp, 44100, 1)]  // channel count dropped — the #861 crash vector
	[InlineData(Fltp, 44100, 6)]  // channel count grew
	public void Mismatch_WhenAnyParameterDiverges(int frameFormat, int frameRate, int frameChannels) {
		Assert.False(AudioStreamDecoder.ResamplerInputMatches(
			frameFormat, frameRate, frameChannels, Fltp, 44100, 2));
	}

	[Theory]
	[InlineData(-1, 44100, 2, 1024)] // no format
	[InlineData(Fltp, 0, 2, 1024)]  // no sample rate
	[InlineData(Fltp, 44100, 0, 1024)] // no channels
	[InlineData(Fltp, 44100, 2, 0)] // no samples
	public void UndecodableLayout_IsDetected(int format, int rate, int channels, int nbSamples) {
		Assert.True(AudioStreamDecoder.IsUndecodableFrameLayout(format, rate, channels, nbSamples));
	}

	[Fact]
	public void ValidLayout_IsNotUndecodable() {
		Assert.False(AudioStreamDecoder.IsUndecodableFrameLayout(Fltp, 44100, 2, 1024));
	}
}

public unsafe class VideoFrameConverterValidationTests {
	static AVFrame MakeFrame(int width, int height, int format, bool withData = true) {
		var frame = new AVFrame { width = width, height = height, format = format };
		if (withData)
			frame.data[0] = (byte*)0x1000; // any non-null pointer — never dereferenced
		return frame;
	}

	[Fact]
	public void MatchingFrame_Passes() {
		AVFrame frame = MakeFrame(320, 240, (int)AVPixelFormat.AV_PIX_FMT_YUV420P);
		VideoFrameConverter.ValidateSourceFrame(in frame, new Size(320, 240), AVPixelFormat.AV_PIX_FMT_YUV420P);
	}

	[Theory]
	[InlineData(640, 240, (int)AVPixelFormat.AV_PIX_FMT_YUV420P)] // width diverged
	[InlineData(320, 480, (int)AVPixelFormat.AV_PIX_FMT_YUV420P)] // height diverged
	[InlineData(320, 240, (int)AVPixelFormat.AV_PIX_FMT_YUV444P)] // format diverged (mid-stream change)
	public void DivergedFrame_Throws(int width, int height, int format) {
		AVFrame frame = MakeFrame(width, height, format);
		Assert.Throws<FFInvalidExitCodeException>(() =>
			VideoFrameConverter.ValidateSourceFrame(in frame, new Size(320, 240), AVPixelFormat.AV_PIX_FMT_YUV420P));
	}

	[Fact]
	public void FrameWithoutPixelData_Throws() {
		AVFrame frame = MakeFrame(320, 240, (int)AVPixelFormat.AV_PIX_FMT_YUV420P, withData: false);
		Assert.Throws<FFInvalidExitCodeException>(() =>
			VideoFrameConverter.ValidateSourceFrame(in frame, new Size(320, 240), AVPixelFormat.AV_PIX_FMT_YUV420P));
	}
}

public class ResolveSourcePixelFormatTests {
	[Fact]
	public void FrameFormat_WinsOverOpenTimeFormat() {
		// SW decode used to trust the open-time codec-context format; the frame is authoritative.
		Assert.Equal(AVPixelFormat.AV_PIX_FMT_YUV444P,
			FfmpegEngine.ResolveSourcePixelFormat((int)AVPixelFormat.AV_PIX_FMT_YUV444P, AVPixelFormat.AV_PIX_FMT_YUV420P));
	}

	[Fact]
	public void MissingFrameFormat_FallsBackToOpenTimeFormat() {
		Assert.Equal(AVPixelFormat.AV_PIX_FMT_YUV420P,
			FfmpegEngine.ResolveSourcePixelFormat(-1, AVPixelFormat.AV_PIX_FMT_YUV420P));
	}
}

public class NativeFailureLogThrottleTests {
	[Theory]
	[InlineData(1)]
	[InlineData(20)]
	public void FirstFailures_LogFullDetail(int n) {
		Assert.Equal(FfmpegEngine.NativeFailureLogMode.Full, FfmpegEngine.GetNativeFailureLogMode(n));
	}

	[Theory]
	[InlineData(21)]
	[InlineData(200)]
	public void MidTier_LogsCompactLine(int n) {
		Assert.Equal(FfmpegEngine.NativeFailureLogMode.Compact, FfmpegEngine.GetNativeFailureLogMode(n));
	}

	[Theory]
	[InlineData(300)]
	[InlineData(1000)]
	public void HighTier_LogsPeriodicSummary(int n) {
		Assert.Equal(FfmpegEngine.NativeFailureLogMode.Summary, FfmpegEngine.GetNativeFailureLogMode(n));
	}

	[Theory]
	[InlineData(201)]
	[InlineData(299)]
	[InlineData(999)]
	public void HighTier_SuppressesBetweenSummaries(int n) {
		Assert.Equal(FfmpegEngine.NativeFailureLogMode.Suppressed, FfmpegEngine.GetNativeFailureLogMode(n));
	}
}
