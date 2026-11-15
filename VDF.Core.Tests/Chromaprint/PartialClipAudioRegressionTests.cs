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

using VDF.Core.Chromaprint;
using VDF.TestSupport;

namespace VDF.Core.Tests.Chromaprint;

public class PartialClipAudioRegressionTests {
	internal static uint[] Fingerprint(ReadOnlySpan<short> samples) {
		var context = new ChromaContext();
		context.Start(); context.Feed(samples); context.Finish();
		return context.GetFrameFingerprint();
	}

	[Theory]
	[InlineData(2.0)]
	[InlineData(2.5)]
	[InlineData(5.5)]
	[InlineData(7.5)]
	public void SampleExactExcerpt_IndependentlyFingerprinted_MatchesAtDefaultThreshold(double offset) {
		short[] source = PartialClipAudioFixture.Generate(16);
		var sourceFp = Fingerprint(source);
		var clipFp = Fingerprint(source.AsSpan((int)(offset * 11025), 8 * 11025));
		var (similarity, _) = ScanEngine.SlidingWindowCompare(clipFp, sourceFp, .8f);
		Assert.True(similarity >= .8f, $"Sample-exact excerpt at {offset}s scored {similarity:P2}");
	}

	[Theory]
	[InlineData(511)]
	[InlineData(4096)]
	public void FineFeatures_AreIndependentOfPcmFeedChunkBoundaries(int chunkSize) {
		short[] pcm = PartialClipAudioFixture.Generate(8);
		uint[] expected = Fingerprint(pcm);
		var context = new ChromaContext(); context.Start();
		for (int offset = 0; offset < pcm.Length; offset += chunkSize)
			context.Feed(pcm.AsSpan(offset, Math.Min(chunkSize, pcm.Length - offset)));
		context.Finish();
		Assert.Equal(expected, context.GetFrameFingerprint());
		context.Start(); context.Feed(pcm); context.Finish();
		Assert.Equal(expected, context.GetFrameFingerprint());
	}

	[Fact]
	public void UnrelatedChangingAudio_DoesNotMeetDefaultThreshold() {
		var source = Fingerprint(PartialClipAudioFixture.Generate(16));
		var unrelated = Fingerprint(PartialClipAudioFixture.Generate(8, 1735));
		Assert.True(ScanEngine.SlidingWindowCompare(unrelated, source).similarity < .8f);
	}

	static FileEntry Entry(ReadOnlySpan<short> pcm) {
		var context = new ChromaContext(); context.Start(); context.Feed(pcm); context.Finish();
		return new FileEntry { AudioFingerprint = context.GetRawFingerprint(), AudioFingerprintFrames = context.GetFrameFingerprint() };
	}

	[Fact]
	public void LongerExcerpts_AcrossFractionalPhasesAndSourceEnd_AreDetected() {
		short[] pcm = PartialClipAudioFixture.Generate(32);
		var source = Entry(pcm);
		foreach (double offset in new[] { .01, .06, .12, .5, .99, 1.25, 5.5, 10.125, 19.95, 20 }) {
			var clip = Entry(pcm.AsSpan((int)(offset * 11025), 12 * 11025));
			var (similarity, actualOffset) = ScanEngine.CompareAudioFingerprints(clip, source, .8f);
			Assert.True(similarity >= .8f, $"Excerpt at {offset}s scored {similarity:P2}");
			Assert.InRange(actualOffset, offset - .08, offset + .08);
		}
	}

	[Fact]
	public void LongerUnrelatedAudio_DoesNotMatchAcrossMultipleSeeds() {
		var source = Entry(PartialClipAudioFixture.Generate(32));
		foreach (uint seed in new uint[] { 17, 42, 501, 1735 }) {
			var clip = Entry(PartialClipAudioFixture.Generate(12, seed));
			Assert.True(ScanEngine.CompareAudioFingerprints(clip, source).similarity < .8f);
		}
	}

	[Fact]
	public void SampleExactExcerpt_WithLowLevelNoise_RemainsDetected() {
		short[] pcm = PartialClipAudioFixture.Generate(32);
		short[] clip = pcm.AsSpan((int)(5.5 * 11025), 12 * 11025).ToArray();
		var random = new Random(1735);
		for (int i = 0; i < clip.Length; i++) clip[i] += (short)random.Next(-300, 301);
		var found = ScanEngine.CompareAudioFingerprints(Entry(clip), Entry(pcm), .8f);
		Assert.True(found.similarity >= .8f);
		Assert.InRange(found.offsetSec, 5.42, 5.58);
	}

	[Fact]
	public void FineWindowDuration_TracksCoveredPcmRatherThanFeatureCountInSeconds() {
		var entry = Entry(PartialClipAudioFixture.Generate(12));
		Assert.InRange(ScanEngine.AudioFingerprintDurationSeconds(entry), 11.87, 12d);
	}

	[Fact]
	public void EmptyFineWindowCache_StillCapsVisualSamplingToLegacyAudioDuration() {
		var pcm = PartialClipAudioFixture.Generate(2);
		var entry = Entry(pcm.AsSpan(0, (int)(1.5 * 11025)));
		Assert.Empty(entry.AudioFingerprintFrames!);
		Assert.Equal(2, entry.AudioFingerprint!.Length);
		double duration = ScanEngine.AudioFingerprintDurationSeconds(entry);
		Assert.Equal(2d, duration);
		var times = ScanEngine.PartialClipVisualSampleTimes(60, 30, duration, 1);
		Assert.All(times, t => Assert.InRange(t, 0, 2));
	}
}
