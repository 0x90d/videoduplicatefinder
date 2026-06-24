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
using VDF.Core.FFTools.FFmpegNative;
using VDF.TestSupport;

namespace VDF.IntegrationTests.Fixtures;

/// <summary>
/// Shared fixture that validates FFmpeg availability and generates test videos once per run.
/// FFmpeg must be installed and available in PATH (or in the app's bin/ directory).
/// On CI this is handled by "choco install ffmpeg". For local development, install FFmpeg
/// and ensure ffmpeg.exe is on your PATH.
/// </summary>
public class FfmpegFixture : IDisposable {
	public string TempDir { get; }
	public bool FfmpegCliAvailable { get; }
	public bool NativeBindingAvailable { get; }

	public string? H264_8bit { get; }
	public string? HEVC_10bit { get; }
	public string? VP9 { get; }
	public string? H264_Different { get; }
	public string? H264_Corrupted { get; }

	/// <summary>Checked-in HEIC still image (FFmpeg can decode but not mux HEIC).</summary>
	public string? SampleHeic { get; }
	/// <summary>Generated still JPEG (MJPEG) for native still-image decode coverage.</summary>
	public string? SampleJpeg { get; }
	/// <summary>Generated still PNG for native still-image decode coverage.</summary>
	public string? SamplePng { get; }
	/// <summary>H.264 clip stamped with <see cref="CreationTimeTag"/> in its container metadata.</summary>
	public string? Mp4WithCreationTime { get; }
	/// <summary>The creation_time value stamped into <see cref="Mp4WithCreationTime"/>.</summary>
	public const string CreationTimeTag = "2023-08-15T12:34:56.000000Z";

	public bool HasLibx265 { get; }
	public bool HasLibvpxVp9 { get; }

	/// <summary>
	/// Message explaining why FFmpeg was not found, for use in skip reasons.
	/// </summary>
	public string? FfmpegNotFoundReason { get; }

	public FfmpegFixture() {
		TempDir = Path.Combine(Path.GetTempPath(), $"vdf_integration_tests_{Guid.NewGuid():N}");
		Directory.CreateDirectory(TempDir);

		string ffmpegPath = FfmpegEngine.FFmpegPath;
		FfmpegCliAvailable = !string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath);
		NativeBindingAvailable = FFmpegHelper.DoFFmpegLibraryFilesExist;

		if (!FfmpegCliAvailable) {
			string message =
				"FFmpeg not found. Integration tests require ffmpeg.exe in PATH. " +
				"Install FFmpeg (e.g. 'choco install ffmpeg' on Windows, " +
				"'apt install ffmpeg' on Linux, 'brew install ffmpeg' on macOS).";

			// On CI, fail hard — tests silently skipping would hide real problems.
			if (Environment.GetEnvironmentVariable("CI") != null)
				throw new InvalidOperationException(message);

			FfmpegNotFoundReason = message;
			return;
		}

		// Probe encoder availability
		HasLibx265 = TestVideoGenerator.HasEncoder(ffmpegPath, "libx265");
		HasLibvpxVp9 = TestVideoGenerator.HasEncoder(ffmpegPath, "libvpx-vp9");

		// Generate test videos
		string h264Path = Path.Combine(TempDir, "h264_8bit.mp4");
		if (TestVideoGenerator.GenerateH264_8bit(ffmpegPath, h264Path))
			H264_8bit = h264Path;

		if (HasLibx265) {
			string hevcPath = Path.Combine(TempDir, "hevc_10bit.mp4");
			if (TestVideoGenerator.GenerateHEVC_10bit(ffmpegPath, hevcPath))
				HEVC_10bit = hevcPath;
		}

		if (HasLibvpxVp9) {
			string vp9Path = Path.Combine(TempDir, "vp9.webm");
			if (TestVideoGenerator.GenerateVP9(ffmpegPath, vp9Path))
				VP9 = vp9Path;
		}

		string diffPath = Path.Combine(TempDir, "h264_different.mp4");
		if (TestVideoGenerator.GenerateH264_Different(ffmpegPath, diffPath))
			H264_Different = diffPath;

		// Corrupted H.264 fixture: re-encode the clean H.264 through bsf=noise so the
		// native decoder will hit AVERROR_INVALIDDATA from av_read_frame /
		// avcodec_send_packet. Used by VideoStreamDecoderTests to verify the #731 fix.
		if (H264_8bit != null) {
			string corruptPath = Path.Combine(TempDir, "h264_corrupted.mp4");
			if (TestVideoGenerator.GenerateH264_Corrupted(ffmpegPath, H264_8bit, corruptPath))
				H264_Corrupted = corruptPath;
		}

		// HEIC support fixtures. The sample image is checked in (FFmpeg cannot mux HEIC);
		// the creation_time clip exercises the FFprobe-based EXIF-date fallback.
		string heicSample = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.heic");
		if (File.Exists(heicSample))
			SampleHeic = heicSample;

		string creationTimePath = Path.Combine(TempDir, "creation_time.mp4");
		if (TestVideoGenerator.GenerateMp4WithCreationTime(ffmpegPath, creationTimePath, CreationTimeTag))
			Mp4WithCreationTime = creationTimePath;

		// Still images: a single frame has no timeline to seek into, so native decoding must
		// not input-seek and must drain the decoder to emit the buffered frame (see #810).
		string jpegPath = Path.Combine(TempDir, "still.jpg");
		if (TestVideoGenerator.GenerateStillImage(ffmpegPath, jpegPath))
			SampleJpeg = jpegPath;
		string pngPath = Path.Combine(TempDir, "still.png");
		if (TestVideoGenerator.GenerateStillImage(ffmpegPath, pngPath))
			SamplePng = pngPath;
	}

	public void Dispose() {
		try {
			if (Directory.Exists(TempDir))
				Directory.Delete(TempDir, true);
		}
		catch { }
	}
}

[CollectionDefinition("Ffmpeg")]
public class FfmpegCollection : ICollectionFixture<FfmpegFixture> { }
