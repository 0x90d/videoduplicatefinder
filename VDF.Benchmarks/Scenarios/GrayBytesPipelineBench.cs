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

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using VDF.Core;
using VDF.Core.FFTools;

namespace VDF.Benchmarks.Scenarios;

/// <summary>
/// End-to-end <c>FfmpegEngine.GetGrayBytesFromVideo</c> benchmark: opens a video,
/// extracts N gray-byte samples, returns. This is the dominant cost on the scan
/// hot path, and the surface where the candidate "decoder reuse across positions"
/// optimization will show up.
///
/// I/O-bound: one invocation per iteration (no inner loop), several iterations,
/// short warmup. BDN's MemoryDiagnoser still applies.
/// </summary>
[MemoryDiagnoser]
// 3 warmup iterations because the first run pays for OS file cache miss + codec/sws first-init
// + JIT, which otherwise pulls the mean off by 100+ ms. 12 iterations narrows the CI enough that
// before/after deltas in the 30-100 ms range stay visible.
[SimpleJob(RunStrategy.Monitoring, iterationCount: 12, warmupCount: 3, invocationCount: 1, launchCount: 1)]
public class GrayBytesPipelineBench {
	/// <summary>Number of sample positions per video (matches Settings.ThumbnailCount).</summary>
	[Params(1, 4)]
	public int Positions;

	/// <summary>Codec family — drives the corpus video selected.</summary>
	[Params(VideoCorpus.Codec.H264, VideoCorpus.Codec.HEVC10)]
	public VideoCorpus.Codec Codec;

	/// <summary>true → FFmpeg.AutoGen native binding; false → spawn ffmpeg CLI.</summary>
	[Params(true, false)]
	public bool Native;

	string? _videoPath;
	FileEntry _entry = null!;
	List<float> _positionList = null!;

	[GlobalSetup]
	public void Setup() {
		// 60-second 1280x720 covers the realistic mid-range. Long enough that seeking
		// to multiple positions exercises real demuxer work; small enough that the
		// encode step at corpus-build time stays under a few seconds per codec.
		var spec = new VideoCorpus.Spec(Codec, 1280, 720, 60);
		_videoPath = VideoCorpus.Ensure(spec);
		if (_videoPath == null)
			throw new InvalidOperationException(
				$"Could not provision corpus video for {Codec}. " +
				"FFmpeg CLI must be available; HEVC10 needs libx265, VP9 needs libvpx-vp9.");

		_positionList = new List<float>(Positions);
		float step = 1f / (Positions + 1);
		for (int i = 0; i < Positions; i++)
			_positionList.Add(step * (i + 1));

		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
	}

	[IterationSetup]
	public void IterationSetup() {
		// FfmpegEngine.UseNativeBinding is a static. BDN runs benchmark methods
		// serially, so flipping it per-iteration is safe within this assembly.
		FfmpegEngine.UseNativeBinding = Native;

		// Fresh FileEntry per iteration so prior gray-byte caching can't bias the next run.
		_entry = new FileEntry(_videoPath!) {
			mediaInfo = FFProbeEngine.GetMediaInfo(_videoPath!, extendedLogging: false)
		};
	}

	[Benchmark]
	public bool GetGrayBytesFromVideo() =>
		FfmpegEngine.GetGrayBytesFromVideo(
			_entry,
			_positionList,
			maxSamplingDurationSeconds: 0,
			extendedLogging: false);
}
