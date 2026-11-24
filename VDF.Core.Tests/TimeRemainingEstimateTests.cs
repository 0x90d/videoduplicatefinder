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
// Regression tests for issue #831: a partial-clip scan looked hung. The audio pass
namespace VDF.Core.Tests;

/// <summary>
/// The time-remaining estimate extrapolates the time spent so far. It counted paused time as
/// work, and counted items whose cost differs by orders of magnitude as equal, so it was far off
/// in exactly the phases that take long: the partial-clip pass and a rescan's analysis.
/// </summary>
public class TimeRemainingEstimateTests {
	[Fact]
	public void ByWork_ExtrapolatesTheCostLeft() {
		// A quarter of the cost took a minute: three more minutes, however many items that is.
		Assert.Equal(TimeSpan.FromMinutes(3), ScanEngine.EstimateRemainingByWork(TimeSpan.FromMinutes(1), workDone: 250, workTotal: 1000));
	}

	[Fact]
	public void ByWork_WithNothingDoneYet_FallsBackToTheItemCount() =>
		Assert.Null(ScanEngine.EstimateRemainingByWork(TimeSpan.FromMinutes(1), workDone: 0, workTotal: 1000));

	[Fact]
	public void ByWork_WhenAllCostIsDone_IsZero() =>
		Assert.Equal(TimeSpan.Zero, ScanEngine.EstimateRemainingByWork(TimeSpan.FromMinutes(1), workDone: 1000, workTotal: 1000));

	[Fact]
	public void PhaseClock_StandsStillWhilePaused() {
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();
		engine.InitProgress(10, "test");
		Thread.Sleep(50);
		engine.IncrementProgress("a");
		engine.ElapsedTimer.Stop(); // what Pause does
		TimeSpan before = engine.EstimateRemaining(1, 10);

		Thread.Sleep(300);

		Assert.True(before > TimeSpan.Zero);
		Assert.Equal(before, engine.EstimateRemaining(1, 10));
	}

	[Fact]
	public void WeightedPhase_UsesTheCostOfTheFinishedItems() {
		// Nine cheap items done, one expensive left: counting items would say almost done.
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();
		engine.InitProgress(10, "test", workTotal: 1009);
		for (int i = 0; i < 9; i++)
			engine.IncrementProgress("cheap", work: 1);
		Thread.Sleep(20);
		TimeSpan phaseElapsed = engine.ElapsedTimer.Elapsed;
		TimeSpan remaining = engine.EstimateRemaining(9, 10);

		// About 1000/9 times what was spent so far, far beyond the item count's 1/9.
		Assert.True(remaining > phaseElapsed * 50, $"remaining {remaining} for elapsed {phaseElapsed}");
	}

	static FileEntry Video(string name, double seconds, int blocks) => new() {
		_Path = Path.Combine(Path.GetTempPath(), name),
		mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(seconds) },
		AudioFingerprint = new uint[blocks],
	};

	[Fact]
	public void PartialRowWork_MatchesASumOverEachRowsCandidates() {
		var rng = new Random(7);
		// Sorted longest first, like the partial pass.
		var videos = Enumerable.Range(0, 300)
			.Select(k => { int s = rng.Next(1, 4000); return Video($"v{k}.mp4", s, s + rng.Next(-5, 6) is var b && b > 1 ? b : 2); })
			.OrderByDescending(v => v.mediaInfo!.Duration)
			.ToList();
		var covered = Enumerable.Range(0, videos.Count).Select(_ => rng.Next(3) == 0).ToArray();
		const double minRatio = 0.10;

		long[] work = ScanEngine.EstimatePartialRowWork(videos, minRatio, k => covered[k]);

		for (int i = 0; i < videos.Count - 1; i++) {
			double source = videos[i].mediaInfo!.Duration.TotalSeconds;
			double li = videos[i].AudioFingerprint!.Length;
			double expected = 0;
			for (int j = i + 1; j < videos.Count; j++) {
				double ratio = videos[j].mediaInfo!.Duration.TotalSeconds / source;
				if (ratio >= 0.95) continue;
				if (ratio < minRatio) break;
				if (covered[i] && covered[j]) continue; // answered by the cache
				double lj = videos[j].AudioFingerprint!.Length;
				expected += (li + 1) * lj - lj * lj;
			}
			Assert.InRange(work[i], (long)(Math.Max(0, expected) / 1024) - 1, (long)(Math.Max(0, expected) / 1024) + 1);
		}
	}

	[Fact]
	public void PartialRowWork_IsHeaviestForTheLongestSources() {
		var videos = Enumerable.Range(0, 200)
			.Select(k => Video($"v{k}.mp4", 4000 - k * 19, 4000 - k * 19))
			.ToList();

		long[] work = ScanEngine.EstimatePartialRowWork(videos, 0.10, _ => false);

		// Counting rows treats the first and the 100th alike; their real cost differs many times over.
		Assert.True(work[0] > 5 * work[100], $"first {work[0]}, middle {work[100]}");
	}

	[Fact]
	public void NeedsAnalysis_SeparatesCachedFilesFromOnesThatMustBeRead() {
		var engine = new ScanEngine();
		string folder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
		engine.Settings.IncludeList.Add(folder);
		engine.Settings.EnablePartialClipDetection = false;
		engine.positionList.AddRange(new[] { 0.25f, 0.75f });
		FileEntry Entry(string name) => new() { _Path = Path.Combine(folder, name), Folder = folder };

		var cached = Entry("cached.mp4");
		cached.mediaInfo = new MediaInfo { Duration = TimeSpan.FromMinutes(1) };
		foreach (float position in engine.positionList)
			cached.grayBytes[cached.GetGrayBytesIndex(position, engine.Settings.MaxSamplingDurationSeconds)] = new byte[32 * 32];
		var fresh = Entry("new.mp4");
		var elsewhere = new FileEntry { _Path = "/somewhere/else/x.mp4", Folder = "/somewhere/else" };

		Assert.False(engine.NeedsAnalysis(cached));
		Assert.True(engine.NeedsAnalysis(fresh));
		Assert.False(engine.NeedsAnalysis(elsewhere)); // out of scope: skipped in microseconds

		var partial = Entry("partial.mp4"); // one sampled position is missing
		partial.mediaInfo = cached.mediaInfo;
		partial.grayBytes[partial.GetGrayBytesIndex(engine.positionList[0], engine.Settings.MaxSamplingDurationSeconds)] = new byte[32 * 32];
		Assert.True(engine.NeedsAnalysis(partial));

		// Skipped by InvalidEntry's disk-free checks, so no read: they must not add to the estimate.
		var image = Entry("photo.jpg");
		image.IsImage = true;
		engine.Settings.IncludeImages = true;
		Assert.True(engine.NeedsAnalysis(image));
		engine.Settings.IncludeImages = false;
		Assert.False(engine.NeedsAnalysis(image));

		var blacklisted = Entry("excluded.mp4");
		Assert.True(engine.NeedsAnalysis(blacklisted));
		engine.Settings.BlackList.Add(folder);
		Assert.False(engine.NeedsAnalysis(blacklisted));
		engine.Settings.BlackList.Clear();

		engine.Settings.EnablePartialClipDetection = true; // now the audio fingerprint is missing
		Assert.True(engine.NeedsAnalysis(cached));
	}
}
