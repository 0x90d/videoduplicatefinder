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
/// The AI decode taps end-to-end on real (generated) videos: 224x224 RGB frames from
/// both the native and process paths, dense keyframe sampling, and the full
/// frame → embedding → union-verdict chain using the checked-in tiny embedder.
/// </summary>
[Collection("Ffmpeg")]
public class AiFrameExtractionTests {
	const int Rgb224Bytes = OnnxEmbedder.InputSide * OnnxEmbedder.InputSide * 3;
	readonly FfmpegFixture _fixture;

	public AiFrameExtractionTests(FfmpegFixture fixture) => _fixture = fixture;

	/// <summary>Records submitted frames without needing ONNX components.</summary>
	sealed class RecordingSink : IEmbeddingFrameSink {
		public readonly List<(FileEntry entry, double key, byte[] rgb)> Frames = new();
		public bool WantsEmbedding(FileEntry entry, double positionKey) =>
			!Frames.Any(f => ReferenceEquals(f.entry, entry) && f.key == positionKey);
		public void SubmitFrame(FileEntry entry, double positionKey, byte[] rgb224) =>
			Frames.Add((entry, positionKey, rgb224));
	}

	static FileEntry EntryFor(string path) {
		var entry = new FileEntry(path);
		entry.mediaInfo = FFProbeEngine.GetMediaInfo(path, extendedLogging: false);
		Assert.NotNull(entry.mediaInfo);
		return entry;
	}

	[SkippableTheory]
	[InlineData(false)]
	[InlineData(true)]
	public void GetGrayBytesFromVideo_FeedsSinkWithRgb224Frames(bool native) {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(native && !_fixture.NativeBindingAvailable, "native FFmpeg libraries not present");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = native;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var entry = EntryFor(_fixture.H264_8bit!);
		var sink = new RecordingSink();
		var positions = new List<float> { 0.3f, 0.7f };

		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false, embeddingSink: sink));

		Assert.Equal(2, entry.grayBytes.Count); // gray path unaffected by the tap
		Assert.Equal(2, sink.Frames.Count);
		Assert.All(sink.Frames, f => Assert.Equal(Rgb224Bytes, f.rgb.Length));
		// Same position keys as the gray bytes, so embeddings stay aligned.
		Assert.All(sink.Frames, f => Assert.Contains(f.key, entry.grayBytes.Keys));
	}

	[SkippableFact]
	public void GetGrayBytesFromVideo_CachedGray_StillBackfillsEmbeddings() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;

		var entry = EntryFor(_fixture.H264_8bit!);
		var positions = new List<float> { 0.5f };
		// First pass without a sink: only gray bytes get cached (old-database situation).
		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false));
		Assert.Single(entry.grayBytes);

		var sink = new RecordingSink();
		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false, embeddingSink: sink));
		Assert.Single(sink.Frames);
		Assert.Single(entry.grayBytes); // no re-extraction of gray data
	}

	[SkippableTheory]
	[InlineData("video")]
	[InlineData("jpeg")]
	[InlineData("png")]
	public void GetGrayAndRgb224Cli_MatchesTheTwoSingleCallOutputs(string kind) {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		string? file = kind switch {
			"video" => _fixture.H264_8bit,
			"jpeg" => _fixture.SampleJpeg,
			_ => _fixture.SamplePng
		};
		Skip.If(file == null, $"{kind} test file not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.CustomFFArguments = string.Empty;

		TimeSpan position = kind == "video" ? TimeSpan.FromSeconds(1) : TimeSpan.Zero;
		bool softwareOnly = kind != "video";

		(byte[]? gray, byte[]? rgb) = FfmpegEngine.GetGrayAndRgb224Cli(file!, position, softwareOnly, extendedLogging: false);

		Assert.NotNull(gray);
		Assert.NotNull(rgb);
		Assert.Equal(32 * 32, gray!.Length);
		Assert.Equal(Rgb224Bytes, rgb!.Length);

		// The combined invocation must be a pure optimization: byte-identical to the
		// two single-output runs, or cached gray bytes from mixed scans would drift.
		byte[]? singleGray = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = file!, Position = position, GrayScale = 1, SoftwareDecodeOnly = softwareOnly
		}, extendedLogging: false);
		byte[]? singleRgb = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = file!, Position = position, Rgb224 = true, SoftwareDecodeOnly = softwareOnly
		}, extendedLogging: false);

		Assert.Equal(singleGray, gray);
		Assert.Equal(singleRgb, rgb);
	}

	[SkippableFact]
	public void GetGrayBytesFromVideo_WithCustomFFArguments_StillFeedsSinkViaTwoCallPath() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		// A user -vf disables the combined invocation (it belongs on the gray chain
		// only); the sink must still receive its unfiltered frames via the old path.
		FfmpegEngine.CustomFFArguments = "-vf hflip";

		var entry = EntryFor(_fixture.H264_8bit!);
		var sink = new RecordingSink();
		var positions = new List<float> { 0.5f };

		Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false, embeddingSink: sink));

		Assert.Single(entry.grayBytes);
		var frame = Assert.Single(sink.Frames);
		Assert.Equal(Rgb224Bytes, frame.rgb.Length);

		// Embedding inputs are uniformly unfiltered — the user filter must not reach them.
		FfmpegEngine.CustomFFArguments = string.Empty;
		byte[]? unfilteredRgb = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_8bit!,
			Position = TimeSpan.FromSeconds(entry.grayBytes.Keys.First()),
			Rgb224 = true,
		}, extendedLogging: false);
		Assert.Equal(unfilteredRgb, frame.rgb);
	}

	[SkippableFact]
	public void GetThumbnail_Rgb224_ProcessMode_ReturnsPackedFrame() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;

		byte[]? rgb = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_8bit!,
			Position = TimeSpan.FromSeconds(1),
			Rgb224 = true,
		}, extendedLogging: false);

		Assert.NotNull(rgb);
		Assert.Equal(Rgb224Bytes, rgb!.Length);
	}

	[SkippableFact]
	public void GetDenseAiFrames_KeyframeSweep_ProducesFrames() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		byte[][]? frames = FfmpegEngine.GetDenseAiFrames(_fixture.H264_8bit!, intervalSeconds: 1, maxFrames: 100, extendedLogging: false);

		Assert.NotNull(frames);
		Assert.True(frames!.Length >= 1);
		Assert.All(frames, f => Assert.Equal(Rgb224Bytes, f.Length));
	}

	[SkippableFact]
	public async Task FullChain_TinyModel_UnionVerdictOnIdenticalVideo() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null || _fixture.H264_Different == null, "test videos not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;

		var positions = new List<float> { 0.3f, 0.7f };
		var same1 = EntryFor(_fixture.H264_8bit!);
		var same2 = EntryFor(_fixture.H264_8bit!);
		var different = EntryFor(_fixture.H264_Different!);

		var store = new UnionEmbeddingStore();
		using (var pipeline = new EmbeddingPipeline(TestModels.TinyEmbedderPath, store, CancellationToken.None)) {
			foreach (var entry in new[] { same1, same2, different })
				Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, positions, 0, extendedLogging: false, embeddingSink: pipeline));
			await pipeline.CompleteAsync();
			Assert.False(pipeline.Faulted);
		}
		foreach (float position in positions)
			Assert.NotNull(store.GetEmbedding(same1, same1.GetGrayBytesIndex(position, 0)));

		var engine = new ScanEngine {
			// Percent 101 forces the classic check to always fail, so the verdict below
			// can only come from the AI union — that's exactly the path under test.
			Settings = { UseAiMatching = true, AiPercent = 94f, Percent = 101f, UsePHashing = false }
		};
		engine.unionEmbeddingStore = store;
		engine.positionList.AddRange(positions);
		foreach (var entry in new[] { same1, same2, different })
			Assert.True(engine.TryBuildCompareSnapshot(entry, usePHashing: false));

		Assert.True(engine.CheckIfDuplicate(same1, null, null, same2, out float difference, out bool aiMatched));
		Assert.True(aiMatched);
		Assert.True(difference < 0.05f);

		Assert.False(engine.CheckIfDuplicate(same1, null, null, different, out _, out _));
	}
}
