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
using VDF.Core.FFTools.FFmpegNative;

namespace VDF.Core.Tests.FFTools;

/// <summary>
/// Covers the managed ring-buffer behavior of the FFmpeg log capture (level filtering, ordering,
/// bounded size, clearing). The native av_log callback wiring itself needs FFmpeg loaded and is
/// exercised by the integration tests; here we test the pure storage logic that drives the
/// diagnostics appended to native failures. State is [ThreadStatic], so each test clears first
/// because xUnit may reuse the worker thread across tests.
/// </summary>
public class FfmpegLogCaptureTests {
	public FfmpegLogCaptureTests() => FfmpegLogCapture.Clear();

	[Fact]
	public void GetRecent_EmptyByDefault() =>
		Assert.Equal(string.Empty, FfmpegLogCapture.GetRecent());

	[Fact]
	public void Record_KeepsWarningAndWorse() {
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_ERROR, "an error");
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_WARNING, "a warning");
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_FATAL, "fatal");
		Assert.Equal("an error | a warning | fatal", FfmpegLogCapture.GetRecent());
	}

	[Fact]
	public void Record_DropsInfoAndBelow() {
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_INFO, "chatty info");
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_VERBOSE, "verbose");
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_DEBUG, "debug");
		Assert.Equal(string.Empty, FfmpegLogCapture.GetRecent());
	}

	[Fact]
	public void Record_IgnoresEmptyAndWhitespace() {
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_ERROR, null);
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_ERROR, "");
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_ERROR, "   ");
		Assert.Equal(string.Empty, FfmpegLogCapture.GetRecent());
	}

	[Fact]
	public void Record_TrimsLines() {
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_ERROR, "  spaced out \n");
		Assert.Equal("spaced out", FfmpegLogCapture.GetRecent());
	}

	[Fact]
	public void Record_KeepsOnlyMostRecentWhenOverflowing() {
		// Capacity is 8; push 10 and expect lines 3..10 (oldest two dropped), oldest first.
		for (int i = 1; i <= 10; i++)
			FfmpegLogCapture.Record(ffmpeg.AV_LOG_ERROR, $"line{i}");
		Assert.Equal("line3 | line4 | line5 | line6 | line7 | line8 | line9 | line10",
			FfmpegLogCapture.GetRecent());
	}

	[Fact]
	public void Clear_Resets() {
		FfmpegLogCapture.Record(ffmpeg.AV_LOG_ERROR, "something");
		Assert.NotEqual(string.Empty, FfmpegLogCapture.GetRecent());
		FfmpegLogCapture.Clear();
		Assert.Equal(string.Empty, FfmpegLogCapture.GetRecent());
	}
}
