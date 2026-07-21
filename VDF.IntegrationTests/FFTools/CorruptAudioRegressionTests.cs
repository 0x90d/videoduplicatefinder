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
using VDF.IntegrationTests.Fixtures;
using VDF.TestSupport;

namespace VDF.IntegrationTests.FFTools;

[Collection("Ffmpeg")]
public class CorruptAudioRegressionTests {
	readonly FfmpegFixture _fixture;

	public CorruptAudioRegressionTests(FfmpegFixture fixture) => _fixture = fixture;

	/// <summary>
	/// #861 regression: a stream whose channel config changes mid-stream (real-world
	/// corrupt AAC) used to be fed into an SwrContext configured for the open-time
	/// layout. When the channel count DROPS, swr_convert dereferences source plane
	/// pointers that are null on the smaller frames — a native access violation that
	/// killed the whole process (this test host, when run against the unfixed code).
	/// The fix rebuilds the resampler when a frame's layout diverges, so the decode
	/// completes and covers BOTH segments.
	/// </summary>
	[SkippableFact]
	public void DecodeAll_ChannelCountDropsMidStream_DecodesBothSegmentsWithoutCrashing() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason ?? "FFmpeg CLI not available");
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");

		string ffmpeg = FfmpegEngine.FFmpegPath;
		string stereo = Path.Combine(_fixture.TempDir, "adts_stereo.aac");
		string mono = Path.Combine(_fixture.TempDir, "adts_mono.aac");
		Skip.If(!TestVideoGenerator.GenerateAacAdts(ffmpeg, stereo, channels: 2), "Could not generate stereo ADTS fixture");
		Skip.If(!TestVideoGenerator.GenerateAacAdts(ffmpeg, mono, channels: 1), "Could not generate mono ADTS fixture");

		// Byte-concatenate: ADTS is self-syncing, so the result is one decodable
		// stream that switches stereo -> mono after ~2s.
		string combined = Path.Combine(_fixture.TempDir, "adts_stereo_then_mono.aac");
		using (FileStream output = File.Create(combined)) {
			foreach (string part in new[] { stereo, mono })
				using (FileStream input = File.OpenRead(part))
					input.CopyTo(output);
		}

		const int targetRate = 11025;
		using var decoder = new AudioStreamDecoder(combined, targetRate);
		Assert.True(decoder.HasAudioStream);

		int totalSamples = decoder.DecodeAll(_ => { }, CancellationToken.None);

		// 2s stereo + 2s mono at 11025 Hz mono output ≈ 44100 samples. Requiring
		// clearly more than one segment's worth proves decoding continued PAST the
		// layout change instead of stopping (or crashing) at it.
		Assert.True(totalSamples > 3 * targetRate,
			$"Expected ~4s of samples (>{3 * targetRate}), got {totalSamples} — decode did not survive the mid-stream channel change");
	}

	/// <summary>
	/// The reverse direction (channel count grows) reads too few planes rather than too
	/// many — not a crash vector, but the rebuild must handle it just as gracefully.
	/// </summary>
	[SkippableFact]
	public void DecodeAll_ChannelCountGrowsMidStream_DecodesBothSegments() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason ?? "FFmpeg CLI not available");
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");

		string ffmpeg = FfmpegEngine.FFmpegPath;
		string mono = Path.Combine(_fixture.TempDir, "adts_mono2.aac");
		string stereo = Path.Combine(_fixture.TempDir, "adts_stereo2.aac");
		Skip.If(!TestVideoGenerator.GenerateAacAdts(ffmpeg, mono, channels: 1), "Could not generate mono ADTS fixture");
		Skip.If(!TestVideoGenerator.GenerateAacAdts(ffmpeg, stereo, channels: 2), "Could not generate stereo ADTS fixture");

		string combined = Path.Combine(_fixture.TempDir, "adts_mono_then_stereo.aac");
		using (FileStream output = File.Create(combined)) {
			foreach (string part in new[] { mono, stereo })
				using (FileStream input = File.OpenRead(part))
					input.CopyTo(output);
		}

		const int targetRate = 11025;
		using var decoder = new AudioStreamDecoder(combined, targetRate);
		Assert.True(decoder.HasAudioStream);

		int totalSamples = decoder.DecodeAll(_ => { }, CancellationToken.None);

		Assert.True(totalSamples > 3 * targetRate,
			$"Expected ~4s of samples (>{3 * targetRate}), got {totalSamples} — decode did not survive the mid-stream channel change");
	}
}
