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
using VDF.TestSupport;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// #880: a video whose keyframes cannot be decoded failed the AI dense sampling again on
/// every scan, after hours of decoding on a large file. The failure is now remembered and
/// the file skipped until it changes, or until "Always retry failed sampling" is on.
/// </summary>
[Collection("Ffmpeg")]
public sealed class DenseSamplingFailureMemoryTests : IDisposable {
	readonly FfmpegFixture _fixture;
	readonly string storeDir = Path.Combine(Path.GetTempPath(), $"vdf_dense_fail_{Guid.NewGuid():N}");
	readonly List<FileEntry> added = new();
	readonly string? previousModel = AiComponents.TestOverrideModelPath;

	public DenseSamplingFailureMemoryTests(FfmpegFixture fixture) {
		_fixture = fixture;
		Directory.CreateDirectory(storeDir);
		DenseEmbeddingStore.TestOverrideStorePath = Path.Combine(storeDir, "DenseEmbeddings.db");
		AiComponents.TestOverrideModelPath = TestModels.TinyEmbedderPath;
	}

	public void Dispose() {
		lock (DatabaseUtils.Database)
			foreach (var e in added)
				DatabaseUtils.Database.Remove(e);
		DenseEmbeddingStore.TestOverrideStorePath = null;
		AiComponents.TestOverrideModelPath = previousModel;
		try { Directory.Delete(storeDir, true); } catch { }
	}

	FileEntry Add(string sourcePath, string name, TimeSpan? duration = null) {
		// Copies, so the scan's size/mtime keys belong to this test alone.
		string path = Path.Combine(storeDir, name);
		File.Copy(sourcePath, path);
		var entry = new FileEntry(path) { invalid = false };
		entry.mediaInfo = FFProbeEngine.GetMediaInfo(path, extendedLogging: false)
			?? new MediaInfo { Duration = duration ?? TimeSpan.FromSeconds(10), Streams = Array.Empty<MediaInfo.StreamInfo>() };
		if (duration != null)
			entry.mediaInfo.Duration = duration.Value;
		lock (DatabaseUtils.Database)
			DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	[SkippableFact]
	public void AFileThatFailedIsSkippedNextTime_UntilRetryIsAskedFor() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null || _fixture.H264_FullyCorrupted == null, "test videos not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		// The generated clips are 2s; the pass only looks at videos of 3s and more.
		Add(_fixture.H264_8bit!, "good.mp4", TimeSpan.FromSeconds(10));
		// Its container still states a duration; the frames behind it are garbage.
		Add(_fixture.H264_FullyCorrupted!, "broken.mp4", TimeSpan.FromSeconds(10));

		var first = new ScanEngine();
		first.ScanForPartialDuplicatesVisual();
		Assert.Equal(1, first.LastDenseSamplingCounts.Failed);
		Assert.Equal(1, first.LastDenseSamplingCounts.Extracted);

		// Next scan: the good file comes from the cache, the broken one is not decoded again.
		var second = new ScanEngine();
		second.ScanForPartialDuplicatesVisual();
		Assert.Equal((0, 1, 0, 1), second.LastDenseSamplingCounts);

		// "Always retry failed sampling" brings it back, as it does for frame sampling.
		var retry = new ScanEngine();
		retry.Settings.AlwaysRetryFailedSampling = true;
		retry.ScanForPartialDuplicatesVisual();
		Assert.Equal(1, retry.LastDenseSamplingCounts.Failed);
		Assert.Equal(0, retry.LastDenseSamplingCounts.SkippedFailed);
	}
}
