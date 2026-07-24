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
			// Claim the phase BEFORE the prep work below. Scanning the database for eligible
			// videos and loading the keyframe sidecar are silent minutes on a large library,
			// and leaving the previous phase's label ("verifying partial clips") plus its
			// finished counters on screen made that read as a hang (#865, same lesson as #831).
			currentStageLabel = T("Scan.Stage.AiPrepare");
			InitProgress(1);

			var alreadyGrouped = BuildAlreadyGroupedPathSet();

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
			if (store.Count > 0)
				Logger.Instance.Info($"AI partial detection: keyframe cache loaded ({store.Count:N0} record(s)).");
			currentStageLabel = T("Scan.Stage.AiDenseSampling");
			InitProgress(videos.Count);
			var dense = new DenseEmbeddingStore.DenseRecord?[videos.Count];
			int extracted = 0, cached = 0, failed = 0;
			using (var embedder = new OnnxEmbedder(AiComponents.ModelPath)) {
				object embedLock = new();
				// Sampling keyframes for a large library runs for hours, and the sidecar used
				// to be written only after the very last file - so a crash, or the kill that
				// ends a run the user believes is hung, threw ALL of it away and the next scan
				// started from zero (#865). Checkpoint on the database's own interval instead.
				var storeCheckpoint = new PeriodicCheckpoint(TimeSpan.FromMinutes(Settings.DatabaseCheckpointIntervalMinutes));
				void TryCheckpointStore() =>
					storeCheckpoint.TryRun(() => {
						// No pruning mid-phase: records are still being added, and the keep-set
						// is only meaningful for the final save.
						store.Save(keepOnly: null);
						Logger.Instance.Info($"AI partial detection: keyframe cache checkpointed ({store.Count:N0} record(s)).");
					});
				void ProcessVideo(int i) {
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
						byte[][]? frames = FfmpegEngine.GetDenseAiFrames(entry.Path, interval, AiPartialMaxFramesPerFile,
							Settings.ExtendedFFToolsLogging, cancelationTokenSource.Token);
						if (frames == null) {
							Interlocked.Increment(ref failed);
							return;
						}
						bool[] usable = SelectUsableDenseFrames(frames);
						// Invalid slots (dark or duplicated frames) stay on the timeline as
						// empty arrays — never embedded, never matched.
						var embedded = new byte[frames.Length][];
						var batch = new List<byte[]>(OnnxEmbedder.MaxBatch);
						var batchSlots = new List<int>(OnnxEmbedder.MaxBatch);
						// Inference is serial (one session, CPU-bound) while other files decode.
						lock (embedLock) {
							for (int f = 0; f < frames.Length; f++) {
								embedded[f] = Array.Empty<byte>();
								if (!usable[f])
									continue;
								batch.Add(frames[f]);
								batchSlots.Add(f);
								if (batch.Count == OnnxEmbedder.MaxBatch)
									FlushBatch();
							}
							FlushBatch();

							void FlushBatch() {
								if (batch.Count == 0)
									return;
								byte[][] vectors = embedder.EmbedBatchQuantized(batch);
								for (int k = 0; k < vectors.Length; k++)
									embedded[batchSlots[k]] = vectors[k];
								batch.Clear();
								batchSlots.Clear();
							}
						}
						var record = new DenseEmbeddingStore.DenseRecord(info.Length, info.LastWriteTimeUtc.Ticks, (float)interval, embedded);
						store.Put(entry.Path, record);
						dense[i] = record;
						Interlocked.Increment(ref extracted);
					}
					catch (OperationCanceledException) {
						throw;
					}
					catch (Exception e) {
						Interlocked.Increment(ref failed);
						Logger.Instance.Warn($"AI partial detection: dense sampling failed for '{entry.Path}': {e.Message}");
					}
					finally {
						IncrementProgress(Path.GetFileName(entry.Path));
						TryCheckpointStore();
					}
				}

				try {
					// Per-drive concurrency (#857): this phase reads WHOLE files off disk
					// (GetDenseAiFrames decodes the full video), so it must respect the same
					// per-drive caps as the gather pass. The previous flat global degree let
					// every worker pile onto whatever drive the queue served next,
					// seek-thrashing spinning disks and network shares.
					List<DriveScanGroup> driveGroups = DriveScanPlanner.PartitionByDrive(videos);
					int[][] groupIndexes = DriveScanPlanner.MapEntryIndexes(videos, driveGroups);
					if (Settings.MaxDegreeOfParallelism == 1) {
						// Documented promise: 1 = strictly one file at a time (drives sequential).
						foreach (int[] indexes in groupIndexes)
							foreach (int i in indexes) {
								cancelationTokenSource.Token.ThrowIfCancellationRequested();
								ProcessVideo(i);
							}
					}
					else {
						DriveScanPlanner.ClassifyGroups(driveGroups, Settings.DriveTypeOverrides,
							DriveScanPlanner.IsNetworkRoot,
							group => DriveScanPlanner.ProbeSeekLatencyMs(group.Entries));
						DriveScanPlanner.AssignParallelism(driveGroups, Settings.MaxDegreeOfParallelism, Settings.HddMaxDegreeOfParallelism, Environment.ProcessorCount);
						var driveTasks = new List<Task>(driveGroups.Count);
						for (int g = 0; g < driveGroups.Count; g++) {
							DriveScanGroup group = driveGroups[g];
							int[] indexes = groupIndexes[g];
							Logger.Instance.Info($"AI partial detection: drive '{group.Root}': {indexes.Length:N0} video(s), concurrency {group.DegreeOfParallelism} ({(group.SpeedClass == DriveSpeedClass.Fast ? "fast" : "slow")}, {group.ClassSource})");
							driveTasks.Add(Task.Run(() => Parallel.ForEach(indexes, new ParallelOptions {
								CancellationToken = cancelationTokenSource.Token,
								MaxDegreeOfParallelism = group.DegreeOfParallelism
							}, ProcessVideo)));
						}
						Task.WaitAll(driveTasks.ToArray());
					}
				}
				catch (OperationCanceledException) { }
				catch (AggregateException ae) when (ae.Flatten().InnerExceptions.All(e => e is OperationCanceledException)) { }
			}
			// Keep-set = the whole database, NOT this scan's eligible videos: pruning to the
			// eligible set wiped other libraries' records on every alternating scan, and even
			// evicted videos the earlier passes had just grouped.
			// Its own stage: writing a multi-gigabyte sidecar is minutes of silence that
			// otherwise sat behind the sampling phase's completed counters (#865).
			currentStageLabel = T("Scan.Stage.AiPersist");
			InitProgress(1);
			store.Save(AllDatabasePaths());
			IncrementProgress(string.Empty);
			if (cancelationTokenSource.IsCancellationRequested)
				return;
			Logger.Instance.Info($"AI partial detection: dense embeddings ready for {videos.Count - failed} video(s) ({cached} cached, {extracted} computed, {failed} failed).");

			// ── Phase B: offset-consistent matching ─────────────────────────────
			float hitThreshold = Settings.AiPartialHitPercent / 100f;
			int hammingBound = EmbeddingMath.SignatureHammingBound(hitThreshold);
			// Sign signatures once per frame: the popcount prefilter in
			// TryMatchDenseFrames discards the vast majority of frame pairs at a
			// fraction of the exact dot product's cost.
			var signatures = new ulong[][]?[videos.Count];
			for (int i = 0; i < videos.Count; i++)
				if (dense[i] is { } record)
					signatures[i] = ComputeDenseSignatures(record);
			currentStageLabel = T("Scan.Stage.AiPartialCompare");
			InitProgress(Math.Max(videos.Count - 1, 1));
			(var matches, int pairsChecked) = CollectPartialMatchCandidates(videos,
				pairPrefilter: (i, j) => dense[i] != null && dense[j] != null,
				tryMatchPair: (i, j) =>
					TryMatchDenseFrames(dense[i]!, dense[j]!, signatures[i]!, signatures[j]!, hitThreshold, hammingBound, out float sim, out int offsetSec)
						? (sim, offsetSec)
						: null);
			if (cancelationTokenSource.IsCancellationRequested)
				return;

			var assignments = AssignPartialClipGroups(matches);
			EmitPartialClipAssignments(videos, assignments, DuplicateFlags.PartialClip | DuplicateFlags.AiMatched,
				(source, clip, sim, offsetSec) => Logger.Instance.Info(
					$"[AI-Partial] {Path.GetFileName(clip.Path)} in {Path.GetFileName(source.Path)}: sim={sim:P1} @ {offsetSec}s (hit threshold {Settings.AiPartialHitPercent:F0}%)"));
			Logger.Instance.Info($"AI partial detection: checked {pairsChecked} pair(s), found {matches.Count} candidate match(es), formed {assignments.Count} clip-source assignment(s).");
		}

		/// <summary>
		/// Marks the frames of a dense sweep that may participate in matching. Excluded:
		/// dark frames (they embed near-identically regardless of content — the union
		/// pass's black-frame guard, applied here) and frames byte-identical to their
		/// predecessor (the fps filter's round=up duplicates the previous keyframe across
		/// gaps, and identical frames would multiply one coincidental hit into a full
		/// evidence quorum). Excluded slots stay on the timeline so index↔time holds.
		/// </summary>
		internal static bool[] SelectUsableDenseFrames(byte[][] frames) {
			var usable = new bool[frames.Length];
			for (int f = 0; f < frames.Length; f++) {
				if (!GrayBytesUtils.VerifyRgbFrameValues(frames[f]))
					continue;
				if (f > 0 && frames[f].AsSpan().SequenceEqual(frames[f - 1]))
					continue;
				usable[f] = true;
			}
			return usable;
		}

		/// <summary>Sign signatures aligned with the record's frames; empty for invalid slots.</summary>
		internal static ulong[][] ComputeDenseSignatures(DenseEmbeddingStore.DenseRecord record) {
			var signatures = new ulong[record.Frames.Length][];
			for (int f = 0; f < record.Frames.Length; f++)
				signatures[f] = record.Frames[f].Length == 0
					? Array.Empty<ulong>()
					: EmbeddingMath.SignSignature(record.Frames[f]);
			return signatures;
		}

		/// <summary>Test/diagnostic convenience: computes signatures and bound on the fly.</summary>
		internal static bool TryMatchDenseFrames(
			DenseEmbeddingStore.DenseRecord source, DenseEmbeddingStore.DenseRecord clip,
			float hitThreshold, out float similarity, out int offsetSeconds) =>
			TryMatchDenseFrames(source, clip, ComputeDenseSignatures(source), ComputeDenseSignatures(clip),
				hitThreshold, EmbeddingMath.SignatureHammingBound(hitThreshold), out similarity, out offsetSeconds);

		/// <summary>
		/// Frame-level matching between two dense embedding timelines. A pair matches
		/// when at least <see cref="AiPartialMinConsistentHits"/> frame hits agree on one
		/// time offset (±<see cref="AiPartialOffsetToleranceSeconds"/> — keyframe timing
		/// jitter). Similarity is the mean cosine of the consistent hits; the offset is
		/// where the clip starts within the source.
		/// </summary>
		internal static bool TryMatchDenseFrames(
			DenseEmbeddingStore.DenseRecord source, DenseEmbeddingStore.DenseRecord clip,
			ulong[][] sourceSignatures, ulong[][] clipSignatures,
			float hitThreshold, int signatureHammingBound,
			out float similarity, out int offsetSeconds) {
			similarity = 0f;
			offsetSeconds = 0;
			List<(double offset, float cos)>? hits = null;
			for (int c = 0; c < clip.Frames.Length; c++) {
				byte[] clipFrame = clip.Frames[c];
				if (clipFrame.Length == 0)
					continue; // invalid slot (dark or duplicated frame)
				ulong[] clipSignature = clipSignatures[c];
				double clipTime = c * clip.IntervalSeconds;
				for (int s = 0; s < source.Frames.Length; s++) {
					if (source.Frames[s].Length == 0)
						continue;
					// Sign-LSH prefilter: pairs whose sign patterns differ too much cannot
					// reach the cosine threshold (see SignatureHammingBound) — skip the dot.
					if (EmbeddingMath.HammingDistance(clipSignature, sourceSignatures[s]) > signatureHammingBound)
						continue;
					float cos = EmbeddingMath.CosineSimilarity(clipFrame, source.Frames[s]);
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
