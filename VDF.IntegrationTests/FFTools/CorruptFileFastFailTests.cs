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
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// The corrupt-file fast-fail end-to-end (#867): when the native batch decode fails on a
/// file whose diagnostics classify as truncated/corrupt (software decode), the FFmpeg
/// process retry is skipped — it runs the same libavcodec over the same broken bitstream,
/// so it can only rediscover the failure one timeout at a time. Before the fix a single
/// corrupt file burned the native budget, the per-position native retry AND a CLI attempt
/// (plus one CLI attempt per position when only AI frames were missing), stalling the scan
/// 45+ seconds per file.
/// </summary>
[Collection("Ffmpeg")]
public class CorruptFileFastFailTests {
	readonly FfmpegFixture _fixture;

	public CorruptFileFastFailTests(FfmpegFixture fixture) => _fixture = fixture;

	sealed class RecordingSink : IEmbeddingFrameSink {
		public readonly List<(FileEntry entry, double key, byte[] rgb)> Frames = new();
		public bool WantsEmbedding(FileEntry entry, double positionKey) =>
			!Frames.Any(f => ReferenceEquals(f.entry, entry) && f.key == positionKey);
		public void SubmitFrame(FileEntry entry, double positionKey, byte[] rgb224) =>
			Frames.Add((entry, positionKey, rgb224));
	}

	sealed class LogCollector : IDisposable {
		readonly List<string> _messages = new();
		public LogCollector() => Logger.Instance.LogEntryAdded += OnEntry;
		void OnEntry(LogEntry entry) { lock (_messages) _messages.Add(entry.Message); }
		public bool Any(string substring) {
			lock (_messages) return _messages.Any(m => m.Contains(substring, StringComparison.Ordinal));
		}
		public void Dispose() => Logger.Instance.LogEntryAdded -= OnEntry;
	}

	static FileEntry EntryFor(string path) {
		var entry = new FileEntry(path);
		entry.mediaInfo = FFProbeEngine.GetMediaInfo(path, extendedLogging: false);
		Assert.NotNull(entry.mediaInfo);
		return entry;
	}

	[SkippableFact]
	public void GetGrayBytesFromVideo_CorruptFile_SkipsProcessRetry() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_FullyCorrupted == null, "fully corrupted H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;

		var entry = EntryFor(_fixture.H264_FullyCorrupted!);
		var positions = new List<float> { 0.3f, 0.7f };

		using var log = new LogCollector();
		Assert.False(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false));

		Assert.True(entry.Flags.Has(EntryFlags.ThumbnailError));
		Assert.True(log.Any("Skipping process-mode retry"),
			"Expected the corrupt-classified native failure to fast-fail the file instead of retrying via the FFmpeg process");
		// The regression signature: before the fix the per-position fallback spawned the
		// FFmpeg process, whose failure logs "Failed to retrieve graybytes".
		Assert.False(log.Any("Failed to retrieve graybytes"),
			"The FFmpeg process retry ran even though the native failure already classified the file as corrupt");
	}

	[SkippableFact]
	public void GetGrayBytesFromVideo_CorruptFile_CachedGrays_AbstainsFromAiWithoutFailingFile() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_FullyCorrupted == null, "fully corrupted H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;

		// Gray bytes fully cached (e.g. hashed before the file went bad on disk); only the
		// AI embedding frames are missing. The file must stay comparable — the AI pass
		// abstains — instead of being error-flagged or ground through one CLI attempt per
		// position.
		var entry = EntryFor(_fixture.H264_FullyCorrupted!);
		var positions = new List<float> { 0.3f, 0.7f };
		foreach (float position in positions)
			entry.grayBytes[entry.GetGrayBytesIndex(position, 0)] = new byte[32 * 32];

		var sink = new RecordingSink();
		using var log = new LogCollector();
		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false, embeddingSink: sink));

		Assert.False(entry.Flags.Has(EntryFlags.ThumbnailError));
		Assert.Empty(sink.Frames);
		// Before the fix, every position fell through to a per-position FFmpeg process
		// attempt for the AI frame, each failing with this message after its own grind.
		Assert.False(log.Any("Failed to retrieve"),
			"AI-frame extraction was retried via the FFmpeg process on a corrupt-classified file");
	}
}
