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

using FFmpeg.AutoGen;
using VDF.Core.FFTools;
using VDF.Core.FFTools.FFmpegNative;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// End-to-end coverage that the native av_log callback actually captures FFmpeg's own diagnostic
/// lines (otherwise lost by the native binding) and that they classify to a useful cause. This is
/// the mechanism that turns an opaque native failure into "your GPU can't decode this" / "the file
/// is corrupt", so issue reports become self-service. The pure ring-buffer/classifier logic is
/// unit tested separately; this test needs FFmpeg loaded to exercise the real callback.
/// </summary>
[Collection("Ffmpeg")]
public class FfmpegLogCaptureIntegrationTests {
	readonly FfmpegFixture _fixture;

	public FfmpegLogCaptureIntegrationTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public unsafe void NativeDecode_OfCorruptStream_CapturesAndClassifiesFfmpegDiagnostics() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_Corrupted == null, "corrupted H.264 fixture not generated");

		// A recognized-but-damaged stream makes the H.264 decoder emit its own av_log error lines
		// ("Invalid NAL unit size", "error while decoding", ...) — exactly the diagnostics the
		// native binding otherwise discards. Walk a few positions so the decoder hits the noise.
		FfmpegLogCapture.Reset();
		using (var vsd = new VideoStreamDecoder(_fixture.H264_Corrupted!, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)) {
			foreach (double pos in new[] { 0.0, 0.25, 0.5, 0.75 }) {
				try { vsd.TryDecodeFrame(out _, TimeSpan.FromSeconds(pos)); }
				catch { /* a throw is fine; we only care that FFmpeg logged something */ }
			}
		}

		string diagnostics = FfmpegLogCapture.GetRecent();
		Assert.False(string.IsNullOrEmpty(diagnostics),
			"Expected FFmpeg's own decoder error lines to be captured for the corrupt stream");
		Assert.Equal(FfmpegErrorCategory.CorruptOrTruncated,
			FfmpegErrorClassifier.Categorize(diagnostics));
	}

	[SkippableFact]
	public unsafe void SuccessfulNativeDecode_LeavesNoCapturedErrors() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H.264 fixture not generated");

		FfmpegLogCapture.Reset();
		using (var vsd = new VideoStreamDecoder(_fixture.H264_8bit!, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)) {
			Assert.True(vsd.TryDecodeFrame(out _, TimeSpan.Zero));
		}

		// A clean decode should not leave warning/error lines that would mislead a later failure.
		Assert.Equal(string.Empty, FfmpegLogCapture.GetRecent());
	}
}
