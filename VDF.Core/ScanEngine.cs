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

global using System;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.IO;
global using System.Threading;
global using System.Threading.Tasks;
global using Size = System.Drawing.Size;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text.Json;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	public sealed partial class ScanEngine {
		public HashSet<DuplicateItem> Duplicates { get; set; } = new HashSet<DuplicateItem>();
		public Settings Settings { get; set; } = new Settings();
		public event EventHandler<ScanProgressChangedEventArgs>? Progress;
		public event EventHandler? BuildingHashesDone;
		public event EventHandler? ScanDone;
		public event EventHandler? ScanAborted;
		public event EventHandler? ThumbnailsRetrieved;
		public event Action<int, int>? ThumbnailProgress;
		public event EventHandler? FilesEnumerated;
		public event EventHandler? DatabaseCleaned;

		/// <summary>Encoded placeholder image (PNG/JPEG bytes) shown when thumbnail extraction fails.</summary>
		public byte[]? NoThumbnailImage;

		internal PauseTokenSource pauseTokenSource = new();
		CancellationTokenSource cancelationTokenSource = new();
		internal readonly List<float> positionList = new();
		// Live only while a scan's hashing phase runs (see StartSearch): receives decoded
		// 224x224 RGB frames and writes int8 embeddings into the union embedding sidecar.
		AI.EmbeddingPipeline? aiEmbeddingPipeline;
		// The union pass's embedding sidecar. Loaded when an AI-enabled scan starts (or
		// lazily for a compare-only run), saved after the hashing phase, and released once
		// the compare snapshots are built — embeddings only occupy memory while needed.
		internal AI.UnionEmbeddingStore? unionEmbeddingStore;

		public ScanEngine() =>
			// Armed and disarmed by the isScanning setter, so an idle engine owns no ticking timer.
			progressHeartbeat = new Timer(_ => HeartbeatTick(), null,
											Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

		bool _isScanning;
		// The main process yields CPU to foreground apps while a scan runs, restored the
		// instant scanning ends — hooked on the setter so EVERY exit path (done/abort/stop
		// via CancelAllTasks) restores. BelowNormal only cedes under contention, so an
		// unattended scan still runs at full speed. Best-effort.
		bool isScanning {
			get => _isScanning;
			set {
				if (_isScanning == value) return;
				_isScanning = value;
				progressHeartbeat.Change(
					value ? progressHeartbeatIntervall : Timeout.InfiniteTimeSpan,
					value ? progressHeartbeatIntervall : Timeout.InfiniteTimeSpan);
				if (!value)
					lock (progressSnapshotLock)
						hasProgressSnapshot = false; // a late tick must not repaint a finished scan
				try {
					using var p = Process.GetCurrentProcess();
					p.PriorityClass = value ? ProcessPriorityClass.BelowNormal
											: ProcessPriorityClass.Normal;
				}
				catch { /* priority is a nicety; never let it break a scan */ }
			}
		}
		int scanProgressMaxValue;
		readonly Stopwatch SearchTimer = new();
		public Stopwatch ElapsedTimer = new();
		int processedFiles;
		DateTime startTime = DateTime.Now;
		DateTime lastProgressUpdate = DateTime.MinValue;
		static readonly TimeSpan progressUpdateIntervall = TimeSpan.FromMilliseconds(300);
		// Elapsed and Remaining only ever reach a frontend on a Progress event, and a phase can
		// run for minutes without completing a single file — the partial-clip visual gate decodes
		// frames for a handful of assignments, one slow source stalling the counter. The whole
		// status block (counter, ETA, stage label and clock) then sat frozen while the scan ran
		// on, which reads as a hang (#831). The heartbeat re-sends the last snapshot with a live
		// clock whenever real progress has gone quiet.
		static readonly TimeSpan progressHeartbeatIntervall = TimeSpan.FromMilliseconds(500);
		readonly Timer progressHeartbeat;
		// ScanProgressChangedEventArgs is a struct: the heartbeat thread reads the snapshot while
		// worker threads write it, so both sides go through the lock to avoid a torn copy.
		readonly object progressSnapshotLock = new();
		ScanProgressChangedEventArgs lastProgressSnapshot;
		bool hasProgressSnapshot;
		const int maxExcludedLogsPerReason = 5;
		readonly ConcurrentDictionary<string, int> excludedReasonCounts = new();
		readonly ConcurrentDictionary<string, int> excludedReasonLoggedCounts = new();
		// Files whose stored pHash for the comparison position is null. Dedupes the
		// per-pair log spam from #754: one bad file otherwise produces a line per
		// candidate it's compared against (thousands of lines from a handful of files).
		readonly ConcurrentDictionary<string, byte> missingPHashFiles = new(
			CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		DateTime lastCheckpointTime = DateTime.MinValue;
		readonly object checkpointLock = new();
		// Per-drive done/total accounting; non-null only while GatherInfos runs, so progress
		// events of every other phase carry Drives = null and the UI hides the drive rows.
		DriveProgressTracker? driveProgressTracker;
		// True between StartSearch beginning a log session and the chained StartCompare
		// joining it; lets a standalone StartCompare open its own session instead.
		bool compareIsChainedToSearch;

		string T(string key, params object[] args) =>
			LanguageService.Instance.Get(Settings.LanguageCode, key, args);

		internal void InitProgress(int count) {
			startTime = DateTime.UtcNow;
			scanProgressMaxValue = count;
			processedFiles = 0;
			lastCheckpointTime = DateTime.UtcNow;
			driveProgressTracker = null; // compare phases re-init progress; they have no per-drive data
			// Publish the new phase's zeroed counters at once. A phase whose first item takes minutes
			// (the visual gate decodes frames off disk) would otherwise leave the previous phase's
			// finished-looking numbers on screen, and leave the heartbeat with nothing to refresh.
			// Callers set currentStageLabel before calling, so the label switches with the counters.
			PushProgress(new ScanProgressChangedEventArgs {
				CurrentPosition = 0,
				CurrentFile = string.Empty,
				Elapsed = ElapsedTimer.Elapsed,
				Remaining = TimeSpan.Zero,
				MaxPosition = scanProgressMaxValue,
				CurrentStage = currentStageLabel,
			});
			// After the push, so the phase's first completed item reports without waiting out the throttle.
			lastProgressUpdate = DateTime.MinValue;
		}
		void ResetExcludedLogging() {
			excludedReasonCounts.Clear();
			excludedReasonLoggedCounts.Clear();
		}
		// ParallelOptions.MaxDegreeOfParallelism rejects 0 but accepts -1 (unlimited).
		// Only 0 needs correcting — clamping with Math.Max(1, ...) turned the -1 default
		// into single-threaded execution.
		int ParallelDegree => Settings.MaxDegreeOfParallelism == 0 ? -1 : Settings.MaxDegreeOfParallelism;

		/// <summary>
		/// Resolves the worker count for the CPU-bound matching phases, independent of
		/// <see cref="Settings.MaxDegreeOfParallelism"/> (a media-READ knob tuned to
		/// storage — throttling it for an HDD must not serialize the compare phase,
		/// and its -1/"unlimited" sentinel is likewise a poor fit for pure CPU work).
		/// A positive configured value is honored up to the logical CPU count;
		/// 0 or negative selects automatic: most of the machine, minus one or two
		/// reserved cores so the UI and the system stay responsive.
		/// </summary>
		internal static int CalculateMatchingParallelism(int configured, int processorCount) {
			processorCount = Math.Max(1, processorCount);
			if (configured > 0)
				return Math.Min(configured, processorCount);

			int reservedProcessors = processorCount >= 8 ? 2 : processorCount >= 2 ? 1 : 0;
			int reserveCap = Math.Max(1, processorCount - reservedProcessors);
			int percentageCap = Math.Max(1, (int)Math.Ceiling(processorCount * 0.80d));
			return Math.Min(reserveCap, percentageCap);
		}

		int MatchingParallelDegree => CalculateMatchingParallelism(Settings.MatchingMaxDegreeOfParallelism, Environment.ProcessorCount);

		// The pHash quorum's requiredMatches is identical for every pair in a scan
		// (sampleCount always equals positionList.Count), so the compare phase precomputes
		// it once here and the per-pair hot path in CheckIfDuplicate reads it instead of
		// re-clamping and re-ceiling on every candidate. Null outside a scan — the direct
		// and diagnostic callers fall back to computing it locally.
		int? matchingRequiredSampleMatches;

		// Status-bar label for the current phase. Empty during per-file analysis (which reports
		// its own sub-stages via ReportStage); set by the compare phases so the UI shows
		// "comparing …" instead of leaving the last analyzed file path on screen, which looked
		// like a frozen analysis.
		string currentStageLabel = string.Empty;
		void LogExcludedFile(FileEntry entry, string reason) {
			if (!Settings.LogExcludedFiles)
				return;
			var totalCount = excludedReasonCounts.AddOrUpdate(reason, 1, (_, count) => count + 1);
			var loggedCount = excludedReasonLoggedCounts.GetOrAdd(reason, 0);
			if (loggedCount >= maxExcludedLogsPerReason)
				return;
			loggedCount = excludedReasonLoggedCounts.AddOrUpdate(reason, 1, (_, count) => count + 1);
			if (loggedCount <= maxExcludedLogsPerReason)
				Logger.Instance.Warn(T("Log.ExcludedFile", entry.Path, reason, totalCount));
		}
		void LogExcludedSummary() {
			if (!Settings.LogExcludedFiles || excludedReasonCounts.IsEmpty)
				return;
			Logger.Instance.Warn(T("Log.ExcludedFilesSummary"));
			foreach (var reason in excludedReasonCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) {
				var loggedCount = excludedReasonLoggedCounts.TryGetValue(reason.Key, out var value) ? value : 0;
				var suppressedCount = Math.Max(0, reason.Value - loggedCount);
				var suppressionText = suppressedCount > 0 ? T("Log.ExcludedFilesSuppressed", suppressedCount) : string.Empty;
				Logger.Instance.Warn(T("Log.ExcludedFilesSummaryItem", reason.Key, reason.Value, suppressionText));
			}
		}
		/// <summary>
		/// Raises <see cref="Progress"/> and keeps the snapshot the heartbeat re-sends.
		/// </summary>
		void PushProgress(ScanProgressChangedEventArgs args) {
			lock (progressSnapshotLock) {
				lastProgressSnapshot = args;
				hasProgressSnapshot = true;
			}
			Progress?.Invoke(this, args);
		}

		/// <summary>
		/// Linear extrapolation of the current phase's remaining time from what it has spent so far.
		/// Clamped at zero: the final item's push has processed == maxPosition, which drove the old
		/// expression negative — rendering as "~0s left" while the phase was still working (#831).
		/// </summary>
		internal static TimeSpan EstimateRemaining(TimeSpan phaseElapsed, int processed, int maxPosition) {
			int remaining = maxPosition - (processed + 1);
			if (remaining <= 0 || processed < 0 || phaseElapsed <= TimeSpan.Zero)
				return TimeSpan.Zero;
			return TimeSpan.FromTicks(phaseElapsed.Ticks * remaining / (processed + 1));
		}

		TimeSpan EstimateRemaining(int processed, int maxPosition) =>
			EstimateRemaining(DateTime.UtcNow.Subtract(startTime), processed, maxPosition);

		/// <summary>
		/// <see cref="progressHeartbeat"/>'s callback. The worker-thread pushes raise Progress inside
		/// the scan task, where StartSearch/StartCompare own the catch; this one runs on a threadpool
		/// thread, where a subscriber's exception would take the process down instead. The clock is
		/// cosmetic — never let it end a scan.
		/// </summary>
		internal void HeartbeatTick() {
			try {
				EmitProgressHeartbeat();
			}
			catch (Exception ex) {
				Logger.Instance.Warn($"Progress heartbeat failed (the scan continues): {ex.Message}");
			}
		}

		/// <summary>
		/// Re-raises the last progress snapshot with a fresh clock and re-estimated remaining time.
		/// Fires off <see cref="progressHeartbeat"/> while a scan is active, and stays quiet while
		/// real progress is already flowing or the scan is paused (Pause stops ElapsedTimer, so the
		/// clock is meant to stand still).
		/// </summary>
		internal void EmitProgressHeartbeat() {
			if (!ElapsedTimer.IsRunning) return;
			if (lastProgressUpdate + progressUpdateIntervall > DateTime.UtcNow) return;
			ScanProgressChangedEventArgs snapshot;
			lock (progressSnapshotLock) {
				if (!hasProgressSnapshot) return;
				snapshot = lastProgressSnapshot;
			}
			// Deliberately does not touch lastProgressUpdate: a heartbeat must never delay or
			// suppress the next real push from a worker.
			snapshot.Elapsed = ElapsedTimer.Elapsed;
			snapshot.Remaining = EstimateRemaining(snapshot.CurrentPosition, snapshot.MaxPosition);
			Progress?.Invoke(this, snapshot);
		}

		internal void IncrementProgress(string path) {
			// Atomic: workers of all concurrent drive groups increment this counter, and a
			// torn increment would lose the processedFiles == scanProgressMaxValue final push.
			int processed = Interlocked.Increment(ref processedFiles);
			var pushUpdate = processed == scanProgressMaxValue ||
								lastProgressUpdate + progressUpdateIntervall < DateTime.UtcNow;
			if (!pushUpdate) return;
			lastProgressUpdate = DateTime.UtcNow;
			PushProgress(new ScanProgressChangedEventArgs {
				CurrentPosition = processed,
				CurrentFile = path,
				Elapsed = ElapsedTimer.Elapsed,
				Remaining = EstimateRemaining(processed, scanProgressMaxValue),
				MaxPosition = scanProgressMaxValue,
				CurrentStage = currentStageLabel,
				Drives = driveProgressTracker?.Snapshot(),
			});
			TryDatabaseCheckpoint();
		}

		// Reports what's happening to a file mid-processing without advancing the file counter.
		// Throttled to the same cadence as IncrementProgress so a stuck file's last-reported
		// stage (e.g. "sampling frame 2/5") hints at where it froze.
		void ReportStage(string path, string stage, int stageCurrent = 0, int stageMax = 0) {
			if (lastProgressUpdate + progressUpdateIntervall > DateTime.UtcNow) return;
			lastProgressUpdate = DateTime.UtcNow;
			PushProgress(new ScanProgressChangedEventArgs {
				CurrentPosition = processedFiles,
				CurrentFile = path,
				Elapsed = ElapsedTimer.Elapsed,
				Remaining = EstimateRemaining(processedFiles, scanProgressMaxValue),
				MaxPosition = scanProgressMaxValue,
				CurrentStage = stage,
				StageCurrent = stageCurrent,
				StageMax = stageMax,
				Drives = driveProgressTracker?.Snapshot(),
			});
		}

		void TryDatabaseCheckpoint() {
			if (Settings.DatabaseCheckpointIntervalMinutes <= 0) return;
			var interval = TimeSpan.FromMinutes(Settings.DatabaseCheckpointIntervalMinutes);
			if (DateTime.UtcNow - lastCheckpointTime < interval) return;
			lock (checkpointLock) {
				// Re-check after acquiring lock to avoid duplicate saves from racing threads
				if (DateTime.UtcNow - lastCheckpointTime < interval) return;
				lastCheckpointTime = DateTime.UtcNow;
				// A checkpoint is best-effort: it runs on a worker thread inside the
				// hashing/compare loops, several of which only catch
				// OperationCanceledException. A failed periodic save must not abort the
				// whole scan — the final end-of-scan save is the one that has to succeed.
				try {
					DatabaseUtils.SaveDatabase();
					Logger.Instance.Info(T("Log.DatabaseCheckpoint", DatabaseUtils.Database.Count));
				}
				catch (Exception ex) {
					Logger.Instance.Warn($"Database checkpoint failed (the scan continues; the final save still runs): {ex}");
				}
			}
		}

		// Explicit flush for a safe suspend point (Pause): persist completed work so the user can
		// close the app while paused and resume later via the fingerprint cache. Shares
		// checkpointLock so it never races a periodic checkpoint or the final save over the temp
		// database file. Best-effort; files finishing during the pause land in the next save.
		void FlushDatabase() {
			lock (checkpointLock) {
				lastCheckpointTime = DateTime.UtcNow;
				try {
					DatabaseUtils.SaveDatabase();
					Logger.Instance.Info("Paused: database flushed — safe to close the app (a later rescan resumes from the cache).");
				}
				catch (Exception ex) {
					Logger.Instance.Warn($"Pause flush failed (the scan continues): {ex}");
				}
			}
		}

		public static bool FFmpegExists => !string.IsNullOrEmpty(FfmpegEngine.FFmpegPath);
		public static bool FFprobeExists => !string.IsNullOrEmpty(FFProbeEngine.FFprobePath);
		public static bool NativeFFmpegExists => FFTools.FFmpegNative.FFmpegHelper.DoFFmpegLibraryFilesExist;

		/// <param name="searchAndCompare">
		/// When true (GUI/Web default) the search chains straight into <see cref="StartCompare"/>.
		/// Callers that drive the two phases separately — the CLI runs hashing and comparison as
		/// distinct awaitable steps — must pass false, otherwise compare runs twice and the two
		/// concurrent <see cref="DatabaseUtils.SaveDatabase"/> calls race over the temp database
		/// file (#803).
		/// </param>
		public async void StartSearch(bool searchAndCompare = true) {
			try {
				PrepareSearch();
				SearchTimer.Start();
				ElapsedTimer.Start();
				Logger.Instance.BeginSession(T("Log.SessionScan"));
				compareIsChainedToSearch = true;
				Logger.Instance.Info(T("Log.BuildingFileList"));
				await BuildFileList(cancelationTokenSource.Token);
				Logger.Instance.Info(T("Log.FinishedBuildingFileList", SearchTimer.StopGetElapsedAndRestart()));
				FilesEnumerated?.Invoke(this, new EventArgs());
				try {
					if (!cancelationTokenSource.IsCancellationRequested && Settings.UseAiMatching) {
						unionEmbeddingStore = AI.UnionEmbeddingStore.Load();
						aiEmbeddingPipeline = new AI.EmbeddingPipeline(AI.AiComponents.ModelPath, unionEmbeddingStore, cancelationTokenSource.Token);
					}
					Logger.Instance.Info(T("Log.GatheringMediaInfo"));
					if (!cancelationTokenSource.IsCancellationRequested)
						await GatherInfos();
					if (aiEmbeddingPipeline != null) {
						// The bounded queue keeps this drain short, but on a slow CPU a few
						// hundred frames can still be pending — give the wait its own stage.
						currentStageLabel = T("Scan.Stage.AiEmbedding");
						InitProgress(1);
						await aiEmbeddingPipeline.CompleteAsync();
						IncrementProgress(string.Empty);
						Logger.Instance.Info($"AI embeddings computed for this scan: {aiEmbeddingPipeline.EmbeddedCount}");
					}
				}
				finally {
					aiEmbeddingPipeline?.Dispose();
					aiEmbeddingPipeline = null;
					// Persist whatever got embedded — also on cancel/error, the work is
					// expensive. The store stays loaded for the chained compare phase.
					unionEmbeddingStore?.Save(AllDatabasePaths());
				}
				Logger.Instance.Info(T("Log.FinishedGatheringHashes", SearchTimer.StopGetElapsedAndRestart()));
				// Save before signaling completion: consumers (e.g. the CLI) may treat the
				// event as "done" and exit the process, which previously killed this thread
				// mid-write and left a torn ScannedFiles_new.db behind.
				// Under checkpointLock: a pause-flush runs on a background task and could
				// otherwise still be writing the temp database file when a quick Stop lets
				// the scan reach this save (#803-style race).
				lock (checkpointLock)
					DatabaseUtils.SaveDatabase();
				BuildingHashesDone?.Invoke(this, new EventArgs());
				if (!cancelationTokenSource.IsCancellationRequested) {
					if (searchAndCompare)
						StartCompare();
					else
						isScanning = false; // search-only: no StartCompare to clear it
				}
				else {
					ScanAborted?.Invoke(this, new EventArgs());
					Logger.Instance.Info(T("Log.ScanAborted"));
					isScanning = false;
				}
			}
			catch (Exception ex) {
				AbortScanOnError(ex);
			}
		}

		public async void StartCompare() {
			try {
				// Standalone compare runs (GUI compare-only rescan, CLI compare command) get
				// their own log session; a compare chained to StartSearch stays in the scan's.
				if (!compareIsChainedToSearch)
					Logger.Instance.BeginSession(T("Log.SessionCompare"));
				compareIsChainedToSearch = false;
				PrepareCompare();
				SearchTimer.Start();
				ElapsedTimer.Start();
				Logger.Instance.Info(T("Log.ScanForDuplicates"));
				if (!cancelationTokenSource.IsCancellationRequested)
					await Task.Run(ScanForDuplicates, cancelationTokenSource.Token);
				if (!cancelationTokenSource.IsCancellationRequested && Settings.EnablePartialClipDetection)
					await Task.Run(ScanForPartialDuplicates, cancelationTokenSource.Token);
				// After the audio pass: videos it already grouped are excluded, the visual
				// pass only adds pairs audio could not see (no/replaced audio, music-only).
				if (!cancelationTokenSource.IsCancellationRequested && Settings.EnableAiPartialDetection)
					await Task.Run(ScanForPartialDuplicatesVisual, cancelationTokenSource.Token);
				if (cancelationTokenSource.IsCancellationRequested) {
					// The passes swallow their internal OperationCanceledExceptions, so a
					// Stop pressed during any of them would otherwise fall through to
					// ScanDone and present partial results as a completed scan.
					AbortScanOnError(new OperationCanceledException());
					return;
				}
				SearchTimer.Stop();
				ElapsedTimer.Stop();
				Logger.Instance.Info(T("Log.FinishedScanForDuplicates", SearchTimer.Elapsed));
				LogGroupStatistics();
				Logger.Instance.Info(T("Log.HighlightingBestResults"));
				HighlightBestMatches();
				// Save before signaling completion — see the matching comments in StartSearch.
				lock (checkpointLock)
					DatabaseUtils.SaveDatabase();
				isScanning = false;
				ScanDone?.Invoke(this, new EventArgs());
				Logger.Instance.Info(T("Log.ScanDone"));
			}
			catch (Exception ex) {
				AbortScanOnError(ex);
			}
		}

		/// <summary>
		/// Terminates a scan whose task died on an exception. StartSearch/StartCompare are
		/// async void, so anything escaping them lands on the UI thread's
		/// SynchronizationContext instead of a caller's catch block: the app survived, but
		/// isScanning stayed true, ScanDone/ScanAborted never fired and Stop had no task
		/// left to cancel — the GUI sat on "Stopping all scan threads..." until the user
		/// killed the process (#821, an OutOfMemoryException during a database checkpoint).
		/// No database save here: after an unknown failure the in-memory database may not
		/// be loaded yet (e.g. PrepareSearch threw), and persisting it would overwrite the
		/// user's good database file with an empty one. Hashing progress is already covered
		/// by the post-hash save and the periodic checkpoints.
		/// </summary>
		void AbortScanOnError(Exception ex) {
			compareIsChainedToSearch = false;
			if (ex is OperationCanceledException)
				Logger.Instance.Info(T("Log.ScanAborted"));
			else
				Logger.Instance.Error($"Scan aborted because of an unexpected error: {ex}");
			SearchTimer.Stop();
			ElapsedTimer.Stop();
			isScanning = false;
			ScanAborted?.Invoke(this, new EventArgs());
		}

		void PrepareSearch() {
			ResetExcludedLogging();
			//Using VDF.GUI we know fftools exist at this point but VDF.Core might be used in other projects as well
			if (!Settings.UseNativeFfmpegBinding && !FFmpegExists)
				throw new FFNotFoundException("Cannot find FFmpeg");
			if (!FFprobeExists)
				throw new FFNotFoundException("Cannot find FFprobe");
			if (Settings.UseNativeFfmpegBinding && !FFTools.FFmpegNative.FFmpegHelper.DoFFmpegLibraryFilesExist)
				throw new FFNotFoundException($"Cannot find FFmpeg libraries. {FFTools.FFmpegNative.FFmpegHelper.DescribeExpectedLibraries()}");
			// Fail fast with an actionable message instead of erroring on every file later.
			if (Settings.UseAiMatching || Settings.EnableAiPartialDetection)
				AI.AiComponents.EnsureReady();

			CancelAllTasks();

			FfmpegEngine.HardwareAccelerationMode = Settings.HardwareAccelerationMode;
			FfmpegEngine.CustomFFArguments = Settings.CustomFFArguments;
			FfmpegEngine.UseNativeBinding = Settings.UseNativeFfmpegBinding;
			DatabaseUtils.CustomDatabaseFolder = Settings.CustomDatabaseFolder;
			DatabaseUtils.InvalidateDatabaseFolder();
			Duplicates.Clear();
			positionList.Clear();
			ElapsedTimer.Reset();
			SearchTimer.Reset();

			BuildPositionList();
			NormalizeScanPaths();

			isScanning = true;
		}

		void BuildPositionList() {
			positionList.Clear();
			float positionCounter = 0f;
			for (int i = 0; i < Settings.ThumbnailCount; i++) {
				positionCounter += 1.0F / (Settings.ThumbnailCount + 1);
				positionList.Add(positionCounter);
			}
		}

		/// <summary>
		/// FileEntry.Folder is always an absolute path without a trailing separator, but the
		/// include/blacklist entries arrive as typed (CLI flags, Web text fields, JSON settings).
		/// A relative path or trailing slash made the StartsWith inclusion check silently skip
		/// every database entry — scans found 0 duplicates with no hint why (issue #790).
		/// </summary>
		void NormalizeScanPaths() {
			static HashSet<string> Normalize(HashSet<string> paths) {
				var result = new HashSet<string>();
				foreach (var path in paths) {
					string normalized = path;
					try {
						normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
					}
					catch { /* keep the original string if it cannot be resolved */ }
					result.Add(normalized);
				}
				return result;
			}
			Settings.IncludeList = Normalize(Settings.IncludeList);
			Settings.BlackList = Normalize(Settings.BlackList);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		double GetGrayBytesIndex(FileEntry entry, float position) =>
			entry.GetGrayBytesIndex(position, Settings.MaxSamplingDurationSeconds);

		/// <summary>
		/// Keep-set for the AI sidecar caches: every path the scan database knows. Sidecar
		/// records are pruned only when their file left the database entirely — pruning by
		/// the current scan's eligible set would wipe other libraries' expensive embeddings
		/// on every alternating scan.
		/// </summary>
		static HashSet<string> AllDatabasePaths() =>
			DatabaseUtils.Database.Select(e => e.Path).ToHashSet(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		void PrepareCompare() {
			if (positionList.Count == 0) {
				// Fresh process running compare-only (CLI 'compare' on an existing database):
				// the list is built during PrepareSearch, which never ran here (issue #790).
				BuildPositionList();
			}
			else if (Settings.ThumbnailCount != positionList.Count) {
				throw new Exception("Number of thumbnails can't be changed between quick rescans! Rescan has been aborted.");
			}
			NormalizeScanPaths();
			if (DatabaseUtils.Database.Count == 0) {
				// Also a compare-only concern: the database is normally loaded by
				// StartSearch's BuildFileList, which never ran in this process (issue #790).
				DatabaseUtils.CustomDatabaseFolder = Settings.CustomDatabaseFolder;
				DatabaseUtils.InvalidateDatabaseFolder();
				DatabaseUtils.LoadDatabase();
				// The invalid flag is not persisted and defaults to true; it is normally
				// cleared per entry by StartSearch's hashing pass. Without this pass a
				// compare-only run sees every imported entry as invalid (0 files compared).
				foreach (FileEntry entry in DatabaseUtils.Database) {
					entry.invalid = InvalidEntry(entry, out _, out string? reason);
					if (entry.invalid && reason != null)
						LogExcludedFile(entry, reason);
				}
			}

			CancelAllTasks();

			Duplicates.Clear();
			SearchTimer.Reset();
			if (!ElapsedTimer.IsRunning)
				ElapsedTimer.Reset();

			isScanning = true;
		}

		void CancelAllTasks() {
			if (!cancelationTokenSource.IsCancellationRequested)
				cancelationTokenSource.Cancel();
			cancelationTokenSource = new CancellationTokenSource();
			pauseTokenSource = new PauseTokenSource();
			isScanning = false;
		}

		Task BuildFileList(CancellationToken cancellationToken) => Task.Run(() => {

			DatabaseUtils.LoadDatabase();
			if (DatabaseUtils.DbVersion < 2)
				Settings.UsePHashing = false;

			int oldFileCount = DatabaseUtils.Database.Count;

			// Index existing analysed entries by size so a path-miss below can be checked for being a
			// MOVE (same content fingerprint, old path now gone) and relinked — reusing its analysis
			// instead of re-decoding. Only OsHash-bearing entries are relink targets; keyed by size so
			// we compute the new file's oshash only when a same-size analysed entry exists (zero reads
			// on a fresh scan, where the DB is empty).
			var relinkBySize = new Dictionary<long, List<FileEntry>>();
			foreach (var e in DatabaseUtils.Database)
				if (e.OsHash != null) {
					if (!relinkBySize.TryGetValue(e.FileSize, out var lst))
						relinkBySize[e.FileSize] = lst = new List<FileEntry>();
					lst.Add(e);
				}
			int relinkedCount = 0;

			foreach (string path in Settings.IncludeList) {
				if (cancellationToken.IsCancellationRequested)
					return;
				if (!Directory.Exists(path)) {
					// A disconnected network drive or removed folder would otherwise be
					// skipped without a trace, making the scan look broken (0 files found).
					Logger.Instance.Warn($"Search directory not found or inaccessible, skipping: '{path}'. If this is a network drive, make sure it is connected (or use the \\\\server\\share UNC path instead of a drive letter).");
					continue;
				}

				foreach (FileInfo file in FileUtils.GetFilesRecursive(path, Settings.IgnoreReadOnlyFolders, Settings.IgnoreReparsePoints,
					Settings.IncludeSubDirectories, Settings.IncludeImages, Settings.BlackList.ToList(), cancellationToken)) {
					if (cancellationToken.IsCancellationRequested)
						return;
					FileEntry fEntry;
					try {
						fEntry = new(file);
					}
					catch (Exception e) {
						//https://github.com/0x90d/videoduplicatefinder/issues/237
						Logger.Instance.Warn($"Skipped file '{file}' because of {e}");
						continue;
					}
					if (!DatabaseUtils.Database.TryGetValue(fEntry, out var dbEntry)) {
						// Path not in the DB: either a genuinely new file or one moved/renamed from a
						// path that's now gone. Relink the latter so its analysis survives the move.
						if (TryRelinkMovedFile(fEntry, relinkBySize))
							relinkedCount++;
						else
							DatabaseUtils.Database.Add(fEntry);
					}
					else
						RefreshExistingEntry(fEntry, dbEntry);
				}
			}

			Logger.Instance.Info($"Files in database: {DatabaseUtils.Database.Count:N0} ({DatabaseUtils.Database.Count - oldFileCount:N0} files added)");
			if (relinkedCount > 0)
				Logger.Instance.Info($"Detected {relinkedCount:N0} moved/renamed file(s) — reused existing analysis (no re-decode)");
		});

		// A path that is already in the database: decide whether its cached analysis survives this
		// rescan. Size changed -> content changed -> re-analyze. Same size but timestamps moved is
		// usually a touch/copy/restore or a container-only rewrite with identical bytes, and
		// re-decoding those wastes hours on big libraries — keep the cached analysis when the content
		// fingerprint PROVES the bytes unchanged. Anything unverifiable (either hash missing, file
		// unreadable, or a pre-OsHash entry not yet backfilled) re-analyzes exactly as before.
		internal static void RefreshExistingEntry(FileEntry fEntry, FileEntry dbEntry) {
			if (fEntry.FileSize != dbEntry.FileSize) {
				DatabaseUtils.Database.Remove(dbEntry);
				DatabaseUtils.Database.Add(fEntry);
			}
			else if (fEntry.DateCreated != dbEntry.DateCreated ||
					fEntry.DateModified != dbEntry.DateModified) {
				string? osHash = OsHashUtils.TryCompute(fEntry.Path);
				if (osHash != null && osHash == dbEntry.OsHash) {
					// Same bytes, just re-dated: keep the analysis and refresh the timestamps
					// so the next scan doesn't re-verify.
					dbEntry.DateCreated = fEntry.DateCreated;
					dbEntry.DateModified = fEntry.DateModified;
				}
				else {
					DatabaseUtils.Database.Remove(dbEntry);
					DatabaseUtils.Database.Add(fEntry);
				}
			}
		}

		// Returns true if fEntry is a moved/renamed version of an existing analysed entry — same size
		// and content fingerprint (oshash), and that entry's recorded path no longer exists — in which
		// case the existing entry is re-keyed to the new path, preserving grayBytes/mediaInfo/PHashes so
		// GatherInfos skips re-decoding it. Ambiguous matches (0 or >1 missing candidates with the same
		// oshash) fall through to "new file" so we never reuse the wrong data.
		internal bool TryRelinkMovedFile(FileEntry fEntry, Dictionary<long, List<FileEntry>> relinkBySize) {
			if (!relinkBySize.TryGetValue(fEntry.FileSize, out var sameSize))
				return false;
			// A move source is an entry whose recorded path is now gone. (A still-present path means it's
			// a copy, not a move — leave it and treat the new path as a new file.)
			List<FileEntry>? missing = null;
			foreach (var c in sameSize)
				if (!File.Exists(c.Path))
					(missing ??= new List<FileEntry>()).Add(c);
			if (missing == null)
				return false;

			string? oshash = OsHashUtils.TryCompute(fEntry.Path);
			if (oshash == null)
				return false;

			FileEntry? match = null;
			foreach (var c in missing)
				if (c.OsHash == oshash) {
					if (match != null)
						return false;   // more than one candidate with this fingerprint -> ambiguous, treat as new
					match = c;
				}
			if (match == null)
				return false;

			// Re-key the surviving entry to the new path. Its analysis rides along untouched; only the
			// path/date/size are refreshed so a later rescan at the new path won't flag it as modified.
			string oldPath = match.Path;
			DatabaseUtils.Database.Remove(match);
			match.Path = fEntry.Path;
			match.DateCreated = fEntry.DateCreated;
			match.DateModified = fEntry.DateModified;
			match.FileSize = fEntry.FileSize;
			DatabaseUtils.Database.Add(match);
			Logger.Instance.Info($"Moved file relinked (analysis reused): '{oldPath}' -> '{match.Path}'");
			return true;
		}

		// Check if entry should be excluded from the scan for any reason
		// Returns true if the entry is invalid (should be excluded)
		internal bool InvalidEntry(FileEntry entry, out bool reportProgress, out string? reason) {
			reportProgress = true;
			reason = null;

			if (Settings.IncludeImages == false && entry.IsImage) {
				reason = "image files are disabled";
				return true;
			}
			if (Settings.BlackList.Any(f => IsBlackListed(entry.Folder, f))) {
				reason = "path is in the excluded directories list";
				return true;
			}

			/* Skip non-included file before checking if it exists
			 * This greatly improves performance if the file is on
			 * a disconnected network/mobile drive
			 */
			if (!Settings.ScanAgainstEntireDatabase && !IsInIncludeScope(entry)) {
				reportProgress = false;
				reason = "path is not in the included directories list";
				return true;
			}

			if (entry.Flags.Has(EntryFlags.ManuallyExcluded)) {
				reason = "file has been manually excluded";
				return true;
			}
			if (entry.Flags.Has(EntryFlags.TooDark)) {
				reason = "file is marked as too dark";
				return true;
			}
			if (!Settings.IncludeMissingFiles && !File.Exists(entry.Path))
			{
				reason = "file does not exist";
				return true;
			}
			if (!FileUtils.IsPathFFmpegSafe(entry.Path)) {
				entry.Flags.Set(EntryFlags.MetadataError);
				reason = "path contains characters not encodable to UTF-8 (e.g. lone surrogate from a mangled emoji) — FFmpeg cannot open it";
				return true;
			}

			if (Settings.FilterByFileSize && (entry.FileSize.BytesToMegaBytes() > Settings.MaximumFileSize ||
				entry.FileSize.BytesToMegaBytes() < Settings.MinimumFileSize)) {
				reason = "file size is outside the configured range";
				return true;
			}
			if (Settings.FilterByFilePathContains) {
				bool contains = false;
				foreach (var f in Settings.FilePathContainsTexts) {
					if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(f, entry.Path)) {
						contains = true;
						break;
					}
				}
				if (!contains) {
					reason = "file path does not match the required patterns";
					return true;
				}
			}

			if (Settings.IgnoreReparsePoints) {
				// The flag is stamped at FileEntry creation; entries from databases written
				// before it existed get a one-time stat here and carry the result forward.
				if (!entry.Flags.Has(EntryFlags.ReparsePointChecked)) {
					try {
						FileAttributes attributes = File.GetAttributes(entry.Path);
						entry.Flags.Set(EntryFlags.ReparsePoint, (attributes & FileAttributes.ReparsePoint) != 0);
						entry.Flags.Set(EntryFlags.ReparsePointChecked);
					}
					catch { } // missing/inaccessible file — the existence check above already covers it
				}
				if (entry.Flags.Has(EntryFlags.ReparsePoint)) {
					reason = "file is a reparse point";
					return true;
				}
			}
			if (Settings.FilterByFilePathNotContains) {
				bool contains = false;
				foreach (var f in Settings.FilePathNotContainsTexts) {
					if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(f, entry.Path)) {
						contains = true;
						break;
					}
				}
				if (contains) {
					reason = "file path matches an excluded pattern";
					return true;
				}
			}

			return false;
		}
		bool InvalidEntryForDuplicateCheck(FileEntry entry) =>
			entry.invalid || entry.mediaInfo == null || entry.Flags.Has(EntryFlags.ThumbnailError) || (!entry.IsImage && entry.grayBytes.Count < Settings.ThumbnailCount);

		public static Task<bool> LoadDatabase() => Task.Run(DatabaseUtils.LoadDatabase);
		/// <summary>
		/// Loads the database from a custom folder. Callers with a configured custom
		/// database folder must use this at startup — the parameterless overload resolves
		/// the DEFAULT folder, and the custom one only took effect at the first scan
		/// (PrepareSearch), so every pre-scan consumer (backup restore's tombstone checks,
		/// database viewer, entry counts) read the wrong database.
		/// </summary>
		public static Task<bool> LoadDatabase(string? customDatabaseFolder) => Task.Run(() => {
			DatabaseUtils.CustomDatabaseFolder = string.IsNullOrEmpty(customDatabaseFolder) ? null : customDatabaseFolder;
			DatabaseUtils.InvalidateDatabaseFolder();
			return DatabaseUtils.LoadDatabase();
		});
		public static void SaveDatabase() => DatabaseUtils.SaveDatabase();
		public static void RemoveFromDatabase(FileEntry dbEntry) => DatabaseUtils.Database.Remove(dbEntry);

		// A DB entry can outlive its file. We tell an intentional deletion from a temporarily
		// offline drive by the file's ROOT: drive mounted but file gone = the user deleted it
		// (a "tombstone" whose fingerprint is kept when RememberDeletedContent is on, so a
		// re-download is recognized); drive itself absent (USB unplugged, letter reassigned) =
		// merely offline, must NOT count as deleted. UNC/unrooted paths can't be probed ->
		// conservative: offline, never a tombstone.
		public static bool IsDriveReady(string path) {
			try {
				string? root = Path.GetPathRoot(path);
				if (string.IsNullOrEmpty(root))
					return false;
				return new DriveInfo(root).IsReady;
			}
			catch {
				return false;
			}
		}
		public static bool PathIsTombstone(string path) => !File.Exists(path) && IsDriveReady(path);
		public static bool PathIsOffline(string path) => !File.Exists(path) && !IsDriveReady(path);

		public static void UpdateFilePathInDatabase(string newPath, FileEntry dbEntry) => DatabaseUtils.UpdateFilePath(newPath, dbEntry);
#pragma warning disable CS8601 // Possible null reference assignment
		public static bool GetFromDatabase(string path, out FileEntry? dbEntry) {
			if (!File.Exists(path)) {
				dbEntry = null;
				return false;
			}
			return DatabaseUtils.Database.TryGetValue(new FileEntry(path), out dbEntry);
		}
#pragma warning restore CS8601 // Possible null reference assignment
		public static void BlackListFileEntry(string filePath) => DatabaseUtils.BlacklistFileEntry(filePath);

		// Returns true if folderPath is covered by blacklistEntry.
		// Supports wildcard patterns (*, ?) in blacklistEntry — see https://github.com/0x90d/videoduplicatefinder/issues/582
		static bool IsBlackListed(string folderPath, string blacklistEntry) {
			bool hasWildcard = blacklistEntry.IndexOfAny(['*', '?']) >= 0;
			if (!hasWildcard) {
				if (!folderPath.StartsWith(blacklistEntry, StringComparison.OrdinalIgnoreCase))
					return false;
				if (folderPath.Length == blacklistEntry.Length)
					return true;
				//Reason: https://github.com/0x90d/videoduplicatefinder/issues/249
				string relativePath = Path.GetRelativePath(blacklistEntry, folderPath);
				return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
			}
			// Wildcard pattern without path separators: match against each individual segment of folderPath
			bool hasSeparator = blacklistEntry.Contains(Path.DirectorySeparatorChar) ||
			                    blacklistEntry.Contains(Path.AltDirectorySeparatorChar);
			if (!hasSeparator) {
				string[] segments = folderPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
					StringSplitOptions.RemoveEmptyEntries);
				return segments.Any(s => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(blacklistEntry, s));
			}
			// Wildcard pattern with path separators: match against the full path
			return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(blacklistEntry, folderPath);
		}

		// True if the entry's folder is covered by the current include list (honours IncludeSubDirectories).
		// Shared by the scan scope filters and the OsHash backfill so out-of-scope drives are never read.
		bool IsInIncludeScope(FileEntry entry) {
			if (!Settings.IncludeSubDirectories)
				return Settings.IncludeList.Contains(entry.Folder);
			return Settings.IncludeList.Any(f => {
				if (!entry.Folder.StartsWith(f))
					return false;
				if (entry.Folder.Length == f.Length)
					return true;
				//Reason: https://github.com/0x90d/videoduplicatefinder/issues/249
				string relativePath = Path.GetRelativePath(f, entry.Folder);
				return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
			});
		}

		async Task GatherInfos() {
			try {
				currentStageLabel = string.Empty; // per-file analysis reports its own sub-stages
				InitProgress(DatabaseUtils.Database.Count);
				// Only in-scope entries count toward a drive's done/total — out-of-scope ones
				// are skipped in microseconds and would otherwise dilute the drive bars.
				bool CountsTowardDriveProgress(FileEntry entry) =>
					Settings.ScanAgainstEntireDatabase || IsInIncludeScope(entry);
				void CompleteEntry(FileEntry entry, DriveProgressTracker.Counter driveCounter) {
					if (CountsTowardDriveProgress(entry))
						driveCounter.Complete(entry.FileSize);
					IncrementProgress(entry.Path);
				}
				ValueTask ProcessEntry(FileEntry entry, DriveProgressTracker.Counter driveCounter, CancellationToken token) {
					if (!pauseTokenSource.TryWaitWhilePaused(token))
						return ValueTask.CompletedTask; // canceled while paused — the loop token ends the iteration

					try {
						entry.invalid = InvalidEntry(entry, out bool reportProgress, out string? invalidReason);
						if (entry.invalid && invalidReason != null)
							LogExcludedFile(entry, invalidReason);

						bool wasInvalid = entry.invalid;
						bool skipEntry = false;
						string? skipReason = null;
						skipEntry |= entry.invalid;
						if (!skipEntry && entry.Flags.Has(EntryFlags.ThumbnailError) && !Settings.AlwaysRetryFailedSampling) {
							skipEntry = true;
							skipReason = "previous thumbnail sampling failed and retry is disabled";
						}

						if (!skipEntry && !Settings.ScanAgainstEntireDatabase && !IsInIncludeScope(entry)) {
							skipEntry = true;
							skipReason = "path is not in the included directories list";
						}

						if (skipEntry) {
							entry.invalid = true;
							if (!wasInvalid && skipReason != null)
								LogExcludedFile(entry, skipReason);
							if (reportProgress)
								CompleteEntry(entry, driveCounter);
							return ValueTask.CompletedTask;
						}

						// Cache a cheap content fingerprint so a future scan can detect this file was
						// MOVED (same OsHash, old path gone) and relink it without re-decoding. Runs once
						// per entry — computed here for new files and backfilled for pre-OsHash entries,
						// then persisted. Best-effort: a missing/locked file leaves it null.
						// Only fingerprint files inside the include list, so "scan against entire database"
						// (which compares every historical entry) never spins up out-of-scope drives for a read.
						if (entry.OsHash == null && IsInIncludeScope(entry))
							entry.OsHash = OsHashUtils.TryCompute(entry.Path);

						if (Settings.IncludeMissingFiles && entry.grayBytes?.Count > 0) {
							bool hasAllInformation = entry.IsImage;
							if (!hasAllInformation) {
								hasAllInformation = true;
								for (int i = 0; i < positionList.Count; i++) {
									if (entry.grayBytes.ContainsKey(GetGrayBytesIndex(entry, positionList[i])))
										continue;
									hasAllInformation = false;
									break;
								}
							}
							// A gray-complete entry may still need embedding backfill — but only
							// when the file is actually readable; tombstones (deleted files kept
							// for comparison) stay fully-cached and the AI pass abstains for them.
							if (hasAllInformation && aiEmbeddingPipeline != null && File.Exists(entry.Path)) {
								if (entry.IsImage) {
									hasAllInformation = !aiEmbeddingPipeline.WantsEmbedding(entry, 0);
								}
								else {
									for (int i = 0; i < positionList.Count && hasAllInformation; i++)
										hasAllInformation = !aiEmbeddingPipeline.WantsEmbedding(entry, GetGrayBytesIndex(entry, positionList[i]));
								}
							}
							if (hasAllInformation) {
								// Thumbnails are cached but audio fingerprint might still be needed
								if (Settings.EnablePartialClipDetection &&
									!entry.IsImage &&
									!entry.Flags.Has(EntryFlags.NoAudioTrack) &&
									!entry.Flags.Has(EntryFlags.AudioFingerprintError) &&
									!entry.Flags.Has(EntryFlags.SilentAudioTrack) &&
									entry.AudioFingerprint == null) {
									string cachedAudioPath = entry.Path;
									string audioStageLabel = T("Scan.Stage.AudioFingerprint");
									ReportStage(cachedAudioPath, audioStageLabel);
									ExtractAudioFingerprint(entry, cancelationTokenSource.Token,
										onProgress: p => ReportStage(cachedAudioPath, audioStageLabel, (int)(p * 100), 100));
								}
								CompleteEntry(entry, driveCounter);
								return ValueTask.CompletedTask;
							}
						}

						// Tombstone/offline safety: the file is gone (deleted, or its drive is
						// unmounted). A fully-cached entry was already kept above; one with
						// incomplete cached data cannot be (re)analysed without the file, so
						// exclude it from this scan instead of spawning ffprobe/ffmpeg on a
						// missing path (which only errors).
						if (!File.Exists(entry.Path)) {
							entry.invalid = true;
							CompleteEntry(entry, driveCounter);
							return ValueTask.CompletedTask;
						}

						if (entry.mediaInfo == null && !entry.IsImage) {
							ReportStage(entry.Path, T("Scan.Stage.Probing"));
							MediaInfo? info = FFProbeEngine.GetMediaInfo(entry.Path, Settings.ExtendedFFToolsLogging);
							if (info == null) {
								entry.invalid = true;
								entry.Flags.Set(EntryFlags.MetadataError);
								CompleteEntry(entry, driveCounter);
								return ValueTask.CompletedTask;
							}

							entry.mediaInfo = info;
						}
						// This is for people upgrading from an older VDF version
						// Or if you create a new database, start and immediately stop the scan and then try to scan again
						entry.grayBytes ??= new Dictionary<double, byte[]?>();
						entry.PHashes ??= new Dictionary<double, ulong?>();


						if (entry.IsImage) {
							if (entry.grayBytes.Count == 0) {
								if (!GetGrayBytesFromImage(entry, Settings.UseExifCreationDate, Settings.ExtendedFFToolsLogging, aiEmbeddingPipeline))
									entry.invalid = true;
							}
							else {
								// Gray bytes cached from an earlier scan — backfill just the embedding.
								TryQueueImageEmbeddingFrame(entry, aiEmbeddingPipeline, Settings.ExtendedFFToolsLogging);
							}
						}
						else {
							string entryPath = entry.Path;
							int totalSamples = positionList.Count;
							string samplingLabel = T("Scan.Stage.SamplingFrames");
							if (!FfmpegEngine.GetGrayBytesFromVideo(entry, positionList, Settings.MaxSamplingDurationSeconds,
									Settings.ExtendedFFToolsLogging,
									onSampleComplete: (done) => ReportStage(entryPath, samplingLabel, done, totalSamples),
									embeddingSink: aiEmbeddingPipeline))
								entry.invalid = true;
						}

						// Audio fingerprint — videos only, only when enabled,
						// skipped if already cached or flagged as having no audio track.
						if (Settings.EnablePartialClipDetection &&
							!entry.IsImage &&
							!entry.Flags.Has(EntryFlags.NoAudioTrack) &&
							!entry.Flags.Has(EntryFlags.AudioFingerprintError) &&
							!entry.Flags.Has(EntryFlags.SilentAudioTrack) &&
							entry.AudioFingerprint == null) {
							string audioPath = entry.Path;
							string audioLabel = T("Scan.Stage.AudioFingerprint");
							ReportStage(audioPath, audioLabel);
							ExtractAudioFingerprint(entry, cancelationTokenSource.Token,
								onProgress: p => ReportStage(audioPath, audioLabel, (int)(p * 100), 100));
						}

						CompleteEntry(entry, driveCounter);
						return ValueTask.CompletedTask;
					}
					catch (OperationCanceledException) {
						throw;
					}
					catch (Exception ex) {
						// One bad file must not tear down a multi-hour scan. Flag the entry
						// so it's skipped on subsequent runs (unless AlwaysRetryFailedSampling)
						// and log enough detail to identify the culprit.
						Logger.Instance.Error($"Unhandled error processing '{entry.Path}': {ex}");
						entry.invalid = true;
						entry.Flags.Set(EntryFlags.ThumbnailError);
						CompleteEntry(entry, driveCounter);
						return ValueTask.CompletedTask;
					}
				}

				// Per-drive concurrency: one spinning disk collapses to a fraction of its
				// sequential throughput when many files are read at once (seek thrash), while
				// an SSD wants high queue depth — a single global parallelism cannot fit a
				// mixed SSD+HDD scan. Each drive therefore runs its own loop at a parallelism
				// matched to its storage, all drives concurrently, so a fast drive is never
				// held back by a slow one. Probe candidates are restricted to files this scan
				// may read anyway — classification must not spin up out-of-scope drives.
				List<DriveScanGroup> driveGroups = DriveScanPlanner.PartitionByDrive(DatabaseUtils.Database);
				if (Settings.MaxDegreeOfParallelism == 1) {
					// Documented promise: 1 = strictly one file at a time. Drives run
					// sequentially, no probing needed (speed class stays unknown).
					driveProgressTracker = new DriveProgressTracker(driveGroups, CountsTowardDriveProgress, classified: false);
					for (int i = 0; i < driveGroups.Count; i++) {
						DriveProgressTracker.Counter counter = driveProgressTracker.CounterFor(i);
						await Parallel.ForEachAsync(driveGroups[i].Entries, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = 1 },
							(entry, token) => ProcessEntry(entry, counter, token));
					}
				}
				else {
					DriveScanPlanner.ClassifyGroups(driveGroups, Settings.DriveTypeOverrides,
						DriveScanPlanner.IsNetworkRoot,
						group => DriveScanPlanner.ProbeSeekLatencyMs(
							group.Entries.Where(CountsTowardDriveProgress)));
					DriveScanPlanner.AssignParallelism(driveGroups, Settings.MaxDegreeOfParallelism, Settings.HddMaxDegreeOfParallelism, Environment.ProcessorCount);
					driveProgressTracker = new DriveProgressTracker(driveGroups, CountsTowardDriveProgress, classified: true);
					var driveTasks = new List<Task>(driveGroups.Count);
					for (int i = 0; i < driveGroups.Count; i++) {
						DriveScanGroup group = driveGroups[i];
						DriveProgressTracker.Counter counter = driveProgressTracker.CounterFor(i);
						Logger.Instance.Info($"Drive '{group.Root}': {group.Entries.Count:N0} file(s), concurrency {group.DegreeOfParallelism} ({(group.SpeedClass == DriveSpeedClass.Fast ? "fast" : "slow")}, {group.ClassSource})");
						driveTasks.Add(Parallel.ForEachAsync(group.Entries, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = group.DegreeOfParallelism },
							(entry, token) => ProcessEntry(entry, counter, token)));
					}
					await Task.WhenAll(driveTasks);
				}
			}
			catch (OperationCanceledException) { }
			finally {
				driveProgressTracker = null;
				LogExcludedSummary();
			}
		}

	
	internal static void ExtractAudioFingerprint(FileEntry entry, CancellationToken ct = default, Action<double>? onProgress = null) {
		uint[]? fp = FFTools.ChromaprintEngine.ExtractFingerprint(entry.Path, false, ct, onProgress);
		if (fp == null && ct.IsCancellationRequested) {
			// Stop/cancel mid-file is not a file error. Flagging here poisoned the entry
			// permanently: both the AudioFingerprintError flag and the non-null empty
			// fingerprint block every retry gate, so the file would never be fingerprinted
			// again. Leave the entry untouched and let the next scan retry it.
			return;
		}
		if (fp == null) {
			// null = extraction failed (error or no audio stream)
			entry.Flags.Set(EntryFlags.AudioFingerprintError);
			entry.AudioFingerprint = Array.Empty<uint>();
		}
		else if (fp.Length == 0) {
			// FFmpeg ran but produced no samples (file has no usable audio)
			entry.Flags.Set(EntryFlags.NoAudioTrack);
			entry.AudioFingerprint = Array.Empty<uint>();
		}
		else if (IsSilentFingerprint(fp)) {
			// Silent tracks produce all-zero fingerprints, which Hamming-match any
			// other silent track at 100% and cause false-positive partial-clip groups.
			entry.Flags.Set(EntryFlags.SilentAudioTrack);
			entry.AudioFingerprint = Array.Empty<uint>();
		}
		else {
			entry.AudioFingerprint = fp;
		}
	}

	/// <summary>
	/// Returns true when every block in the fingerprint is zero. For silent or
	/// near-silent audio the chroma bins collapse to equal values, and the 32
	/// comparison pairs in <see cref="Chromaprint.Pipeline.FingerprintCalculator"/>
	/// all resolve to the non-greater branch, producing uniformly zero blocks.
	/// </summary>
	internal static bool IsSilentFingerprint(uint[] fp) {
		if (fp.Length == 0) return false;
		for (int i = 0; i < fp.Length; i++)
			if (fp[i] != 0u) return false;
		return true;
	}

	static byte[]?[] CreateFlippedGrayBytes(FileEntry entry) {
			byte[]?[] source = entry.compareGray!;
			var flipped = new byte[]?[source.Length];
			for (int j = 0; j < source.Length; j++)
				// FlipGrayScale derives the side length from the array, so it handles both
				// current 32x32 data and 16x16 data from legacy (DbVersion < 2) databases.
				flipped[j] = GrayBytesUtils.FlipGrayScale(source[j]!);
			return flipped;
		}

		// For the flip path this runs once per entry before its pair loop — never per
		// pair, the DCT is far too expensive for the per-pair hot path. Null when any
		// position cannot be hashed (legacy 16x16 data): pHash checks using the result
		// are skipped for that entry.
		internal static ulong[]? ComputePHashesFromGray(byte[]?[] grayBytes) {
			var phashes = new ulong[grayBytes.Length];
			for (int j = 0; j < grayBytes.Length; j++) {
				byte[]? gray = grayBytes[j];
				if (gray == null || gray.Length != GrayBytesUtils.Side * GrayBytesUtils.Side)
					return null;
				phashes[j] = pHash.PerceptualHash.ComputePHashFromGray32x32(gray);
			}
			return phashes;
		}

		/// <summary>Returns true if the last <paramref name="depth"/> path segments of both folder paths are equal (case-insensitive).</summary>
	static bool SameFolderAtDepth(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int depth) {
		for (int i = 0; i < depth; i++) {
			while (a.Length > 0 && (a[^1] == Path.DirectorySeparatorChar || a[^1] == Path.AltDirectorySeparatorChar))
				a = a[..^1];
			while (b.Length > 0 && (b[^1] == Path.DirectorySeparatorChar || b[^1] == Path.AltDirectorySeparatorChar))
				b = b[..^1];

			int sepA = a.LastIndexOf(Path.DirectorySeparatorChar);
			if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar) {
				int alt = a.LastIndexOf(Path.AltDirectorySeparatorChar);
				if (alt > sepA) sepA = alt;
			}
			int sepB = b.LastIndexOf(Path.DirectorySeparatorChar);
			if (Path.DirectorySeparatorChar != Path.AltDirectorySeparatorChar) {
				int alt = b.LastIndexOf(Path.AltDirectorySeparatorChar);
				if (alt > sepB) sepB = alt;
			}

			var segA = sepA >= 0 ? a[(sepA + 1)..] : a;
			var segB = sepB >= 0 ? b[(sepB + 1)..] : b;

			if (!segA.Equals(segB, StringComparison.OrdinalIgnoreCase))
				return false;

			a = sepA >= 0 ? a[..sepA] : ReadOnlySpan<char>.Empty;
			b = sepB >= 0 ? b[..sepB] : ReadOnlySpan<char>.Empty;
		}
		return true;
	}

	void LogMissingPHash(string path) {
			if (missingPHashFiles.TryAdd(path, 0))
				Logger.Instance.Warn($"Missing pHash data for '{path}' — file will be skipped in pHash comparisons. Re-scan to repopulate.");
		}

	/// <summary>
		/// Builds the transient compare snapshot for <paramref name="entry"/>: gray-byte
		/// arrays aligned with <see cref="positionList"/> order and, when pHashing is
		/// enabled, the pHash of every sampled position (computed once and cached back
		/// into <see cref="FileEntry.PHashes"/> where missing — this prefill is what
		/// keeps the parallel per-pair hot path free of dictionary writes). Returns
		/// false when the stored data is incomplete for the current scan settings —
		/// those entries are excluded from the comparison instead of failing on every
		/// pair.
		/// </summary>
		internal bool TryBuildCompareSnapshot(FileEntry entry, bool usePHashing) {
			if (entry.IsImage) {
				if (!entry.grayBytes.TryGetValue(0, out byte[]? imageGray) || imageGray == null)
					return false;
				entry.compareGray = new[] { imageGray };
				if (Settings.UseAiMatching)
					BuildAiCompareSnapshot(entry, new[] { (double)0 }, entry.compareGray);
				return true;
			}

			var gray = new byte[]?[positionList.Count];
			var positionKeys = Settings.UseAiMatching ? new double[positionList.Count] : null;
			for (int j = 0; j < positionList.Count; j++) {
				double idx = GetGrayBytesIndex(entry, positionList[j]);
				if (positionKeys != null)
					positionKeys[j] = idx;
				if (!entry.grayBytes.TryGetValue(idx, out byte[]? data) || data == null)
					return false;
				gray[j] = data;
			}

			if (usePHashing) {
				var phashes = new ulong[positionList.Count];
				for (int j = 0; j < positionList.Count; j++) {
					double idx = GetGrayBytesIndex(entry, positionList[j]);
					if (entry.PHashes.TryGetValue(idx, out ulong? cached) && cached.HasValue) {
						phashes[j] = cached.Value;
						continue;
					}
					if (gray[j]!.Length != GrayBytesUtils.Side * GrayBytesUtils.Side) {
						// Legacy 16x16 data slipped past the DbVersion gate (mixed database).
						// Return before assigning compareGray so a dropped entry leaves no
						// dangling snapshot behind (the end-of-phase cleanup only visits the
						// validated ScanList).
						LogMissingPHash(entry.Path);
						return false;
					}
					phashes[j] = pHash.PerceptualHash.ComputePHashFromGray32x32(gray[j]);
					entry.PHashes[idx] = phashes[j]; // cache for future quick rescans; also heals stored nulls
				}
				entry.comparePHashes = phashes;
			}
			// AI snapshot last, after every validation that can still drop the entry —
			// a dropped entry must leave no dangling snapshot behind (the end-of-phase
			// cleanup only visits the validated ScanList). Missing embeddings never drop
			// an entry — the classic modes still work and the AI pass abstains for null slots.
			if (positionKeys != null)
				BuildAiCompareSnapshot(entry, positionKeys, gray);
			entry.compareGray = gray;
			return true;
		}

		/// <summary>
		/// AI-pass snapshot: the entry's quantized embeddings aligned with the compare
		/// order, plus the black-frame guard — an embedding only participates when its
		/// gray frame has enough non-dark pixels (dark frames embed near-identically
		/// regardless of content, which would turn the union pass into a false-positive
		/// generator on fade-outs and night scenes).
		/// </summary>
		void BuildAiCompareSnapshot(FileEntry entry, double[] positionKeys, byte[]?[] gray) {
			AI.UnionEmbeddingStore? store = unionEmbeddingStore;
			var embeddings = new byte[]?[positionKeys.Length];
			var valid = new bool[positionKeys.Length];
			for (int j = 0; j < positionKeys.Length; j++) {
				embeddings[j] = store?.GetEmbedding(entry, positionKeys[j]);
				valid[j] = gray[j] != null && GrayBytesUtils.VerifyGrayScaleValues(gray[j]!);
			}
			entry.compareEmbeddings = embeddings;
			entry.compareEmbeddingValid = valid;
		}

		/// <summary>
		/// Mean embedding similarity over the positions where both entries have a valid
		/// (non-dark, successfully embedded) vector; -1 when fewer than half of the
		/// compared positions qualify — the AI pass then abstains for this pair rather
		/// than letting one coincidental bright frame decide a mostly-dark file.
		/// </summary>
		internal static float ComputeAiSimilarity(FileEntry entry, FileEntry compItem) {
			byte[]?[]? ea = entry.compareEmbeddings;
			byte[]?[]? eb = compItem.compareEmbeddings;
			if (ea == null || eb == null)
				return -1f;
			bool[]? va = entry.compareEmbeddingValid;
			bool[]? vb = compItem.compareEmbeddingValid;
			int n = Math.Min(ea.Length, eb.Length);
			float sum = 0f;
			int count = 0;
			for (int j = 0; j < n; j++) {
				byte[]? x = ea[j];
				byte[]? y = eb[j];
				if (x == null || y == null)
					continue;
				if (va != null && j < va.Length && !va[j])
					continue;
				if (vb != null && j < vb.Length && !vb[j])
					continue;
				sum += AI.EmbeddingMath.CosineSimilarity(x, y);
				count++;
			}
			int requiredPositions = Math.Max(1, (n + 1) / 2);
			return count < requiredPositions ? -1f : sum / count;
		}

		internal bool CheckIfDuplicate(FileEntry entry, byte[]?[]? overrideGray, ulong[]? overridePHashes, FileEntry compItem, out float difference) =>
			CheckIfDuplicate(entry, overrideGray, overridePHashes, compItem, out difference, out _);

		/// <summary>
		/// The pair verdict: the classic gray-bytes/pHash check, unioned with the AI
		/// embedding pass when <see cref="Settings.UseAiMatching"/> is on — a pair the
		/// classic mode rejects still counts as a duplicate when the mean embedding
		/// similarity reaches <see cref="Settings.AiPercent"/> (<paramref name="aiMatched"/>
		/// reports that case so callers can flag it). The AI pass only runs on the
		/// normal orientation: embeddings are measured flip-robust, so re-running it
		/// for the flipped comparison would only duplicate work.
		/// </summary>
		internal bool CheckIfDuplicate(FileEntry entry, byte[]?[]? overrideGray, ulong[]? overridePHashes, FileEntry compItem, out float difference, out bool aiMatched) {
			aiMatched = false;
			if (CheckIfDuplicateClassic(entry, overrideGray, overridePHashes, compItem, out difference))
				return true;
			if (overrideGray != null || !Settings.UseAiMatching)
				return false;
			float aiSimilarity = ComputeAiSimilarity(entry, compItem);
			if (aiSimilarity < Settings.AiPercent / 100f)
				return false;
			difference = 1f - aiSimilarity;
			aiMatched = true;
			return true;
		}

		bool CheckIfDuplicateClassic(FileEntry entry, byte[]?[]? overrideGray, ulong[]? overridePHashes, FileEntry compItem, out float difference) {
			byte[]?[] grayBytes = overrideGray ?? entry.compareGray!;
			float differenceLimit = 1.0f - Settings.Percent / 100f;
			bool ignoreBlackPixels = Settings.IgnoreBlackPixels;
			bool ignoreWhitePixels = Settings.IgnoreWhitePixels;
			difference = 1f;

			if (entry.IsImage) {
				difference = ignoreBlackPixels || ignoreWhitePixels ?
								GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(grayBytes[0]!, compItem.compareGray![0]!, ignoreBlackPixels, ignoreWhitePixels) :
								GrayBytesUtils.PercentageDifference(grayBytes[0]!, compItem.compareGray![0]!);
				return difference <= differenceLimit;
			}

			if (Settings.UsePHashing) {
				float differenceLimitpHash = Settings.Percent / 100f;

				// Entries with unrecoverable pHash data were dropped during snapshot
				// building; a null array only occurs on the flip path when the flipped
				// hashes could not be computed.
				ulong[]? phashes = overrideGray != null ? overridePHashes : entry.comparePHashes;
				ulong[]? phashesComp = compItem.comparePHashes;
				if (phashes == null || phashesComp == null)
					return false;

				// A pair is a duplicate when at least PHashRequiredMatchingSampleRatio
				// of the sampled positions individually pass the similarity threshold.
				// Comparing only one position made a single coincidental frame (black
				// intro, title card) enough to report two unrelated videos as
				// duplicates — and a single divergent frame enough to miss real ones.
				int sampleCount = Math.Min(phashes.Length, phashesComp.Length);
				if (sampleCount == 0)
					return false;
				// requiredMatches is the same for every pair in a scan; the compare phase
				// precomputes it (matchingRequiredSampleMatches). Direct/diagnostic callers
				// leave it null and compute locally.
				int requiredMatches = matchingRequiredSampleMatches is int precomputed && sampleCount == positionList.Count
					? precomputed
					: Math.Max(1, (int)Math.Ceiling(sampleCount * Math.Clamp(Settings.PHashRequiredMatchingSampleRatio, 0.01f, 1f)));
				int matches = 0;
				// Mean dissimilarity over ALL sampled positions, not just the matching ones:
				// dividing both orientations by the same sampleCount keeps the normal and
				// flipped `difference` values comparable for the flip-vs-normal selection in
				// TryCheckDuplicate, and stops a fully divergent frame from being hidden
				// behind the high average of the few frames that happened to pass.
				float pHashDiffSum = 0f;

				for (int j = 0; j < sampleCount; j++) {
					bool pass = pHash.PHashCompare.IsDuplicateByPercent(phashes[j], phashesComp[j], out float similarity, differenceLimitpHash, strict: true);
					pHashDiffSum += 1f - similarity;
					if (pass)
						matches++;
					else if (matches + (sampleCount - j - 1) < requiredMatches)
						return false; // quorum unreachable — skip the remaining samples
				}
				if (matches < requiredMatches)
					return false;

				difference = pHashDiffSum / sampleCount;
				return !float.IsNaN(difference);
			}

			byte[]?[] compGray = compItem.compareGray!;
			differenceLimit *= grayBytes.Length;
			float diffSum = 0;
			for (int j = 0; j < grayBytes.Length; j++) {
				diffSum += ignoreBlackPixels || ignoreWhitePixels ?
							GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(
								grayBytes[j]!, compGray[j]!, ignoreBlackPixels, ignoreWhitePixels) :
							GrayBytesUtils.PercentageDifference(grayBytes[j]!, compGray[j]!);
				if (diffSum > differenceLimit) // already exceeding maximum tolerated diff -> exit early
					return false;
			}
			difference = diffSum / grayBytes.Length;
			return !float.IsNaN(difference);
		}

		internal void ScanForDuplicates() {
			Dictionary<string, DuplicateItem>? duplicateDict = new();
			// Maps GroupId -> representative FileEntry for that group.
			// Used to prevent merging groups whose representatives aren't similar.
			Dictionary<Guid, FileEntry> groupRepresentatives = new();
			// Maps GroupId -> its members, so merging two groups relabels only the
			// absorbed group's items instead of scanning every duplicate found so far
			// while holding the lock.
			Dictionary<Guid, List<DuplicateItem>> groupMembers = new();
			int mergesBlocked = 0;
			missingPHashFiles.Clear();

			//Exclude existing database entries which not met current scan settings
			List<FileEntry> ScanList = new();

			Logger.Instance.Info("Prepare list of items to compare...");
			foreach (FileEntry entry in DatabaseUtils.Database) {
				if (!InvalidEntryForDuplicateCheck(entry)) {
					ScanList.Add(entry);
				}
			}

			// Materialize per-entry compare snapshots so the per-pair hot path works on
			// plain arrays instead of probing Dictionary<double,...> with recomputed keys.
			// Entries whose stored data is incomplete for the current settings are dropped
			// here (previously they would have failed mid-comparison on every pair).
			bool usePHashing = Settings.UsePHashing;
			int droppedSnapshots = 0;
			{
				if (Settings.UseAiMatching && unionEmbeddingStore == null) {
					// Compare-only run (no scan phase loaded the sidecar in this process).
					unionEmbeddingStore = AI.UnionEmbeddingStore.Load();
					if (unionEmbeddingStore.Count == 0)
						Logger.Instance.Info("AI matching: no cached embeddings found — the AI pass will abstain. Run a scan to compute them.");
				}
				List<FileEntry> validated = new(ScanList.Count);
				foreach (FileEntry entry in ScanList) {
					if (TryBuildCompareSnapshot(entry, usePHashing)) {
						// compareIndex preserves list ordering so symmetric comparisons can be skipped.
						entry.compareIndex = validated.Count;
						validated.Add(entry);
					}
					else
						droppedSnapshots++;
				}
				ScanList = validated;
				// The snapshots now hold references to every embedding this compare needs;
				// drop the store so the sidecar's memory is reclaimable after the phase.
				unionEmbeddingStore = null;
			}
			if (droppedSnapshots > 0)
				Logger.Instance.Warn($"Excluded {droppedSnapshots} file(s) with incomplete cached scan data (missing gray bytes for the current thumbnail positions). Rescan to repopulate.");

			Logger.Instance.Info($"Scanning for duplicates in {ScanList.Count:N0} files");
			// Precompute the pHash quorum threshold once for the whole phase (see field note).
			matchingRequiredSampleMatches = usePHashing
				? Math.Max(1, (int)Math.Ceiling(positionList.Count * Math.Clamp(Settings.PHashRequiredMatchingSampleRatio, 0.01f, 1f)))
				: null;
			int matchingParallelism = MatchingParallelDegree;
			Logger.Instance.Info($"Matching concurrency: {matchingParallelism} worker(s) on {Environment.ProcessorCount} logical processor(s) (configured: matching={Settings.MatchingMaxDegreeOfParallelism}, media reads={Settings.MaxDegreeOfParallelism})");

			currentStageLabel = T("Scan.Stage.ComparingDuplicates");
			InitProgress(ScanList.Count);

			// Duration buckets are keyed by whole seconds to keep percent-based tolerance intact.
			const int bucketSizeSeconds = 1;
			// Avoid bucket overhead for small datasets; fall back to the linear path.
			const int bucketActivationThreshold = 5000;
			var imageEntries = new List<FileEntry>();
			var videoEntries = new List<FileEntry>();
			var videoBuckets = new Dictionary<int, List<FileEntry>>();
			const int largeBucketThreshold = 400;

			for (int i = 0; i < ScanList.Count; i++) {
				var entry = ScanList[i];
				if (entry.IsImage) {
					imageEntries.Add(entry);
					continue;
				}
				videoEntries.Add(entry);
				// Bucket by duration seconds for candidate reduction in the large-data path.
				int bucketKey = (int)Math.Floor(entry.mediaInfo!.Duration.TotalSeconds / bucketSizeSeconds);
				if (!videoBuckets.TryGetValue(bucketKey, out var bucket)) {
					bucket = new List<FileEntry>();
					videoBuckets.Add(bucketKey, bucket);
				}
				bucket.Add(entry);
			}

			void MergeDuplicate(FileEntry entry, FileEntry compItem, float difference, DuplicateFlags flags) {
				lock (duplicateDict) {
					bool foundBase = duplicateDict.TryGetValue(entry.Path, out DuplicateItem? existingBase);
					bool foundComp = duplicateDict.TryGetValue(compItem.Path, out DuplicateItem? existingComp);

					if (foundBase && foundComp) {
						//this happens with 4+ identical items:
						//first, 2+ duplicate groups are found independently, they are merged in this branch
						if (existingBase!.GroupId != existingComp!.GroupId) {
							// Before merging two groups, verify that the representative
							// of each group is similar to the other group's representative.
							// This prevents daisy-chain merging where a single bridging
							// pair pulls two unrelated groups together.
							if (groupRepresentatives.TryGetValue(existingBase.GroupId, out var repBase) &&
								groupRepresentatives.TryGetValue(existingComp.GroupId, out var repComp) &&
								!CheckIfDuplicate(repBase, null, null, repComp, out _)) {
								mergesBlocked++;
								return; // Representatives aren't similar — don't merge.
							}
							Guid groupID = existingComp!.GroupId;
							List<DuplicateItem> baseMembers = groupMembers[existingBase.GroupId];
							foreach (DuplicateItem dup in groupMembers[groupID]) {
								dup.GroupId = existingBase.GroupId;
								baseMembers.Add(dup);
							}
							groupMembers.Remove(groupID);
							// Keep the representative of the absorbing group; remove the merged one.
							groupRepresentatives.Remove(groupID);
						}
					}
					else if (foundBase) {
						// New item joining an existing group — verify it matches the representative.
						if (groupRepresentatives.TryGetValue(existingBase!.GroupId, out var rep) &&
							!CheckIfDuplicate(rep, null, null, compItem, out _)) {
							mergesBlocked++;
							return;
						}
						var newItem = new DuplicateItem(compItem, difference, existingBase!.GroupId, flags);
						if (duplicateDict.TryAdd(compItem.Path, newItem))
							groupMembers[existingBase.GroupId].Add(newItem);
					}
					else if (foundComp) {
						// New item joining an existing group — verify it matches the representative.
						if (groupRepresentatives.TryGetValue(existingComp!.GroupId, out var rep) &&
							!CheckIfDuplicate(rep, null, null, entry, out _)) {
							mergesBlocked++;
							return;
						}
						var newItem = new DuplicateItem(entry, difference, existingComp!.GroupId, flags);
						if (duplicateDict.TryAdd(entry.Path, newItem))
							groupMembers[existingComp.GroupId].Add(newItem);
					}
					else {
						var groupId = Guid.NewGuid();
						var compDup = new DuplicateItem(compItem, difference, groupId, flags);
						var entryDup = new DuplicateItem(entry, difference, groupId, DuplicateFlags.None);
						duplicateDict.TryAdd(compItem.Path, compDup);
						duplicateDict.TryAdd(entry.Path, entryDup);
						groupMembers[groupId] = new List<DuplicateItem> { compDup, entryDup };
						groupRepresentatives[groupId] = entry;
					}
				}
			}

			bool TryCheckDuplicate(FileEntry entry, FileEntry compItem, byte[]?[]? flippedGrayBytes, ulong[]? flippedPHashes, out float difference, out DuplicateFlags flags) {
				flags = DuplicateFlags.None;
				difference = 0;
				bool isDuplicate = CheckIfDuplicate(entry, null, null, compItem, out difference, out bool aiMatched);
				if (aiMatched)
					flags |= DuplicateFlags.AiMatched;
				if (Settings.CompareHorizontallyFlipped &&
					CheckIfDuplicate(entry, flippedGrayBytes, flippedPHashes, compItem, out float flippedDifference)) {
					if (!isDuplicate || flippedDifference < difference) {
						// The flipped pass is classic-only, so a better flipped match
						// supersedes an AI-only verdict (and its flag) with a classic one.
						flags = (flags & ~DuplicateFlags.AiMatched) | DuplicateFlags.Flipped;
						isDuplicate = true;
						difference = flippedDifference;
					}
				}
				return isDuplicate;
			}

			double GetDurationToleranceSeconds(double durationSeconds) =>
				Settings.GetDurationToleranceSeconds(durationSeconds);

			// Compare one entry against candidate buckets (bucketed path).
			void CompareEntry(FileEntry entry, int entryIndex, IEnumerable<int> candidateBucketKeys) {
				if (!pauseTokenSource.TryWaitWhilePaused(cancelationTokenSource.Token))
					return; // canceled while paused — the parallel loop's token ends the iteration

				float difference = 0;
				bool isDuplicate;
				DuplicateFlags flags;
				byte[]?[]? flippedGrayBytes = null;
				ulong[]? flippedPHashes = null;
				double entryDurationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
				double entryToleranceSeconds = GetDurationToleranceSeconds(entryDurationSeconds);

				if (Settings.CompareHorizontallyFlipped) {
					flippedGrayBytes = CreateFlippedGrayBytes(entry);
					if (usePHashing)
						flippedPHashes = ComputePHashesFromGray(flippedGrayBytes);
				}

				foreach (int bucketKey in candidateBucketKeys) {
					if (!videoBuckets.TryGetValue(bucketKey, out var bucketEntries))
						continue;
					foreach (var compItem in bucketEntries) {
						int compIndex = compItem.compareIndex;
						if (compIndex <= entryIndex)
							continue;

						if (!entry.IsImage) {
							double compDurationSeconds = compItem.mediaInfo!.Duration.TotalSeconds;
							double compToleranceSeconds = GetDurationToleranceSeconds(compDurationSeconds);
							double allowedSeconds = Math.Min(entryToleranceSeconds, compToleranceSeconds);
							double diffSeconds = Math.Abs(entryDurationSeconds - compDurationSeconds);
							if (diffSeconds > allowedSeconds)
								continue;
						}

						if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
							!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
							SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;

						isDuplicate = TryCheckDuplicate(entry, compItem, flippedGrayBytes, flippedPHashes, out difference, out flags);

						if (isDuplicate &&
							entry.FileSize == compItem.FileSize &&
							entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
							Settings.ExcludeHardLinks &&
							HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
							isDuplicate = false;
						}

						if (isDuplicate)
							MergeDuplicate(entry, compItem, difference, flags);
					}
				}
				IncrementProgress(entry.Path);
			}

			// Images are always compared linearly; bucketing is only applied to videos.
			void CompareImages() {
				Action<int> compareAction = i => {
					var entry = imageEntries[i];
					byte[]?[]? flippedGrayBytes = null;
					if (Settings.CompareHorizontallyFlipped)
						flippedGrayBytes = CreateFlippedGrayBytes(entry);
					for (int n = i + 1; n < imageEntries.Count; n++) {
						var compItem = imageEntries[n];
						float difference = 0;
						DuplicateFlags flags;
						if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
							!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
							SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						// Images never take the pHash branch, so no flipped pHash is needed.
						bool isDuplicate = TryCheckDuplicate(entry, compItem, flippedGrayBytes, null, out difference, out flags);

						if (isDuplicate &&
							entry.FileSize == compItem.FileSize &&
							Settings.ExcludeHardLinks &&
							HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
							isDuplicate = false;
						}

						if (isDuplicate)
							MergeDuplicate(entry, compItem, difference, flags);
					}
					IncrementProgress(entry.Path);
				};

				try {
					if (imageEntries.Count >= largeBucketThreshold) {
						Parallel.For(0, imageEntries.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = matchingParallelism }, compareAction);
					}
					else {
						for (int i = 0; i < imageEntries.Count; i++)
							compareAction(i);
					}
				}
				catch (OperationCanceledException) { }
			}

			// Linear compare path for small datasets to avoid bucket bookkeeping overhead.
			void CompareVideosLinear() {
				Action<int> compareAction = i => {
					if (!pauseTokenSource.TryWaitWhilePaused(cancelationTokenSource.Token))
						return; // canceled while paused — the loop guards below end the iteration

					var entry = videoEntries[i];
					float difference = 0;
					DuplicateFlags flags;
					byte[]?[]? flippedGrayBytes = null;
					ulong[]? flippedPHashes = null;
					double entryDurationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
					double entryToleranceSeconds = GetDurationToleranceSeconds(entryDurationSeconds);

					if (Settings.CompareHorizontallyFlipped) {
						flippedGrayBytes = CreateFlippedGrayBytes(entry);
						if (usePHashing)
							flippedPHashes = ComputePHashesFromGray(flippedGrayBytes);
					}

					for (int n = i + 1; n < videoEntries.Count; n++) {
						var compItem = videoEntries[n];
						double compDurationSeconds = compItem.mediaInfo!.Duration.TotalSeconds;
						double compToleranceSeconds = GetDurationToleranceSeconds(compDurationSeconds);
						double allowedSeconds = Math.Min(entryToleranceSeconds, compToleranceSeconds);
						double diffSeconds = Math.Abs(entryDurationSeconds - compDurationSeconds);
						if (diffSeconds > allowedSeconds)
							continue;

						if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly &&
							!SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;
						if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly &&
							SameFolderAtDepth(entry.Folder, compItem.Folder, Settings.SameFolderDepth))
							continue;

						bool isDuplicate = TryCheckDuplicate(entry, compItem, flippedGrayBytes, flippedPHashes, out difference, out flags);
						if (isDuplicate &&
							entry.FileSize == compItem.FileSize &&
							entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
							Settings.ExcludeHardLinks &&
							HardLinkUtils.AreSameFile(entry.Path, compItem.Path)) {
							isDuplicate = false;
						}

						if (isDuplicate)
							MergeDuplicate(entry, compItem, difference, flags);
					}

					IncrementProgress(entry.Path);
				};

				try {
					if (videoEntries.Count >= largeBucketThreshold) {
						Parallel.For(0, videoEntries.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = matchingParallelism }, compareAction);
					}
					else {
						// compareAction returns early on cancellation instead of throwing,
						// so the sequential path must check the token itself.
						for (int i = 0; i < videoEntries.Count && !cancelationTokenSource.IsCancellationRequested; i++)
							compareAction(i);
					}
				}
				catch (OperationCanceledException) { }
			}

			try {
				CompareImages();

				if (videoEntries.Count < bucketActivationThreshold) {
					// Small dataset: keep the simpler linear path.
					CompareVideosLinear();
				}
				else {
					// Large dataset: use buckets to reduce candidate comparisons.
					var smallBuckets = videoBuckets.Where(kvp => kvp.Value.Count < largeBucketThreshold).ToList();
					var largeBuckets = videoBuckets.Where(kvp => kvp.Value.Count >= largeBucketThreshold).ToList();

					Parallel.ForEach(smallBuckets, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = matchingParallelism }, bucket => {
						foreach (var entry in bucket.Value) {
							int entryIndex = entry.compareIndex;
							double durationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
							double maxDiffSeconds = GetDurationToleranceSeconds(durationSeconds);
							double minDuration = Math.Max(0d, durationSeconds - maxDiffSeconds);
							double maxDuration = durationSeconds + maxDiffSeconds;
							int minKey = (int)Math.Floor(minDuration / bucketSizeSeconds);
							int maxKey = (int)Math.Floor(maxDuration / bucketSizeSeconds);
							CompareEntry(entry, entryIndex, Enumerable.Range(minKey, maxKey - minKey + 1));
						}
					});

					foreach (var bucket in largeBuckets) {
						Parallel.For(0, bucket.Value.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = matchingParallelism }, i => {
							var entry = bucket.Value[i];
							int entryIndex = entry.compareIndex;
							double durationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
							double maxDiffSeconds = GetDurationToleranceSeconds(durationSeconds);
							double minDuration = Math.Max(0d, durationSeconds - maxDiffSeconds);
							double maxDuration = durationSeconds + maxDiffSeconds;
							int minKey = (int)Math.Floor(minDuration / bucketSizeSeconds);
							int maxKey = (int)Math.Floor(maxDuration / bucketSizeSeconds);
							CompareEntry(entry, entryIndex, Enumerable.Range(minKey, maxKey - minKey + 1));
						});
					}
				}
			}
			catch (OperationCanceledException) { }
			if (mergesBlocked > 0)
				Logger.Instance.Info($"Group merge validation: blocked {mergesBlocked} merge(s) where group representatives were not similar");
			if (missingPHashFiles.Count > 0)
				Logger.Instance.Warn($"pHash comparison: {missingPHashFiles.Count} file(s) had missing pHash data and were skipped in pHash comparisons. Delete the database (or rescan with 'Always retry failed sampling') to recompute.");
			Duplicates = new HashSet<DuplicateItem>(duplicateDict.Values);
			SplitDaisyChainGroups();

			// Release the transient snapshots; the gray-byte arrays themselves remain
			// owned by entry.grayBytes, only the alignment wrappers are dropped.
			foreach (FileEntry entry in ScanList) {
				entry.compareGray = null;
				entry.comparePHashes = null;
				entry.compareEmbeddings = null;
				entry.compareEmbeddingValid = null;
			}
			matchingRequiredSampleMatches = null;
		}


		/// <summary>
		/// Phase 2 comparison: find pairs where a shorter video is a partial clip of a longer one,
		/// using audio fingerprint sliding-window matching.  Results are added to Duplicates.
		/// The comparison loop runs in parallel; grouping is applied sequentially afterward.
		/// </summary>
		internal void ScanForPartialDuplicates() {
			Logger.Instance.Info("Partial clip detection: building fingerprint index...");

			// Build a quick lookup for paths already covered by visual duplicate groups.
			var alreadyGrouped = new HashSet<string>(
				Duplicates.Select(d => d.Path),
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			// Collect eligible videos: not an image, has a usable fingerprint, not already grouped.
			// Exclude silent/all-zero fingerprints: they Hamming-match any other silent track
			// at 100% and produce meaningless partial-clip groups. Older scan databases written
			// before this check may still contain all-zero fingerprints, so filter at read time.
			var videos = DatabaseUtils.Database
				.Where(e => !e.invalid && !e.IsImage &&
						!e.Flags.Has(EntryFlags.SilentAudioTrack) &&
						e.AudioFingerprint != null && e.AudioFingerprint.Length >= 2 &&
						!IsSilentFingerprint(e.AudioFingerprint) &&
						!alreadyGrouped.Contains(e.Path))
				.OrderByDescending(e => e.mediaInfo?.Duration ?? TimeSpan.Zero)
				.ToList();

			if (videos.Count < 2) {
				Logger.Instance.Info("Partial clip detection: fewer than 2 eligible videos, skipping.");
				return;
			}

			Logger.Instance.Info($"Partial clip detection: comparing {videos.Count} video(s) (fingerprint blocks: min={videos.Min(e => e.AudioFingerprint!.Length)}, max={videos.Max(e => e.AudioFingerprint!.Length)})...");

			float simThreshold = (float)Settings.PartialClipSimilarityThreshold;
			currentStageLabel = T("Scan.Stage.PartialCompare");
			InitProgress(videos.Count - 1);

			// --- Parallel phase: compute all matches without mutating shared state ---
			var matches = new ConcurrentBag<(int sourceIdx, int clipIdx, float sim, int offsetSec)>();
			int pairsChecked = 0;

			Parallel.For(0, videos.Count - 1,
				new ParallelOptions {
					CancellationToken = cancelationTokenSource.Token,
					MaxDegreeOfParallelism = MatchingParallelDegree
				},
				i => {
					if (!pauseTokenSource.TryWaitWhilePaused(cancelationTokenSource.Token))
						return; // canceled while paused — the loop's token ends the remaining iterations

					FileEntry source = videos[i];
					double sourceSec = (source.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;

					if (sourceSec >= 1.0) {
						for (int j = i + 1; j < videos.Count; j++) {
							if (cancelationTokenSource.IsCancellationRequested) break;
							FileEntry clip = videos[j];
							double clipSec = (clip.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
							if (clipSec < 1.0) continue;

							// Pre-filter 1: clip must be at least PartialClipMinRatio of source
							if (clipSec / sourceSec < Settings.PartialClipMinRatio) continue;

							// Pre-filter 2: clip must be shorter than 95% of source (visual dup handles the rest)
							if (clipSec / sourceSec >= 0.95) continue;

							// Fingerprint block sanity (each block ≈ 1 second)
							uint[] fpSource = source.AudioFingerprint!;
							uint[] fpClip = clip.AudioFingerprint!;
							if (fpClip.Length >= fpSource.Length) continue;

							Interlocked.Increment(ref pairsChecked);
							var (sim, offsetSec) = SlidingWindowCompare(fpClip, fpSource, simThreshold);

							if (sim >= simThreshold)
								matches.Add((i, j, sim, offsetSec));
						}
					}

					// Counted on completion, as every other compare loop does: a source is only done
					// once its whole row of pairs has been checked. Counting at the loop head
					// reported work that had not happened yet — the bar reached its maximum the
					// moment the last iteration *started*.
					IncrementProgress(Path.GetFileName(source.Path));
				});

			// --- Sequential phase: build groups from matches (preserving longest-source-first order) ---
			// A clip is kept with its first (longest) matching source. Sources whose only
			// candidate clips are already claimed are skipped entirely - adding them would
			// produce singleton groups in the result list.
			var assignments = AssignPartialClipGroups(matches);

			// Optional visual gate: drop pairs that match audio but differ visually at the
			// matched offset (e.g. videos sharing a backing track but otherwise unrelated).
			// Uses pHash when Settings.UsePHashing is on, else 32x32 grayscale percentage diff.
			if (Settings.PartialClipRequireVisualMatch && assignments.Count > 0)
				assignments = RunPartialClipVisualGate(videos, assignments,
					(source, clip, offsetSec) => {
						bool pass = VerifyPartialClipVisually(source, clip, offsetSec, out float visualSim);
						return (pass, visualSim);
					});

			var addedSources = new HashSet<int>();

			foreach (var (si, ci, sim, offsetSec, groupId) in assignments) {
				FileEntry source = videos[si];
				FileEntry clip = videos[ci];

				if (Settings.ExtendedFFToolsLogging)
					Logger.Instance.Info($"[Partial] {System.IO.Path.GetFileName(clip.Path)} in {System.IO.Path.GetFileName(source.Path)}: sim={sim:P1} @ {offsetSec}s (threshold {Settings.PartialClipSimilarityThreshold:P0}, fp {clip.AudioFingerprint!.Length}/{source.AudioFingerprint!.Length} blocks)");

				if (addedSources.Add(si))
					Duplicates.Add(new DuplicateItem(source, 0f, groupId, DuplicateFlags.None));

				Duplicates.Add(new DuplicateItem(clip, 1f - sim, groupId, DuplicateFlags.PartialClip) {
					PartialClipOffset = TimeSpan.FromSeconds(offsetSec)
				});
			}

			Logger.Instance.Info($"Partial clip detection: checked {pairsChecked} pair(s), found {matches.Count} candidate match(es), formed {assignments.Count} clip-source assignment(s).");
		}

		/// <summary>
		/// Runs <paramref name="verify"/> over every candidate assignment and returns the ones that
		/// pass, ordered deterministically. Its own progress phase: the gate decodes frames off disk
		/// and can outlast the audio pass that produced the assignments, so leaving it silent left
		/// the UI showing that pass's completed counters for minutes on end (#831).
		/// <paramref name="verify"/> is injected so tests can drive the phase without FFmpeg.
		/// </summary>
		internal List<(int sourceIdx, int clipIdx, float sim, int offsetSec, Guid groupId)> RunPartialClipVisualGate(
			List<FileEntry> videos,
			List<(int sourceIdx, int clipIdx, float sim, int offsetSec, Guid groupId)> assignments,
			Func<FileEntry, FileEntry, int, (bool pass, float visualSim)> verify) {

			currentStageLabel = T("Scan.Stage.PartialVisualVerify");
			InitProgress(assignments.Count);

			int beforeCount = assignments.Count;
			int dropped = 0;
			var verified = new ConcurrentBag<(int, int, float, int, Guid)>();
			try {
				// Storage-tuned degree, NOT MatchingParallelDegree: unlike the audio
				// fingerprint pass above (pure CPU over in-memory fingerprints), the visual
				// gate decodes frames live off disk via GetGrayFrames, so it must respect the
				// media-read cap that HDD users lower to avoid seek-thrash.
				Parallel.ForEach(assignments, new ParallelOptions {
					CancellationToken = cancelationTokenSource.Token,
					MaxDegreeOfParallelism = ParallelDegree
				}, a => {
					if (!pauseTokenSource.TryWaitWhilePaused(cancelationTokenSource.Token))
						return; // canceled while paused — the loop's token ends the remaining iterations

					var (pass, visualSim) = verify(videos[a.sourceIdx], videos[a.clipIdx], a.offsetSec);
					if (pass) {
						verified.Add(a);
					}
					else {
						Interlocked.Increment(ref dropped);
						if (Settings.ExtendedFFToolsLogging)
							Logger.Instance.Info($"[Partial] Visual gate dropped {System.IO.Path.GetFileName(videos[a.clipIdx].Path)} in {System.IO.Path.GetFileName(videos[a.sourceIdx].Path)}: visualSim={visualSim:P1} (threshold {Settings.PartialClipVisualThreshold:P0})");
					}
					IncrementProgress(System.IO.Path.GetFileName(videos[a.clipIdx].Path));
				});
			}
			catch (OperationCanceledException) { }
			var kept = verified.OrderBy(a => a.Item1).ThenBy(a => a.Item2).ToList();
			Logger.Instance.Info($"Partial clip detection: visual gate kept {kept.Count}/{beforeCount} assignment(s), dropped {dropped}");
			return kept;
		}

		/// <summary>
		/// On-demand visual check for a partial-clip candidate. Decodes 1-3 frames from the
		/// clip and the source at the matched audio offset and compares them. Returns true
		/// when the average similarity meets <see cref="Settings.PartialClipVisualThreshold"/>,
		/// or when no frames could be sampled (in which case audio alone decides). Uses pHash
		/// when <see cref="Settings.UsePHashing"/> is enabled, otherwise grayscale percent diff.
		/// </summary>
		bool VerifyPartialClipVisually(FileEntry source, FileEntry clip, int offsetSec, out float visualSim) {
			visualSim = 0f;
			double sourceSec = (source.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
			double clipSec = (clip.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
			if (sourceSec <= 0 || clipSec <= 0) return true;

			// Sample times in clip-local seconds. Avoid the very edges so intros/outros
			// (often black or text-only) don't dominate the result.
			var clipTimes = new List<double>(3);
			if (clipSec >= 9.0) {
				clipTimes.Add(clipSec * 0.25);
				clipTimes.Add(clipSec * 0.50);
				clipTimes.Add(clipSec * 0.75);
			}
			else if (clipSec >= 3.0) {
				clipTimes.Add(clipSec * 0.33);
				clipTimes.Add(clipSec * 0.66);
			}
			else {
				clipTimes.Add(clipSec * 0.5);
			}

			bool useP = Settings.UsePHashing;
			double threshold = Settings.PartialClipVisualThreshold;
			int comparisons = 0;
			float simSum = 0f;

			// Collect the usable sample times first so each file is decoded in a single
			// batched session instead of one decoder open per frame.
			var srcSampleTimes = new List<double>(clipTimes.Count);
			var clipSampleTimes = new List<double>(clipTimes.Count);
			foreach (double t in clipTimes) {
				double srcAt = offsetSec + t;
				if (srcAt >= sourceSec - 0.1 || t >= clipSec - 0.1) continue;
				srcSampleTimes.Add(srcAt);
				clipSampleTimes.Add(t);
			}
			if (srcSampleTimes.Count == 0) return true;

			byte[]?[] srcFrames = FfmpegEngine.GetGrayFrames(source.Path, srcSampleTimes, Settings.ExtendedFFToolsLogging);
			byte[]?[] clipFrames = FfmpegEngine.GetGrayFrames(clip.Path, clipSampleTimes, Settings.ExtendedFFToolsLogging);

			for (int i = 0; i < srcSampleTimes.Count; i++) {
				byte[]? srcFrame = srcFrames[i];
				byte[]? clipFrame = clipFrames[i];
				if (srcFrame == null || clipFrame == null) continue;

				float pairSim;
				if (useP) {
					ulong hSrc = pHash.PerceptualHash.ComputePHashFromGray32x32(srcFrame);
					ulong hClip = pHash.PerceptualHash.ComputePHashFromGray32x32(clipFrame);
					pHash.PHashCompare.IsDuplicateByPercent(hSrc, hClip, out pairSim, threshold, strict: true);
				}
				else {
					float diff = GrayBytesUtils.PercentageDifference(srcFrame, clipFrame);
					pairSim = 1f - diff;
				}
				simSum += pairSim;
				comparisons++;
			}

			if (comparisons == 0) return true;
			visualSim = simSum / comparisons;
			return visualSim >= threshold;
		}

		/// <summary>
		/// Resolves overlapping partial-clip matches into deterministic group assignments.
		/// Matches are processed in (sourceIdx ASC, clipIdx ASC) order - since callers sort
		/// videos by duration descending, this means each clip is bound to the longest
		/// source that contains it. Subsequent matches for an already-assigned clip are
		/// dropped, and their would-be source is omitted unless it has unclaimed clips of
		/// its own. This prevents singleton groups in the output.
		/// </summary>
		internal static List<(int sourceIdx, int clipIdx, float sim, int offsetSec, Guid groupId)>
			AssignPartialClipGroups(IEnumerable<(int sourceIdx, int clipIdx, float sim, int offsetSec)> matches) {
			var sourceGroupId = new Dictionary<int, Guid>();
			var assignedClips = new HashSet<int>();
			var assignments = new List<(int, int, float, int, Guid)>();

			foreach (var (si, ci, sim, offsetSec) in matches.OrderBy(m => m.sourceIdx).ThenBy(m => m.clipIdx)) {
				if (!assignedClips.Add(ci)) continue;

				if (!sourceGroupId.TryGetValue(si, out Guid groupId)) {
					groupId = Guid.NewGuid();
					sourceGroupId[si] = groupId;
				}
				assignments.Add((si, ci, sim, offsetSec, groupId));
			}
			return assignments;
		}

		/// <summary>
		/// Slides <paramref name="shorter"/> over <paramref name="longer"/> and returns the
		/// best average Hamming similarity (0–1) and the offset (in seconds / blocks) at which
		/// it occurs.  Uses SIMD-accelerated XOR where available and skips offsets early when
		/// the accumulated Hamming distance already exceeds what could beat the current best.
		/// </summary>
		/// <param name="minSim">Minimum similarity the caller cares about (e.g. the user threshold).
		/// Offsets that cannot reach this value are skipped via early exit.</param>
		internal static (float similarity, int offsetBlocks) SlidingWindowCompare(uint[] shorter, uint[] longer, float minSim = 0f) {
			int lenS = shorter.Length;
			int lenL = longer.Length;
			int maxOffset = lenL - lenS;
			int totalBitsCapacity = lenS * 32;

			float bestSim = 0f;
			int bestOffset = 0;

			for (int offset = 0; offset <= maxOffset; offset++) {
				// The maximum number of differing bits we can tolerate and still
				// beat the current best (or the caller's minimum threshold).
				int maxAllowedBits = (int)((1f - Math.Max(bestSim, minSim)) * totalBitsCapacity);

				int totalBits = HammingDistance(shorter, longer, offset, lenS, maxAllowedBits);

				if (totalBits > maxAllowedBits)
					continue; // early exit triggered inside HammingDistance

				float sim = 1f - (float)totalBits / totalBitsCapacity;
				if (sim > bestSim) {
					bestSim = sim;
					bestOffset = offset;
				}
			}

			return (bestSim, bestOffset);
		}

		/// <summary>
		/// Computes the Hamming distance (total differing bits) between
		/// <paramref name="a"/>[0..len) and <paramref name="b"/>[offset..offset+len).
		/// Uses 256-bit or 128-bit SIMD for the XOR when hardware support is available.
		/// Returns early (with a value &gt; <paramref name="maxAllowedBits"/>) when the
		/// running total exceeds the budget, avoiding unnecessary work on non-matching offsets.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static int HammingDistance(uint[] a, uint[] b, int offset, int len, int maxAllowedBits) {
			int totalBits = 0;
			int k = 0;

			// --- Vector256 path (8 × uint per iteration) ---
			if (Vector256.IsHardwareAccelerated && len >= 8) {
				ref uint aRef = ref MemoryMarshal.GetArrayDataReference(a);
				ref uint bRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(b), offset);

				for (; k + 8 <= len; k += 8) {
					var va = Vector256.LoadUnsafe(ref aRef, (nuint)k);
					var vb = Vector256.LoadUnsafe(ref bRef, (nuint)k);
					// Popcount over 64-bit lanes: half the PopCount calls of per-uint
					// counting. (Vector256.PopCount still has no hardware path here.)
					var xored = (va ^ vb).AsUInt64();

					totalBits += BitOperations.PopCount(xored.GetElement(0))
							   + BitOperations.PopCount(xored.GetElement(1))
							   + BitOperations.PopCount(xored.GetElement(2))
							   + BitOperations.PopCount(xored.GetElement(3));

					if (totalBits > maxAllowedBits) return totalBits;
				}
			}
			// --- Vector128 path (4 × uint per iteration, e.g. ARM NEON) ---
			else if (Vector128.IsHardwareAccelerated && len >= 4) {
				ref uint aRef = ref MemoryMarshal.GetArrayDataReference(a);
				ref uint bRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(b), offset);

				for (; k + 4 <= len; k += 4) {
					var va = Vector128.LoadUnsafe(ref aRef, (nuint)k);
					var vb = Vector128.LoadUnsafe(ref bRef, (nuint)k);
					var xored = (va ^ vb).AsUInt64();

					totalBits += BitOperations.PopCount(xored.GetElement(0))
							   + BitOperations.PopCount(xored.GetElement(1));

					if (totalBits > maxAllowedBits) return totalBits;
				}
			}

			// --- Scalar remainder ---
			for (; k < len; k++) {
				totalBits += BitOperations.PopCount(a[k] ^ b[offset + k]);
			}

			return totalBits;
		}

		/// <summary>
		/// Post-processes duplicate groups to break apart "daisy chains" where transitive
		/// merging created groups containing items that aren't actually similar to each other.
		/// For each group with 3+ members, builds a pairwise similarity graph, then
		/// iteratively prunes members that are similar to fewer than half the group.
		/// Pruned items are re-clustered into their own groups if they still have matches.
		/// </summary>
		void SplitDaisyChainGroups() {
			// Build a fast lookup from path -> FileEntry for re-comparing pairs.
			var dbLookup = new Dictionary<string, FileEntry>(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
			foreach (FileEntry fe in DatabaseUtils.Database)
				dbLookup[fe.Path] = fe;

			// Group duplicates by GroupId; only process groups with 3+ members.
			var groups = Duplicates
				.GroupBy(d => d.GroupId)
				.Where(g => g.Count() >= 3)
				.ToList();

			if (groups.Count == 0) return;

			int groupsSplit = 0;
			int itemsRemoved = 0;

			foreach (var group in groups) {
				var members = group.ToList();
				int n = members.Count;

				// Resolve FileEntry for each member; skip group if any entry is missing
				// or lacks a compare snapshot (defensive — all visual duplicates stem
				// from the snapshot-validated scan list).
				var entries = new FileEntry[n];
				bool allFound = true;
				for (int i = 0; i < n; i++) {
					if (!dbLookup.TryGetValue(members[i].Path, out var fe) || fe.compareGray == null) {
						allFound = false;
						break;
					}
					entries[i] = fe;
				}
				if (!allFound) continue;

				// Build pairwise similarity matrix.
				var similar = new bool[n, n];
				for (int i = 0; i < n; i++) {
					similar[i, i] = true;
					for (int j = i + 1; j < n; j++) {
						bool isSimilar = CheckIfDuplicate(entries[i], null, null, entries[j], out _);
						similar[i, j] = isSimilar;
						similar[j, i] = isSimilar;
					}
				}

				// Iterative pruning: remove the least-connected member until every
				// remaining member is similar to at least half of the other members.
				var active = new List<int>(Enumerable.Range(0, n));
				var pruned = new List<int>();

				bool changed = true;
				while (changed && active.Count >= 2) {
					changed = false;
					int worstIdx = -1;
					int worstConnections = int.MaxValue;

					for (int ai = 0; ai < active.Count; ai++) {
						int idx = active[ai];
						int connections = 0;
						for (int aj = 0; aj < active.Count; aj++) {
							if (ai != aj && similar[idx, active[aj]])
								connections++;
						}
						if (connections < worstConnections) {
							worstConnections = connections;
							worstIdx = ai;
						}
					}

					// Prune if the least-connected member is similar to fewer than half.
					int requiredConnections = (active.Count - 1 + 1) / 2; // ceiling of (count-1)/2
					if (worstConnections < requiredConnections) {
						pruned.Add(active[worstIdx]);
						active.RemoveAt(worstIdx);
						changed = true;
					}
				}

				if (pruned.Count == 0) continue;

				groupsSplit++;

				// Assign a new GroupId to the surviving core group (if 2+ members remain).
				if (active.Count >= 2) {
					var coreGroupId = Guid.NewGuid();
					foreach (int idx in active)
						members[idx].GroupId = coreGroupId;
				}
				else {
					// Core collapsed to a single item — remove it too.
					foreach (int idx in active) {
						Duplicates.Remove(members[idx]);
						itemsRemoved++;
					}
					active.Clear();
				}

				// Re-cluster pruned items among themselves: form groups from connected
				// components using the same similarity matrix.
				var visited = new HashSet<int>();
				foreach (int seed in pruned) {
					if (visited.Contains(seed)) continue;
					var component = new List<int>();
					var queue = new Queue<int>();
					queue.Enqueue(seed);
					visited.Add(seed);
					while (queue.Count > 0) {
						int cur = queue.Dequeue();
						component.Add(cur);
						foreach (int other in pruned) {
							if (!visited.Contains(other) && similar[cur, other]) {
								visited.Add(other);
								queue.Enqueue(other);
							}
						}
					}

					if (component.Count >= 2) {
						// Recursively validate this sub-group too: apply the same
						// majority-pruning before accepting it.
						var subActive = new List<int>(component);
						bool subChanged = true;
						while (subChanged && subActive.Count >= 2) {
							subChanged = false;
							int subWorstIdx = -1;
							int subWorstConn = int.MaxValue;
							for (int ai = 0; ai < subActive.Count; ai++) {
								int idx = subActive[ai];
								int conn = 0;
								for (int aj = 0; aj < subActive.Count; aj++) {
									if (ai != aj && similar[idx, subActive[aj]])
										conn++;
								}
								if (conn < subWorstConn) {
									subWorstConn = conn;
									subWorstIdx = ai;
								}
							}
							int subRequired = (subActive.Count - 1 + 1) / 2;
							if (subWorstConn < subRequired) {
								// Remove this item entirely — it doesn't fit anywhere.
								Duplicates.Remove(members[subActive[subWorstIdx]]);
								itemsRemoved++;
								subActive.RemoveAt(subWorstIdx);
								subChanged = true;
							}
						}

						if (subActive.Count >= 2) {
							var subGroupId = Guid.NewGuid();
							foreach (int idx in subActive)
								members[idx].GroupId = subGroupId;
						}
						else {
							foreach (int idx in subActive) {
								Duplicates.Remove(members[idx]);
								itemsRemoved++;
							}
						}
					}
					else {
						// Single pruned item with no matches among other pruned items.
						Duplicates.Remove(members[component[0]]);
						itemsRemoved++;
					}
				}
			}

			if (groupsSplit > 0)
				Logger.Instance.Info($"Daisy-chain validation: split {groupsSplit} group(s), removed {itemsRemoved} singleton item(s)");
		}

		void LogGroupStatistics() {
			var groupSizes = Duplicates
				.GroupBy(d => d.GroupId)
				.Select(g => g.Count())
				.ToList();
			if (groupSizes.Count == 0) return;
			int totalItems = groupSizes.Sum();
			int maxSize = groupSizes.Max();
			double avgSize = groupSizes.Average();
			int groupsOver5 = groupSizes.Count(s => s > 5);
			int groupsOver10 = groupSizes.Count(s => s > 10);
			Logger.Instance.Info($"Group statistics: {groupSizes.Count} groups, {totalItems} items, " +
				$"avg size {avgSize:F1}, max size {maxSize}, " +
				$"groups with >5 items: {groupsOver5}, >10 items: {groupsOver10}");
		}

		public async void CleanupDatabase() {
			await Task.Run(() => {
				DatabaseUtils.CleanupDatabase(preserveDeletedContentMemory: Settings.RememberDeletedContent);
			});
			DatabaseCleaned?.Invoke(this, new EventArgs());
		}
		public static void ClearDatabase() => DatabaseUtils.ClearDatabase();

		// A "ghost" is an entry whose file is gone from a currently-MOUNTED drive and that carries
		// no comparable data at all — no usable frame hash and no audio fingerprint. Unlike a
		// tombstone (missing file WITH fingerprints, kept when RememberDeletedContent is on so a
		// re-download is recognized), a ghost can never match anything and can never heal (the
		// file is gone), so it is pure dead weight iterated by every scan. Offline drives are
		// excluded, same discipline as PathIsTombstone: their files may still exist.
		static bool IsGhostEntry(FileEntry e, Dictionary<string, bool> driveReadyCache) {
			if (e.AudioFingerprint != null) return false;
			if (e.grayBytes != null)
				foreach (var v in e.grayBytes.Values)
					if (v != null) return false;   // at least one usable frame hash -> keep as tombstone
			if (File.Exists(e.Path)) return false;
			string root = Path.GetPathRoot(e.Path) ?? string.Empty;
			if (!driveReadyCache.TryGetValue(root, out bool ready))
				driveReadyCache[root] = ready = IsDriveReady(e.Path);
			return ready;
		}
		/// <summary>Counts what <see cref="PruneGhostEntries"/> would remove (read-only preview).</summary>
		public static int CountGhostEntries() {
			var readyCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			int n = 0;
			foreach (var e in DatabaseUtils.Database)
				if (IsGhostEntry(e, readyCache)) n++;
			return n;
		}
		/// <summary>Removes ghost entries and saves the database. Do not call during a scan.</summary>
		public static int PruneGhostEntries() {
			var readyCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			var ghosts = new List<FileEntry>();
			foreach (var e in DatabaseUtils.Database)
				if (IsGhostEntry(e, readyCache)) ghosts.Add(e);
			foreach (var g in ghosts)
				DatabaseUtils.Database.Remove(g);
			if (ghosts.Count > 0)
				DatabaseUtils.SaveDatabase();
			Logger.Instance.Info($"Pruned {ghosts.Count:N0} ghost entries (file missing on a mounted drive, no comparable fingerprint data).");
			return ghosts.Count;
		}
		/// <summary>
		/// Number of database entries whose path lies under <paramref name="folderPath"/> —
		/// the "N files known" a setup screen can show instantly, before any folder walk.
		/// </summary>
		public static int CountDatabaseEntriesUnder(string folderPath) {
			if (string.IsNullOrWhiteSpace(folderPath)) return 0;
			string prefix = folderPath.EndsWith(Path.DirectorySeparatorChar) || folderPath.EndsWith(Path.AltDirectorySeparatorChar)
				? folderPath
				: folderPath + Path.DirectorySeparatorChar;
			var comparison = CoreUtils.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			int n = 0;
			foreach (var e in DatabaseUtils.Database)
				if (e.Path.StartsWith(prefix, comparison)) n++;
			return n;
		}
		/// <summary>Total number of entries in the fingerprint database.</summary>
		public static int DatabaseEntryCount => DatabaseUtils.Database.Count;

		public static bool ExportDataBaseToJson(string jsonFile, JsonSerializerOptions options) => DatabaseUtils.ExportDatabaseToJson(jsonFile, options);
		public static bool ImportDataBaseFromJson(string jsonFile, JsonSerializerOptions options) => DatabaseUtils.ImportDatabaseFromJson(jsonFile, options);

		/// <summary>
		/// Extracts a single JPEG thumbnail from a video or image file on demand.
		/// Intended for web endpoints that need higher resolution than the default 100px scan thumbnails.
		/// </summary>
		/// <param name="filePath">Absolute path to the media file.</param>
		/// <param name="position">Seek position (ignored for images).</param>
		/// <param name="maxWidth">Target width in pixels. 0 = original resolution.</param>
		/// <returns>JPEG bytes, or null on failure.</returns>
		public static byte[]? ExtractThumbnailJpeg(string filePath, TimeSpan position, int maxWidth = 0, int jpegQuality = 0) {
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

			bool isImage = IsImageExtension(Path.GetExtension(filePath));
			return FfmpegEngine.GetThumbnail(new FfmpegSettings {
				File = filePath,
				Position = isImage ? TimeSpan.Zero : position,
				GrayScale = 0,
				Fullsize = (byte)(maxWidth == 0 ? 1 : 0),
				MaxWidth = maxWidth,
				JpegQuality = jpegQuality,
				SoftwareDecodeOnly = isImage,
			}, false);
		}

		static bool IsImageExtension(string ext) =>
			ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
			ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
			ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
			ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
			ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
			ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
			ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
			ext.Equals(".tif", StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Whether an item should be (re)processed for thumbnails. Items with no thumbnails
		/// load on first pass; items whose sole image is the NoThumbnailImage placeholder
		/// represent a prior extraction failure and must remain eligible for explicit retry,
		/// otherwise a "Load thumbnails for group" click silently no-ops on the very items
		/// the user is trying to recover (issue #748).
		/// </summary>
		internal static bool ShouldRetryThumbnails(DuplicateItem item, byte[]? placeholder, int requiredWidth = 0) {
			if (item.ImageList == null || item.ImageList.Count == 0) return true;
			if (placeholder != null && item.ImageList.Count == 1 && ReferenceEquals(item.ImageList[0], placeholder)) return true;
			// Explicit reloads also refresh thumbnails extracted at a smaller width than
			// the current setting (issue #777). Width 0 = unknown (older backups) — those
			// stay as-is rather than forcing a re-extract of everything.
			if (requiredWidth > 0 && item.ThumbnailWidth > 0 && item.ThumbnailWidth < requiredWidth) return true;
			return false;
		}

		/// <summary>
		/// The frame sample positions are populated during scan setup. When results are restored
		/// from a saved backup without running a scan, the list is empty, so video thumbnail
		/// re-extraction would sample zero frames and yield placeholders only (issue #775).
		/// Rebuild it on demand from the configured thumbnail count.
		/// </summary>
		internal void EnsureThumbnailPositions() {
			if (positionList.Count > 0) return;
			float positionCounter = 0f;
			for (int i = 0; i < Settings.ThumbnailCount; i++) {
				positionCounter += 1.0F / (Settings.ThumbnailCount + 1);
				positionList.Add(positionCounter);
			}
		}

		public async Task RetrieveThumbnailsForItems(IEnumerable<DuplicateItem> items) {
			// Explicit reloads also refresh thumbnails whose extraction width is below the
			// current setting (issue #777); the automatic post-scan pass does not.
			int requiredWidth = Settings.ThumbnailMaxWidth > 0 ? Settings.ThumbnailMaxWidth : 100;
			var dupList = items.Where(d => ShouldRetryThumbnails(d, NoThumbnailImage, requiredWidth)).ToList();
			if (dupList.Count == 0) {
				Logger.Instance.Info("Explicit thumbnail retry: nothing to do (all selected items already have up-to-date thumbnails).");
				return;
			}
			EnsureThumbnailPositions();
			Logger.Instance.Info($"Explicit thumbnail retry: starting for {dupList.Count} item(s).");
			int loaded = 0, placeholders = 0, skippedMissing = 0;
			try {
				await Parallel.ForEachAsync(dupList, new ParallelOptions { MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, (entry, cancellationToken) => {
					List<byte[]>? list = null;
					bool needsThumbnails = !Settings.IncludeMissingFiles || File.Exists(entry.Path);
					List<TimeSpan>? timeStamps = null;
					int maxDim = Settings.ThumbnailMaxWidth > 0 ? Settings.ThumbnailMaxWidth : 100;

					if (!needsThumbnails) {
						// Missing on disk (a deleted or offline entry included via IncludeMissingFiles):
						// nothing can be extracted. Items without any thumbnails get the shared
						// placeholder; items that already have some (width-upgrade retries) keep them.
						// Never fall through — SetThumbnails with null timestamps stored a null list
						// that consumers dereference (and tripped the Debug.Assert below).
						Interlocked.Increment(ref skippedMissing);
						if (entry.ImageList is not { Count: > 0 } && NoThumbnailImage != null) {
							entry.ThumbnailWidth = 0;
							entry.SetThumbnails(new List<byte[]> { NoThumbnailImage }, new List<TimeSpan> { TimeSpan.Zero });
						}
						return ValueTask.CompletedTask;
					}
					if (entry.IsImage) {
						timeStamps = new(0);
						list = new List<byte[]>(1);
						var b = ExtractThumbnailJpeg(entry.Path, TimeSpan.Zero, maxDim);
						if (b == null || b.Length == 0) {
							Logger.Instance.Warn($"Failed loading image from file: '{entry.Path}'.");
							return ValueTask.CompletedTask;
						}
						list.Add(b);
						entry.ThumbnailWidth = maxDim;
						Interlocked.Increment(ref loaded);
					}
					else {
						list = new List<byte[]>(positionList.Count);
						timeStamps = new List<TimeSpan>(positionList.Count);
						int failedPositions = 0;
						for (int j = 0; j < positionList.Count; j++) {
							var timestamp = TimeSpan.FromSeconds(entry.Duration.TotalSeconds * positionList[j]);
							var b = FfmpegEngine.ExtractThumbnailJpeg(entry.Path, timestamp, maxDim, Settings.ExtendedFFToolsLogging);
							if (b == null || b.Length == 0) {
								failedPositions++;
								Logger.Instance.Warn($"Failed extracting thumbnail at {timestamp} for '{entry.Path}', skipping that position.");
								continue;
							}
							list.Add(b);
							timeStamps.Add(timestamp);
						}
						if (list.Count == 0 && NoThumbnailImage != null) {
							list.Add(NoThumbnailImage);
							timeStamps.Add(TimeSpan.Zero);
							entry.ThumbnailWidth = 0;
							Logger.Instance.Warn($"Using placeholder for '{entry.Path}' — all {positionList.Count} sample position(s) failed.");
							Interlocked.Increment(ref placeholders);
						}
						else if (list.Count > 0 && failedPositions > 0) {
							entry.ThumbnailWidth = maxDim;
							Logger.Instance.Warn($"Loaded {list.Count}/{positionList.Count} thumbnail(s) for '{entry.Path}' ({failedPositions} position(s) failed).");
							Interlocked.Increment(ref loaded);
						}
						else if (list.Count > 0) {
							entry.ThumbnailWidth = maxDim;
							Interlocked.Increment(ref loaded);
						}
					}
					Debug.Assert(timeStamps != null);
					entry.SetThumbnails(list ?? (NoThumbnailImage != null ? new() { NoThumbnailImage } : new()), timeStamps!);

					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
			Logger.Instance.Info($"Explicit thumbnail retry complete: {loaded} fully loaded, {placeholders} placeholder, {skippedMissing} skipped (missing on disk).");
		}
		public async void RetrieveThumbnails() {
			var dupList = Duplicates.Where(d => ShouldRetryThumbnails(d, NoThumbnailImage)).ToList();
			int total = dupList.Count;
			int done = 0;
			int lastNotified = 0;
			int loaded = 0, placeholders = 0, skippedMissing = 0;
			Logger.Instance.Info($"Thumbnail loading: starting for {total} item(s).");

			var totalSw = Stopwatch.StartNew();
			var sw = Stopwatch.StartNew();
			try {
				await Parallel.ForEachAsync(dupList, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, (entry, cancellationToken) => {
					List<byte[]>? list = null;
					bool needsThumbnails = !Settings.IncludeMissingFiles || File.Exists(entry.Path);
					List<TimeSpan>? timeStamps = null;

					int current = Interlocked.Increment(ref done);
					if (sw.ElapsedMilliseconds > 300)
						if (Interlocked.Exchange(ref lastNotified, current) < current) {
							sw.Restart(); // only this thread resets the stopwatch
							ThumbnailProgress?.Invoke(current, total);
						}

					int maxDim = Settings.ThumbnailMaxWidth > 0 ? Settings.ThumbnailMaxWidth : 100;

					if (!needsThumbnails) {
						// Missing on disk (a deleted or offline entry included via IncludeMissingFiles):
						// nothing can be extracted. Items without any thumbnails get the shared
						// placeholder; items that already have some (width-upgrade retries) keep them.
						// Never fall through — SetThumbnails with null timestamps stored a null list
						// that consumers dereference (and tripped the Debug.Assert below).
						Interlocked.Increment(ref skippedMissing);
						if (entry.ImageList is not { Count: > 0 } && NoThumbnailImage != null) {
							entry.ThumbnailWidth = 0;
							entry.SetThumbnails(new List<byte[]> { NoThumbnailImage }, new List<TimeSpan> { TimeSpan.Zero });
						}
						return ValueTask.CompletedTask;
					}
					if (entry.IsImage) {
						//For images it doesn't make sense to load the actual image more than once
						timeStamps = new(0);
						list = new List<byte[]>(1);
						var b = ExtractThumbnailJpeg(entry.Path, TimeSpan.Zero, maxDim);
						if (b == null || b.Length == 0) {
							Logger.Instance.Warn($"Failed loading image from file: '{entry.Path}'.");
							return ValueTask.CompletedTask;
						}
						list.Add(b);
						entry.ThumbnailWidth = maxDim;
						Interlocked.Increment(ref loaded);
					}
					else {
						list = new List<byte[]>(positionList.Count);
						timeStamps = new List<TimeSpan>(positionList.Count);
						int failedPositions = 0;
						for (int j = 0; j < positionList.Count; j++) {
							var timestamp = TimeSpan.FromSeconds(entry.Duration.TotalSeconds * positionList[j]);
							var b = FfmpegEngine.ExtractThumbnailJpeg(entry.Path, timestamp, maxDim, Settings.ExtendedFFToolsLogging);
							if (b == null || b.Length == 0) {
								failedPositions++;
								Logger.Instance.Warn($"Failed extracting thumbnail at {timestamp} for '{entry.Path}', skipping that position.");
								continue;
							}
							list.Add(b);
							timeStamps.Add(timestamp);
						}
						if (list.Count == 0 && NoThumbnailImage != null) {
							list.Add(NoThumbnailImage);
							timeStamps.Add(TimeSpan.Zero);
							entry.ThumbnailWidth = 0;
							Logger.Instance.Warn($"Using placeholder for '{entry.Path}' — all {positionList.Count} sample position(s) failed.");
							Interlocked.Increment(ref placeholders);
						}
						else if (list.Count > 0 && failedPositions > 0) {
							entry.ThumbnailWidth = maxDim;
							Logger.Instance.Warn($"Loaded {list.Count}/{positionList.Count} thumbnail(s) for '{entry.Path}' ({failedPositions} position(s) failed).");
							Interlocked.Increment(ref loaded);
						}
						else if (list.Count > 0) {
							entry.ThumbnailWidth = maxDim;
							Interlocked.Increment(ref loaded);
						}
					}
					Debug.Assert(timeStamps != null);
					entry.SetThumbnails(list ?? (NoThumbnailImage != null ? new() { NoThumbnailImage } : new()), timeStamps!);

					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
			Logger.Instance.Info($"Thumbnail loading complete: {loaded} fully loaded, {placeholders} placeholder, {skippedMissing} skipped (missing on disk) in {totalSw.Elapsed.TotalSeconds:F1}s.");
			ThumbnailsRetrieved?.Invoke(this, new EventArgs());
		}

		/// <summary>
		/// Fetches the 224x224 RGB frame for an image and queues it for embedding when the
		/// sink still needs it. Failures are non-fatal: the image just stays without an
		/// embedding and the AI pass abstains for it.
		/// </summary>
		static void TryQueueImageEmbeddingFrame(FileEntry imageFile, AI.IEmbeddingFrameSink? embeddingSink, bool extendedLogging) {
			if (embeddingSink?.WantsEmbedding(imageFile, 0) != true)
				return;
			byte[]? rgb = FfmpegEngine.GetThumbnail(new FfmpegSettings {
				File = imageFile.Path,
				Position = TimeSpan.Zero,
				Rgb224 = true,
				SoftwareDecodeOnly = true,
			}, extendedLogging);
			if (rgb != null)
				embeddingSink.SubmitFrame(imageFile, 0, rgb);
		}

		static bool GetGrayBytesFromImage(FileEntry imageFile, bool useExifIfAvailable, bool extendedLogging, AI.IEmbeddingFrameSink? embeddingSink = null) {
			try {
				// Decode through FFmpeg — the same pipeline videos use — so image and video
				// gray bytes share identical grayscale conversion and scaling.
				byte[]? grayBytes;
				int width, height;
				if (!FfmpegEngine.TryGetImageInfoAndGrayBytes(imageFile.Path, out grayBytes, out width, out height, extendedLogging)) {
					// CLI fallback. Read dimensions straight from the file header first: some
					// PNGs trip FFprobe's demuxer with a bogus "chunk too big" error (#805),
					// and the header carries the dimensions without decoding. Only fall back
					// to FFprobe when the header reader doesn't recognise the format.
					if (!ImageHeader.TryGetDimensions(imageFile.Path, out width, out height)) {
						MediaInfo? info = FFProbeEngine.GetMediaInfo(imageFile.Path, extendedLogging);
						var stream = info?.Streams?.FirstOrDefault(s => s.Width > 0 && s.Height > 0);
						width = stream?.Width ?? 0;
						height = stream?.Height ?? 0;
					}
					grayBytes = FfmpegEngine.GetThumbnail(new FfmpegSettings {
						File = imageFile.Path,
						Position = TimeSpan.Zero,
						GrayScale = 1,
						SoftwareDecodeOnly = true,
					}, extendedLogging);
				}

				if (grayBytes == null) {
					imageFile.Flags.Set(EntryFlags.ThumbnailError);
					return false;
				}

				imageFile.mediaInfo = new MediaInfo {
					Streams = new[] {
							new MediaInfo.StreamInfo {Height = height, Width = width}
						}
				};

				// Extract EXIF capture date if enabled
				if (useExifIfAvailable) {
					if (ExifReader.TryGetDateTaken(imageFile.Path, out DateTime exifDate)) {
						imageFile.DateCreated = exifDate;
					}
					else {
						// HEIC/HEIF carry the date in the container instead; read it via FFprobe.
						string ext = Path.GetExtension(imageFile.Path);
						if (ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
							ext.Equals(".heif", StringComparison.OrdinalIgnoreCase)) {
							var creationTime = FFProbeEngine.GetCreationTime(imageFile.Path);
							if (creationTime.HasValue)
								imageFile.DateCreated = creationTime.Value;
						}
					}
				}

				if (!GrayBytesUtils.VerifyGrayScaleValues(grayBytes)) {
					imageFile.Flags.Set(EntryFlags.TooDark);
					Logger.Instance.Warn($"Graybytes too dark of: {imageFile.Path}");
					return false;
				}

				imageFile.grayBytes.Add(0, grayBytes);
				TryQueueImageEmbeddingFrame(imageFile, embeddingSink, extendedLogging);
				return true;
			}
			catch (Exception ex) {
				Logger.Instance.Error(
					$"Exception, file: {imageFile.Path}, reason: {ex.Message}, stacktrace {ex.StackTrace}");
				imageFile.Flags.Set(EntryFlags.ThumbnailError);
				return false;
			}
		}

		internal void HighlightBestMatches() {
			// One pass per group: find the best value per metric and mark every item
			// that ties it. Equivalent to the previous sort-and-walk-ties logic, but
			// without re-filtering the whole duplicate set per item and re-sorting per
			// metric, which was quadratic in the number of results.
			foreach (var group in Duplicates.GroupBy(d => d.GroupId)) {
				List<DuplicateItem> items = group.ToList();
				// Groups are homogeneous: images are only ever compared with images.
				bool isImage = items[0].IsImage;

				if (!isImage) {
					TimeSpan bestDuration = items.Max(d => d.Duration);
					foreach (DuplicateItem d in items)
						if (d.Duration == bestDuration) d.IsBestDuration = true;
				}

				long bestSize = items.Min(d => d.SizeLong);
				foreach (DuplicateItem d in items)
					if (d.SizeLong == bestSize) d.IsBestSize = true;

				if (!isImage) {
					float bestFps = items.Max(d => d.Fps);
					foreach (DuplicateItem d in items)
						if (d.Fps == bestFps) d.IsBestFps = true;

					decimal bestBitRate = items.Max(d => d.BitRateKbs);
					foreach (DuplicateItem d in items)
						if (d.BitRateKbs == bestBitRate) d.IsBestBitRateKbs = true;

					int bestAudioSampleRate = items.Max(d => d.AudioSampleRate);
					foreach (DuplicateItem d in items)
						if (d.AudioSampleRate == bestAudioSampleRate) d.IsBestAudioSampleRate = true;

					decimal bestAudioBitRate = items.Max(d => d.AudioBitRateKbs);
					foreach (DuplicateItem d in items)
						if (d.AudioBitRateKbs == bestAudioBitRate) d.IsBestAudioBitRateKbs = true;

					int bestHdrRank = items.Max(d => d.HdrFormatRank);
					foreach (DuplicateItem d in items)
						if (d.HdrFormatRank == bestHdrRank) d.IsBestHdrFormat = true;
				}

				int bestFrameSize = items.Max(d => d.FrameSizeInt);
				foreach (DuplicateItem d in items)
					if (d.FrameSizeInt == bestFrameSize) d.IsBestFrameSize = true;
			}
		}

		public void Pause() {
			if (!isScanning || pauseTokenSource.IsPaused) return;
			Logger.Instance.Info("Scan paused by user");
			ElapsedTimer.Stop();
			SearchTimer.Stop();
			pauseTokenSource.IsPaused = true;
			// Safe suspend point: flush completed work off the caller's (UI) thread so closing
			// the app while paused loses nothing. Files that were mid-processing when the pause
			// hit finish first (workers park at WaitWhilePaused between files) and land in the
			// next checkpoint or the final save.
			Task.Run(FlushDatabase);
		}

		public void Resume() {
			if (!isScanning || pauseTokenSource.IsPaused != true) return;
			Logger.Instance.Info("Scan resumed by user");
			ElapsedTimer.Start();
			SearchTimer.Start();
			pauseTokenSource.IsPaused = false;
		}

		public void Stop() {
			Logger.Instance.Info("Scan stopped by user");
			if (isScanning)
				cancelationTokenSource.Cancel();
			// Cancel before resuming: workers parked in WaitWhilePaused observe the
			// cancelled token and throw instead of waking up and fully processing one
			// more file each (with a dead token that would poison its results).
			if (pauseTokenSource.IsPaused)
				Resume();
			else
				// No scan task is alive to observe the cancellation, so nothing would ever
				// raise ScanAborted. A frontend that still believes a scan is running (its
				// Stop was clickable) would otherwise wait on its busy overlay forever (#821)
				// — tell it the scan is over so it can reset.
				ScanAborted?.Invoke(this, new EventArgs());
		}
	}
}
