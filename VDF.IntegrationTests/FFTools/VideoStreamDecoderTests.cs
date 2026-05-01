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

using FFmpeg.AutoGen;
using VDF.Core.FFTools.FFmpegNative;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

[Collection("Ffmpeg")]
public class VideoStreamDecoderTests {
	readonly FfmpegFixture _fixture;

	public VideoStreamDecoderTests(FfmpegFixture fixture) => _fixture = fixture;

	/// <summary>
	/// Sweeps every 0.1s through a clean 2s H.264 file and asserts every in-range
	/// position decodes successfully. Pins the new packet-loop control flow so a
	/// future refactor can't accidentally infinite-loop, leak packet refs, or
	/// confuse continue/break across the inner read-loop and outer decode-loop.
	/// </summary>
	[SkippableFact]
	public void TryDecodeFrame_CleanFile_AllPositionsDecode() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var vsd = new VideoStreamDecoder(_fixture.H264_8bit!);

		// Fixture is testsrc2 duration=2s. Sweep up to 1.9s; positions at/past 2.0s
		// are legitimately past EOF and would return false.
		int succeeded = 0;
		for (double pos = 0; pos < 1.95; pos += 0.1) {
			bool decoded = vsd.TryDecodeFrame(out _, TimeSpan.FromSeconds(pos));
			Assert.True(decoded, $"TryDecodeFrame returned false at pos={pos:F2}s on a clean file");
			succeeded++;
		}
		Assert.True(succeeded >= 18, $"Expected ~20 successful decodes, got {succeeded}");
	}

	/// <summary>
	/// Decodes a deliberately corrupted H.264 file. Before the #731 fix, the first
	/// AVERROR_INVALIDDATA from av_read_frame or avcodec_send_packet bubbled out as
	/// FFInvalidExitCodeException("Invalid data found when processing input"), which
	/// forced VDF to spam the log and fall through to CLI process mode. The fix
	/// skips bad packets and either returns true with a recovered frame or false
	/// when the cap (64 bad packets) is reached — but never throws AVERROR_INVALIDDATA.
	/// </summary>
	[SkippableFact]
	public void TryDecodeFrame_CorruptedFile_DoesNotThrowInvalidData() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_Corrupted == null, "Corrupted H264 test video not generated");

		using var vsd = new VideoStreamDecoder(_fixture.H264_Corrupted!);

		// A handful of positions across the 2s clip; each must complete without
		// throwing. Whether the call returns true (recovered) or false (gave up
		// after the bad-packet cap) is fixture-noise-dependent and intentionally
		// not asserted — the contract under test is "no INVALIDDATA escapes".
		foreach (double pos in new[] { 0.0, 0.5, 1.0, 1.5 }) {
			var ex = Record.Exception(() => vsd.TryDecodeFrame(out _, TimeSpan.FromSeconds(pos)));
			Assert.True(ex == null,
				$"TryDecodeFrame at pos={pos:F2}s threw {ex?.GetType().Name}: {ex?.Message}");
		}
	}

	/// <summary>
	/// AVERROR_EOF (e.g. seeking past the end of the stream) must continue to
	/// return false rather than throwing — the new bad-packet handling sits next
	/// to the EOF check, so it's worth pinning that EOF still wins.
	/// </summary>
	[SkippableFact]
	public void TryDecodeFrame_PastEnd_ReturnsFalseWithoutThrowing() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var vsd = new VideoStreamDecoder(_fixture.H264_8bit!);

		// Fixture is 2s; 60s is well past EOF.
		bool decoded = vsd.TryDecodeFrame(out _, TimeSpan.FromSeconds(60));
		Assert.False(decoded);
	}
}
