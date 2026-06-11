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

using System.Diagnostics;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Benchmarks.Scenarios;

/// <summary>
/// Direct phase-timing probe for the duplicate-comparison phase
/// (<c>ScanEngine.ScanForDuplicates</c>) and the <c>HighlightBestMatches</c>
/// post-pass, using a fully synthetic in-memory corpus — no FFmpeg, no disk I/O.
/// Run with:
///
///   dotnet run -c Release --project VDF.Benchmarks -- --probe-compare
///
/// The corpus is built from a fixed seed, so besides the timings the printed
/// duplicate/group counts let a before/after comparison verify that an
/// optimization did not change the results.
/// </summary>
public static class ComparePhaseProbe {
	const int WarmupIterations = 1;
	const int Iterations = 5;
	const int ThumbnailCount = 2;

	public static int Run(string[] args) {
		Console.WriteLine("== Compare phase probe ==");
		Console.WriteLine($"cores: {Environment.ProcessorCount}, iterations: {Iterations} (+{WarmupIterations} warmup), thumbnails/video: {ThumbnailCount}");
		Console.WriteLine();

		// Wide duration spread: most pairs are rejected by the duration pre-filter,
		// so this measures bucket bookkeeping more than per-pair comparison cost.
		RunCompareScenario("videos-3000-linear-gray", count: 3000, usePHashing: false, durationMin: 30, durationSpread: 3570);
		RunCompareScenario("videos-8000-bucketed-gray", count: 8000, usePHashing: false, durationMin: 30, durationSpread: 3570);
		RunCompareScenario("videos-8000-bucketed-phash", count: 8000, usePHashing: true, durationMin: 30, durationSpread: 3570);
		// Dense duration cluster (e.g. a TV-series library, 20±2 min): nearly every
		// pair passes the duration filter, so CheckIfDuplicate dominates.
		RunCompareScenario("videos-6000-dense-gray", count: 6000, usePHashing: false, durationMin: 1200, durationSpread: 240);
		RunCompareScenario("videos-6000-dense-phash", count: 6000, usePHashing: true, durationMin: 1200, durationSpread: 240);
		RunHighlightScenario("highlight-20000-items-5000-groups", groupCount: 5000, groupSize: 4);
		return 0;
	}

	/// <summary>
	/// Builds a deterministic corpus: ~1 in 6 base entries spawns a duplicate
	/// cluster of 2-4 members whose gray bytes are the base pattern with ±2 noise
	/// (well above the 96% similarity threshold) and whose durations differ by
	/// less than 1% (well inside the duration tolerance). All other entries get
	/// independent random patterns, which sit at ~33% difference — guaranteed
	/// non-duplicates that still exercise the full comparison cost.
	/// </summary>
	static List<FileEntry> BuildCorpus(int count, Random rng, double durationMin = 30, double durationSpread = 3570) {
		var positions = new List<float>();
		float positionCounter = 0f;
		for (int i = 0; i < ThumbnailCount; i++) {
			positionCounter += 1.0f / (ThumbnailCount + 1);
			positions.Add(positionCounter);
		}

		var entries = new List<FileEntry>(count);
		int fileIndex = 0;
		while (entries.Count < count) {
			bool cluster = rng.Next(6) == 0;
			int members = cluster ? rng.Next(2, 5) : 1;
			double duration = durationMin + rng.NextDouble() * durationSpread;
			var basePatterns = new byte[ThumbnailCount][];
			for (int p = 0; p < ThumbnailCount; p++) {
				var pattern = new byte[1024];
				rng.NextBytes(pattern);
				basePatterns[p] = pattern;
			}

			for (int m = 0; m < members && entries.Count < count; m++) {
				var entry = new FileEntry {
					_Path = $@"C:\bench\folder{fileIndex % 100}\file{fileIndex:D6}.mp4",
					Folder = $@"C:\bench\folder{fileIndex % 100}",
					FileSize = 1_000_000 + rng.Next(1_000_000),
					mediaInfo = new MediaInfo {
						Duration = TimeSpan.FromSeconds(m == 0 ? duration : duration * (1.0 + (rng.NextDouble() - 0.5) * 0.01)),
						Streams = new[] {
							new MediaInfo.StreamInfo {
								CodecType = "video", CodecName = "h264",
								Width = 1280, Height = 720, FrameRate = 25, BitRate = 4_000_000
							}
						}
					},
					invalid = false
				};

				for (int p = 0; p < ThumbnailCount; p++) {
					byte[] data;
					if (m == 0) {
						data = basePatterns[p];
					}
					else {
						data = (byte[])basePatterns[p].Clone();
						for (int k = 0; k < data.Length; k++)
							data[k] = (byte)Math.Clamp(data[k] + rng.Next(-2, 3), 0, 255);
					}
					double key = entry.GetGrayBytesIndex(positions[p], 0d);
					entry.grayBytes[key] = data;
					entry.PHashes[key] = VDF.Core.pHash.PerceptualHash.ComputePHashFromGray32x32(data);
				}

				entries.Add(entry);
				fileIndex++;
			}
		}
		return entries;
	}

	static ScanEngine CreateEngine(bool usePHashing) {
		var engine = new ScanEngine();
		engine.Settings.ThumbnailCount = ThumbnailCount;
		engine.Settings.Percent = 96f;
		engine.Settings.MaxDegreeOfParallelism = Environment.ProcessorCount;
		engine.Settings.UsePHashing = usePHashing;
		engine.Settings.DatabaseCheckpointIntervalMinutes = 0;
		engine.EnsureThumbnailPositions();
		return engine;
	}

	static void RunCompareScenario(string name, int count, bool usePHashing, double durationMin, double durationSpread) {
		var entries = BuildCorpus(count, new Random(12345), durationMin, durationSpread);
		DatabaseUtils.Database.Clear();
		foreach (var entry in entries)
			DatabaseUtils.Database.Add(entry);

		var engine = CreateEngine(usePHashing);

		var times = new List<double>();
		int duplicateCount = 0, groupCount = 0;
		for (int it = 0; it < WarmupIterations + Iterations; it++) {
			var sw = Stopwatch.StartNew();
			engine.ScanForDuplicates();
			sw.Stop();
			if (it >= WarmupIterations)
				times.Add(sw.Elapsed.TotalMilliseconds);
			duplicateCount = engine.Duplicates.Count;
			groupCount = engine.Duplicates.Select(d => d.GroupId).Distinct().Count();
		}

		Report(name, times, $"duplicates={duplicateCount} groups={groupCount}");
		DatabaseUtils.Database.Clear();
	}

	static void RunHighlightScenario(string name, int groupCount, int groupSize) {
		var rng = new Random(999);
		var entries = BuildCorpus(groupCount * groupSize, rng);

		var engine = CreateEngine(usePHashing: false);
		var duplicates = new HashSet<DuplicateItem>();
		int idx = 0;
		for (int g = 0; g < groupCount; g++) {
			var groupId = Guid.NewGuid();
			for (int m = 0; m < groupSize; m++) {
				var item = new DuplicateItem(entries[idx++], (float)(rng.NextDouble() * 0.04), groupId, DuplicateFlags.None) {
					SizeLong = rng.Next(1_000_000, 100_000_000)
				};
				duplicates.Add(item);
			}
		}
		engine.Duplicates = duplicates;

		var times = new List<double>();
		for (int it = 0; it < WarmupIterations + Iterations; it++) {
			var sw = Stopwatch.StartNew();
			engine.HighlightBestMatches();
			sw.Stop();
			if (it >= WarmupIterations)
				times.Add(sw.Elapsed.TotalMilliseconds);
		}

		int bestSizeCount = duplicates.Count(d => d.IsBestSize);
		Report(name, times, $"items={duplicates.Count} bestSize={bestSizeCount}");
	}

	static void Report(string name, List<double> times, string verification) {
		times.Sort();
		Console.WriteLine($"{name,-36} median {times[times.Count / 2],9:F1} ms   (min {times[0]:F1}, max {times[^1]:F1})   {verification}");
	}
}
