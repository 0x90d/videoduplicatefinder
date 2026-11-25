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
// #908: a partial clip passed "visual confirmation" at 0% because none of the sample
// times fell inside the source, and that case meant "audio alone decides".
// #925: a clip whose first (longest) candidate source failed the visual gate was lost,
// although a later candidate source would have passed.

using VDF.Core.FFTools;

namespace VDF.Core.Tests;

public class PartialClipVisualGateTests {
	static FileEntry Video(string name, double seconds, int fingerprintSeconds = 0) => new() {
		_Path = Path.Combine(Path.GetTempPath(), name),
		mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(seconds), Streams = Array.Empty<MediaInfo.StreamInfo>() },
		AudioFingerprint = fingerprintSeconds > 0 ? new uint[fingerprintSeconds] : null,
	};

	// ── #908: where the frames are sampled ──────────────────────────────

	[Fact]
	public void SampleTimes_StayInsideTheAudioMatchedWindow_WhenTheClipsAudioIsShorterThanItsVideo() {
		// The report's geometry: clip video 1656s, source 1898s, audio matched at 1840s.
		// Only a clip audio track of at most 58s can match there. Sampling at 25/50/75% of
		// the clip's VIDEO put all three samples past the end of the source.
		var times = ScanEngine.PartialClipVisualSampleTimes(sourceSec: 1898, clipSec: 1656, clipFingerprintSeconds: 50, offsetSec: 1840);

		Assert.Equal(3, times.Count);
		Assert.All(times, t => {
			Assert.InRange(t, 0, 50);
			Assert.True(1840 + t < 1898);
		});
	}

	[Fact]
	public void SampleTimes_AreEmpty_WhenNoVideoOverlapsTheMatch() {
		Assert.Empty(ScanEngine.PartialClipVisualSampleTimes(sourceSec: 100, clipSec: 50, clipFingerprintSeconds: 0, offsetSec: 100));
		Assert.Empty(ScanEngine.PartialClipVisualSampleTimes(sourceSec: 100, clipSec: 50, clipFingerprintSeconds: 0, offsetSec: 150));
	}

	[Fact]
	public void SampleTimes_UseTheWholeClip_WhenAudioAndVideoAgree() {
		var times = ScanEngine.PartialClipVisualSampleTimes(sourceSec: 600, clipSec: 120, clipFingerprintSeconds: 120, offsetSec: 60);
		Assert.Equal(new[] { 30.0, 60.0, 90.0 }, times);
	}

	[Fact]
	public void Verify_FailsAPairWhoseMatchedAudioHasNoVideoToCompare() {
		// Before the fix this returned true (and 0% similarity) without decoding anything.
		var engine = new ScanEngine();
		var source = Video("source.mp4", 100);
		var clip = Video("clip.mp4", 50);

		bool pass = engine.VerifyPartialClipVisually(source, clip, offsetSec: 100, out float visualSim);

		Assert.False(pass);
		Assert.Equal(0f, visualSim);
	}

	[Fact]
	public void Verify_StillKeepsPairsWithUnknownDurations() {
		// Unchanged: without durations nothing can be placed, and the audio decides.
		var engine = new ScanEngine();
		var source = new FileEntry { _Path = Path.Combine(Path.GetTempPath(), "s.mp4") };
		var clip = new FileEntry { _Path = Path.Combine(Path.GetTempPath(), "c.mp4") };

		Assert.True(engine.VerifyPartialClipVisually(source, clip, 0, out _));
	}

	// ── #925: a rejected first candidate hands the clip to the next one ──

	static readonly List<FileEntry> Videos = new() {
		Video("wrong_longer_source.mp4", 3000),
		Video("correct_source.mp4", 2000),
		Video("clip.mp4", 300),
		Video("other_clip.mp4", 200),
	};

	[Fact]
	public void ClipRejectedByItsFirstSource_IsGivenToTheNextCandidate() {
		var engine = new ScanEngine();
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 10),
			(sourceIdx: 1, clipIdx: 2, sim: 0.92f, offsetSec: 0),
		};

		var assignments = engine.AssignAndVerifyPartialClips(Videos, matches,
			(source, _, _, _) => source == Videos[1] ? (true, 0.9f) : (false, 0.53f));

		var only = Assert.Single(assignments);
		Assert.Equal(1, only.sourceIdx);
		Assert.Equal(2, only.clipIdx);
		Assert.Equal(0, only.offsetSec);
	}

	[Fact]
	public void EveryPairIsVerifiedAtMostOnce_AndPassingPairsKeepTheirGroup() {
		var engine = new ScanEngine();
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 10),
			(sourceIdx: 0, clipIdx: 3, sim: 0.95f, offsetSec: 20),
			(sourceIdx: 1, clipIdx: 2, sim: 0.92f, offsetSec: 0),
		};
		var verified = new List<(FileEntry, FileEntry)>();

		var assignments = engine.AssignAndVerifyPartialClips(Videos, matches, (source, clip, _, _) => {
			lock (verified)
				verified.Add((source, clip));
			return (!(source == Videos[0] && clip == Videos[2]), 0f);
		});

		Assert.Equal(3, verified.Count);
		Assert.Equal(verified.Count, verified.Distinct().Count());
		Assert.Equal(new[] { (0, 3), (1, 2) }, assignments.Select(a => (a.sourceIdx, a.clipIdx)).OrderBy(p => p).ToArray());
		// Source 0 keeps clip 3, source 1 gets clip 2: two groups, not one.
		Assert.Equal(2, assignments.Select(a => a.groupId).Distinct().Count());
	}

	[Fact]
	public void ClipFailingEveryCandidate_IsDropped() {
		var engine = new ScanEngine();
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 10),
			(sourceIdx: 1, clipIdx: 2, sim: 0.92f, offsetSec: 0),
		};
		int calls = 0;

		var assignments = engine.AssignAndVerifyPartialClips(Videos, matches, (_, _, _, _) => {
			Interlocked.Increment(ref calls);
			return (false, 0f);
		});

		Assert.Empty(assignments);
		Assert.Equal(2, calls);
	}

	[Fact]
	public void ClipFailingItsFirstTwoSources_EndsUpWithTheThird() {
		var videos = new List<FileEntry> {
			Video("s0.mp4", 3000), Video("s1.mp4", 2500), Video("s2.mp4", 2000), Video("clip.mp4", 300),
		};
		var engine = new ScanEngine();
		var matches = new[] {
			(sourceIdx: 2, clipIdx: 3, sim: 0.90f, offsetSec: 7),
			(sourceIdx: 0, clipIdx: 3, sim: 0.95f, offsetSec: 10),
			(sourceIdx: 1, clipIdx: 3, sim: 0.92f, offsetSec: 0),
		};
		var order = new List<int>();

		var assignments = engine.AssignAndVerifyPartialClips(videos, matches, (source, _, _, _) => {
			order.Add(videos.IndexOf(source));
			return (source == videos[2], 0.5f);
		});

		var only = Assert.Single(assignments);
		Assert.Equal((2, 3, 7), (only.sourceIdx, only.clipIdx, only.offsetSec));
		Assert.Equal(new[] { 0, 1, 2 }, order); // ascending source order, stops at the pass
	}

	[Fact]
	public void ClipFramesAreDecodedOnce_WhenItsCandidatesShareSampleTimes() {
		// Long sources and a 60s clip fingerprint: every candidate samples the same clip times.
		var videos = new List<FileEntry> {
			Video("s0.mp4", 3000), Video("s1.mp4", 2500), Video("s2.mp4", 2000), Video("clip.mp4", 300, fingerprintSeconds: 60),
		};
		var decodes = new List<string>();
		var engine = new ScanEngine {
			DecodeGrayFrames = (string path, IReadOnlyList<double> times, bool _, out FfmpegEngine.GrayFrameSource decodedBy) => {
				decodedBy = FfmpegEngine.GrayFrameSource.Native;
				lock (decodes)
					decodes.Add(Path.GetFileName(path));
				// Only the third source looks like the clip.
				byte value = path.EndsWith("s0.mp4") || path.EndsWith("s1.mp4") ? (byte)255 : (byte)0;
				return times.Select(_ => new byte[1024].Select(_ => value).ToArray()).ToArray();
			},
		};
		engine.Settings.UsePHashing = false;
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 3, sim: 0.95f, offsetSec: 100),
			(sourceIdx: 1, clipIdx: 3, sim: 0.92f, offsetSec: 200),
			(sourceIdx: 2, clipIdx: 3, sim: 0.90f, offsetSec: 300),
		};

		var assignments = engine.AssignAndVerifyPartialClips(videos, matches, (source, clip, offset, frames) => {
			bool pass = engine.VerifyPartialClipVisually(source, clip, offset, out float sim, frames);
			return (pass, sim);
		});

		Assert.Equal(2, Assert.Single(assignments).sourceIdx);
		Assert.Equal(1, decodes.Count(d => d == "clip.mp4"));
		Assert.Equal(new[] { "s0.mp4", "s1.mp4", "s2.mp4" }, decodes.Where(d => d != "clip.mp4").OrderBy(d => d).ToArray());
	}

	[Fact]
	public void ClipFramesAreDecodedAgain_WhenACandidateSamplesDifferentTimes() {
		// The second source ends 70s after the offset, so its window (and the times) is shorter.
		var videos = new List<FileEntry> {
			Video("s0.mp4", 3000), Video("s1.mp4", 1000), Video("clip.mp4", 300, fingerprintSeconds: 100),
		};
		int clipDecodes = 0;
		var engine = new ScanEngine {
			// Source 0 must fail so source 1 is reached: its frames differ from the clip's.
			DecodeGrayFrames = (string path, IReadOnlyList<double> times, bool _, out FfmpegEngine.GrayFrameSource decodedBy) => {
				decodedBy = FfmpegEngine.GrayFrameSource.Native;
				if (path.EndsWith("clip.mp4"))
					Interlocked.Increment(ref clipDecodes);
				byte value = path.EndsWith("s0.mp4") ? (byte)255 : (byte)0;
				return times.Select(_ => new byte[1024].Select(_ => value).ToArray()).ToArray();
			},
		};
		engine.Settings.UsePHashing = false;
		var matches = new[] {
			(sourceIdx: 0, clipIdx: 2, sim: 0.95f, offsetSec: 100),
			(sourceIdx: 1, clipIdx: 2, sim: 0.92f, offsetSec: 930),
		};

		var assignments = engine.AssignAndVerifyPartialClips(videos, matches, (source, clip, offset, frames) => {
			bool pass = engine.VerifyPartialClipVisually(source, clip, offset, out float sim, frames);
			return (pass, sim);
		});

		Assert.Equal(1, Assert.Single(assignments).sourceIdx);
		Assert.Equal(2, clipDecodes);
	}

	/// <summary>The rounds-based algorithm this gate replaced, kept as the reference.</summary>
	static List<(int sourceIdx, int clipIdx, float sim, int offsetSec)> AssignByRounds(
		List<(int sourceIdx, int clipIdx, float sim, int offsetSec)> candidates, Func<int, int, bool> passes) {
		var rejected = new HashSet<(int, int)>();
		while (true) {
			var assignments = ScanEngine.AssignPartialClipGroups(candidates.Where(m => !rejected.Contains((m.sourceIdx, m.clipIdx))));
			var failed = assignments.Where(a => !passes(a.sourceIdx, a.clipIdx)).ToList();
			if (failed.Count == 0)
				return assignments.Select(a => (a.sourceIdx, a.clipIdx, a.sim, a.offsetSec)).ToList();
			foreach (var a in failed)
				rejected.Add((a.sourceIdx, a.clipIdx));
		}
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(42)]
	public void MatchesTheRoundsAlgorithm_OnRandomCandidatesAndVerdicts(int seed) {
		var rng = new Random(seed);
		var videos = Enumerable.Range(0, 40).Select(i => Video($"v{i}.mp4", 1000 - i)).ToList();
		var candidates = new List<(int sourceIdx, int clipIdx, float sim, int offsetSec)>();
		for (int s = 0; s < 40; s++)
			for (int c = s + 1; c < 40; c++)
				if (rng.NextDouble() < 0.3)
					candidates.Add((s, c, (float)rng.NextDouble(), rng.Next(0, 500)));
		candidates = candidates.OrderBy(_ => rng.Next()).ToList();
		var verdict = new Dictionary<(int, int), bool>();
		foreach (var m in candidates)
			verdict[(m.sourceIdx, m.clipIdx)] = rng.NextDouble() < 0.3;
		var engine = new ScanEngine();

		var actual = engine.AssignAndVerifyPartialClips(videos, candidates,
			(source, clip, _, _) => (verdict[(videos.IndexOf(source), videos.IndexOf(clip))], 0f));
		var expected = AssignByRounds(candidates, (s, c) => verdict[(s, c)]);

		Assert.NotEmpty(expected);
		Assert.Equal(expected, actual.Select(a => (a.sourceIdx, a.clipIdx, a.sim, a.offsetSec)).ToList());
		// Same grouping: one group id per source, shared by exactly that source's clips.
		Assert.Equal(actual.Select(a => a.sourceIdx).Distinct().Count(), actual.Select(a => a.groupId).Distinct().Count());
		Assert.All(actual.GroupBy(a => a.sourceIdx), g => Assert.Single(g.Select(a => a.groupId).Distinct()));
	}
}
