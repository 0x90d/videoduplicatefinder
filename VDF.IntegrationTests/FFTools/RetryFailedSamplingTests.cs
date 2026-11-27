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
using VDF.Core.AI;
using VDF.Core.FFTools;
using VDF.IntegrationTests.Fixtures;
using VDF.TestSupport;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// "Always retry failed sampling" re-decodes files whose sampling failed in an earlier scan.
/// Those retries must neither switch the native binding off for the whole scan (a rescan
/// often analyses little else, so five known-bad files in a row did it within seconds) nor
/// leave a file that now decodes marked as failed.
/// </summary>
[Collection("Ffmpeg")]
public class RetryFailedSamplingTests {
	readonly FfmpegFixture _fixture;

	public RetryFailedSamplingTests(FfmpegFixture fixture) => _fixture = fixture;

	static FileEntry EntryFor(string path, bool failedBefore) {
		var entry = new FileEntry(path);
		entry.mediaInfo = FFProbeEngine.GetMediaInfo(path, extendedLogging: false);
		Assert.NotNull(entry.mediaInfo);
		if (failedBefore)
			entry.Flags.Set(EntryFlags.ThumbnailError);
		return entry;
	}

	static readonly List<float> Positions = new() { 0.3f, 0.7f };

	FfmpegStaticStateGuard NativeSoftwareDecode() {
		var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true; // also resets the per-scan failure count
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;
		return guard;
	}

	/// <summary>
	/// A file with no video stream: native decoding fails with "Stream not found", which the
	/// classifier cannot place, so it counts towards the switch-off unless the file is a retry.
	/// </summary>
	string AudioOnlyFile() {
		string path = Path.Combine(_fixture.TempDir, "audio_only.mp4");
		if (!File.Exists(path))
			Skip.If(!TestVideoGenerator.GenerateAacAdts(FfmpegEngine.FFmpegPath, path, channels: 2, durationSeconds: 10), "audio-only test file not generated");
		return path;
	}

	static FileEntry AudioOnlyEntry(string path) =>
		new() { _Path = path, mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(10) } };

	/// <summary>Wants an AI frame for every position, as the AI pass does for a file without them.</summary>
	sealed class WantsAiFrames : IEmbeddingFrameSink {
		public bool WantsEmbedding(FileEntry entry, double positionKey) => true;
		public void SubmitFrame(FileEntry entry, double positionKey, byte[] rgb224) { }
	}

	void FailFiveTimes(Func<FileEntry> entry, IEmbeddingFrameSink? sink = null) {
		for (int i = 0; i < 5; i++)
			FfmpegEngine.GetGrayBytesFromVideo(entry(), Positions, 0, extendedLogging: false, embeddingSink: sink);
	}

	[SkippableFact]
	public void FilesMarkedFailed_DoNotSwitchOffNativeBinding() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		string path = AudioOnlyFile();
		using var guard = NativeSoftwareDecode();

		FailFiveTimes(() => {
			var entry = AudioOnlyEntry(path);
			entry.Flags.Set(EntryFlags.ThumbnailError);
			return entry;
		});
		Assert.False(FfmpegEngine.NativeDisabledForSession);
	}

	[SkippableFact]
	public void FullySampledFilesOpenedForAiFrames_DoNotSwitchOffNativeBinding() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		string path = AudioOnlyFile();
		using var guard = NativeSoftwareDecode();

		// Frames sampled in an earlier scan, AI frames still missing: the files that switched
		// native off at the start of every rescan.
		FailFiveTimes(() => {
			var entry = AudioOnlyEntry(path);
			foreach (float p in Positions)
				entry.grayBytes[entry.GetGrayBytesIndex(p, 0)] = new byte[32 * 32];
			return entry;
		}, new WantsAiFrames());
		Assert.False(FfmpegEngine.NativeDisabledForSession);
	}

	[SkippableFact]
	public void FilesSampledForTheFirstTime_StillSwitchItOff() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		string path = AudioOnlyFile();
		using var guard = NativeSoftwareDecode();

		// The same file and failures as above, so the two tests above can fail.
		FailFiveTimes(() => AudioOnlyEntry(path));
		Assert.True(FfmpegEngine.NativeDisabledForSession);
	}

	[SkippableFact]
	public void SuccessfulRetry_ClearsTheFailedMark() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = NativeSoftwareDecode();
		var entry = EntryFor(_fixture.H264_8bit!, failedBefore: true);

		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, Positions, 0, extendedLogging: false));
		Assert.Equal(Positions.Count, entry.grayBytes.Count);
		Assert.False(entry.Flags.Has(EntryFlags.ThumbnailError));
	}

	[SkippableFact]
	public void NothingToDecode_KeepsTheFailedMark() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = NativeSoftwareDecode();
		var entry = EntryFor(_fixture.H264_8bit!, failedBefore: false);
		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, Positions, 0, extendedLogging: false));

		// All frames cached, yet marked failed: what the crash quarantine leaves for a file that
		// took the process down in the partial-clip visual gate. Nothing was decoded, so nothing
		// shows the file is safe again.
		entry.Flags.Set(EntryFlags.ThumbnailError);
		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, Positions, 0, extendedLogging: false));
		Assert.True(entry.Flags.Has(EntryFlags.ThumbnailError));
	}
}
