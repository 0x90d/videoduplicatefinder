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

using System.Linq;
using VDF.Core.AI;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	public sealed partial class ScanEngine {
		// Calibrated on real footage: true matches of re-encoded clips sit at cosine
		// 0.89-0.95, indistinguishable per-frame from same-scene noise — the
		// discriminator is several hits agreeing on ONE time offset.
		const int AiPartialMinConsistentHits = 4;
		const double AiPartialOffsetToleranceSeconds = 30;
		const int AiPartialMaxFramesPerFile = 400;

		/// <summary>
		/// Sampling interval for the dense pass: short clips sample every 5 s (a 75 s cut
		/// still yields ~15 query frames), long recordings settle at 15 s, and the frame
		/// cap keeps multi-hour files bounded.
		/// </summary>
		internal static double GetAiPartialIntervalSeconds(double durationSeconds) =>
			Math.Max(Math.Clamp(durationSeconds / 60d, 5d, 15d), durationSeconds / AiPartialMaxFramesPerFile);

		/// <summary>
		/// Visual partial/time-shift detection: matches dense keyframe embeddings by
		/// temporal offset consistency, so a trimmed or embedded clip finds its source
		/// with no audio required (works on silent, muted and re-dubbed copies — the
		/// cases the Chromaprint pass cannot cover). Runs after the audio pass; videos
		/// already grouped there (or by the visual duplicate scan) are skipped.
		/// </summary>
		internal void ScanForPartialDuplicatesVisual() {
			var alreadyGrouped = new HashSet<string>(
				Duplicates.Select(d => d.Path),
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			var videos = DatabaseUtils.Database
				.Where(e => !e.invalid && !e.IsImage &&
						(e.mediaInfo?.Duration.TotalSeconds ?? 0) >= 3 &&
						!alreadyGrouped.Contains(e.Path))
				.OrderByDescending(e => e.mediaInfo!.Duration)
				.ToList();

			if (videos.Count < 2) {
				Logger.Instance.Info("AI partial detection: fewer than 2 eligible videos, skipping.");
				return;
			}

			// ── Phase A: dense keyframe embeddings (sidecar-cached) ─────────────
			var store = DenseEmbeddingStore.Load();
			currentStageLabel = T("Scan.Stage.AiDenseSampling");
			InitProgress(videos.Count);
			var dense = new DenseEmbeddingStore.DenseRecord?[videos.Count];
			int extracted = 0, cached = 0, failed = 0;
			using (var embedder = new OnnxEmbedder(AiComponents.ModelPath)) {
				object embedLock = new();
				try {
					// Storage-tuned degree: this phase reads whole files off disk.
					Parallel.For(0, videos.Count, new ParallelOptions {
						CancellationToken = cancelationTokenSource.Token,
						MaxDegreeOfParallelism = ParallelDegree
					}, i => {
						if (!pauseTokenSource.TryWaitWhilePaused(cancelationTokenSource.Token))
							return;
						FileEntry entry = videos[i];
						try {
							var info = new FileInfo(entry.Path);
							if (!info.Exists)
								return;
							if (store.TryGet(entry.Path, info.Length, info.LastWriteTimeUtc.Ticks, out var cachedRecord)) {
								dense[i] = cachedRecord;
								Interlocked.Increment(ref cached);
								return;
							}
							double duration = entry.mediaInfo!.Duration.TotalSeconds;
							double interval = GetAiPartialIntervalSeconds(duration);
							byte[][]? frames = FfmpegEngine.GetDenseAiFrames(entry.Path, interval, AiPartialMaxFramesPerFile, Settings.ExtendedFFToolsLogging);
							if (frames == null) {
								Interlocked.Increment(ref failed);
								return;
							}
							// Inference is serial (one session, CPU-bound) while other files decode.
							byte[][] embedded;
							lock (embedLock) {
								var all = new List<byte[]>(frames.Length);
								for (int off = 0; off < frames.Length; off += OnnxEmbedder.MaxBatch)
									all.AddRange(embedder.EmbedBatchQuantized(frames.Skip(off).Take(OnnxEmbedder.MaxBatch).ToArray()));
								embedded = all.ToArray();
							}
							var record = new DenseEmbeddingStore.DenseRecord(info.Length, info.LastWriteTimeUtc.Ticks, (float)interval, embedded);
							store.Put(entry.Path, record);
							dense[i] = record;
							Interlocked.Increment(ref extracted);
						}
						catch (Exception e) {
							Interlocked.Increment(ref failed);
							Logger.Instance.Warn($"AI partial detection: dense sampling failed for '{entry.Path}': {e.Message}");
						}
						finally {
							IncrementProgress(Path.GetFileName(entry.Path));
						}
					});
				}
				catch (OperationCanceledException) { }
			}
			store.Save(videos.Select(v => v.Path).ToHashSet(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
			if (cancelationTokenSource.IsCancellationRequested)
				return;
			Logger.Instance.Info($"AI partial detection: dense embeddings ready for {videos.Count - failed} video(s) ({cached} cached, {extracted} computed, {failed} failed).");

			// ── Phase B: offset-consistent matching ─────────────────────────────
			float hitThreshold = Settings.AiPartialHitPercent / 100f;
			currentStageLabel = T("Scan.Stage.AiPartialCompare");
			InitProgress(Math.Max(videos.Count - 1, 1));
			var matches = new ConcurrentBag<(int sourceIdx, int clipIdx, float sim, int offsetSec)>();
			int pairsChecked = 0;
			try {
				Parallel.For(0, videos.Count - 1, new ParallelOptions {
					CancellationToken = cancelationTokenSource.Token,
					MaxDegreeOfParallelism = MatchingParallelDegree
				}, i => {
					if (!pauseTokenSource.TryWaitWhilePaused(cancelationTokenSource.Token))
						return;
					var source = dense[i];
					if (source == null) {
						IncrementProgress(Path.GetFileName(videos[i].Path));
						return;
					}
					double sourceSec = videos[i].mediaInfo!.Duration.TotalSeconds;
					for (int j = i + 1; j < videos.Count; j++) {
						if (cancelationTokenSource.IsCancellationRequested)
							break;
						var clip = dense[j];
						if (clip == null)
							continue;
						double clipSec = videos[j].mediaInfo!.Duration.TotalSeconds;
						// Same candidate prefilters as the audio pass.
						if (clipSec / sourceSec < Settings.PartialClipMinRatio)
							continue;
						if (clipSec / sourceSec >= 0.95)
							continue;
						Interlocked.Increment(ref pairsChecked);
						if (TryMatchDenseFrames(source, clip, hitThreshold, out float sim, out int offsetSec))
							matches.Add((i, j, sim, offsetSec));
					}
					IncrementProgress(Path.GetFileName(videos[i].Path));
				});
			}
			catch (OperationCanceledException) { }
			if (cancelationTokenSource.IsCancellationRequested)
				return;

			var assignments = AssignPartialClipGroups(matches);
			var addedSources = new HashSet<int>();
			foreach (var (si, ci, sim, offsetSec, groupId) in assignments) {
				FileEntry source = videos[si];
				FileEntry clip = videos[ci];
				if (Settings.ExtendedFFToolsLogging)
					Logger.Instance.Info($"[AI-Partial] {Path.GetFileName(clip.Path)} in {Path.GetFileName(source.Path)}: sim={sim:P1} @ {offsetSec}s (hit threshold {Settings.AiPartialHitPercent:F0}%)");
				if (addedSources.Add(si))
					Duplicates.Add(new DuplicateItem(source, 0f, groupId, DuplicateFlags.None));
				Duplicates.Add(new DuplicateItem(clip, 1f - sim, groupId, DuplicateFlags.PartialClip | DuplicateFlags.AiMatched) {
					PartialClipOffset = TimeSpan.FromSeconds(offsetSec)
				});
			}
			Logger.Instance.Info($"AI partial detection: checked {pairsChecked} pair(s), found {matches.Count} candidate match(es), formed {assignments.Count} clip-source assignment(s).");
		}

		/// <summary>
		/// Frame-level matching between two dense embedding timelines. A pair matches
		/// when at least <see cref="AiPartialMinConsistentHits"/> frame hits agree on one
		/// time offset (±<see cref="AiPartialOffsetToleranceSeconds"/> — keyframe timing
		/// jitter). Similarity is the mean cosine of the consistent hits; the offset is
		/// where the clip starts within the source.
		/// </summary>
		internal static bool TryMatchDenseFrames(
			DenseEmbeddingStore.DenseRecord source, DenseEmbeddingStore.DenseRecord clip,
			float hitThreshold, out float similarity, out int offsetSeconds) {
			similarity = 0f;
			offsetSeconds = 0;
			List<(double offset, float cos)>? hits = null;
			for (int c = 0; c < clip.Frames.Length; c++) {
				double clipTime = c * clip.IntervalSeconds;
				for (int s = 0; s < source.Frames.Length; s++) {
					float cos = EmbeddingMath.CosineSimilarity(clip.Frames[c], source.Frames[s]);
					if (cos < hitThreshold)
						continue;
					hits ??= new List<(double, float)>();
					hits.Add((s * source.IntervalSeconds - clipTime, cos));
				}
			}
			if (hits == null || hits.Count < AiPartialMinConsistentHits)
				return false;

			hits.Sort((a, b) => a.offset.CompareTo(b.offset));
			double median = hits[hits.Count / 2].offset;
			float sum = 0f;
			int consistent = 0;
			foreach ((double offset, float cos) in hits) {
				if (Math.Abs(offset - median) > AiPartialOffsetToleranceSeconds)
					continue;
				sum += cos;
				consistent++;
			}
			if (consistent < AiPartialMinConsistentHits)
				return false;
			similarity = sum / consistent;
			offsetSeconds = (int)Math.Max(0, Math.Round(median));
			return true;
		}
	}
}
