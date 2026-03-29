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

global using System;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.IO;
global using System.Threading;
global using System.Threading.Tasks;
global using SixLabors.ImageSharp;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	public sealed class ScanEngine {
		public HashSet<DuplicateItem> Duplicates { get; set; } = new HashSet<DuplicateItem>();
		public Settings Settings { get; } = new Settings();
		public event EventHandler<ScanProgressChangedEventArgs>? Progress;
		public event EventHandler? BuildingHashesDone;
		public event EventHandler? ScanDone;
		public event EventHandler? ScanAborted;
		public event EventHandler? ThumbnailsRetrieved;
		public event Action<int, int>? ThumbnailProgress;
		public event EventHandler? FilesEnumerated;
		public event EventHandler? DatabaseCleaned;

		public Image? NoThumbnailImage;

		PauseTokenSource pauseTokenSource = new();
		CancellationTokenSource cancelationTokenSource = new();
		readonly List<float> positionList = new();

		bool isScanning;
		int scanProgressMaxValue;
		readonly Stopwatch SearchTimer = new();
		public Stopwatch ElapsedTimer = new();
		int processedFiles;
		DateTime startTime = DateTime.Now;
		DateTime lastProgressUpdate = DateTime.MinValue;
		static readonly TimeSpan progressUpdateIntervall = TimeSpan.FromMilliseconds(300);
		const int maxExcludedLogsPerReason = 5;
		readonly ConcurrentDictionary<string, int> excludedReasonCounts = new();
		readonly ConcurrentDictionary<string, int> excludedReasonLoggedCounts = new();

		string T(string key, params object[] args) =>
			LanguageService.Instance.Get(Settings.LanguageCode, key, args);

		void InitProgress(int count) {
			startTime = DateTime.UtcNow;
			scanProgressMaxValue = count;
			processedFiles = 0;
			lastProgressUpdate = DateTime.MinValue;
		}
		void ResetExcludedLogging() {
			excludedReasonCounts.Clear();
			excludedReasonLoggedCounts.Clear();
		}
		void LogExcludedFile(FileEntry entry, string reason) {
			if (!Settings.LogExcludedFiles)
				return;
			var totalCount = excludedReasonCounts.AddOrUpdate(reason, 1, (_, count) => count + 1);
			var loggedCount = excludedReasonLoggedCounts.GetOrAdd(reason, 0);
			if (loggedCount >= maxExcludedLogsPerReason)
				return;
			loggedCount = excludedReasonLoggedCounts.AddOrUpdate(reason, 1, (_, count) => count + 1);
			if (loggedCount <= maxExcludedLogsPerReason)
				Logger.Instance.Info(T("Log.ExcludedFile", entry.Path, reason, totalCount));
		}
		void LogExcludedSummary() {
			if (!Settings.LogExcludedFiles || excludedReasonCounts.IsEmpty)
				return;
			Logger.Instance.Info(T("Log.ExcludedFilesSummary"));
			foreach (var reason in excludedReasonCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) {
				var loggedCount = excludedReasonLoggedCounts.TryGetValue(reason.Key, out var value) ? value : 0;
				var suppressedCount = Math.Max(0, reason.Value - loggedCount);
				var suppressionText = suppressedCount > 0 ? T("Log.ExcludedFilesSuppressed", suppressedCount) : string.Empty;
				Logger.Instance.Info(T("Log.ExcludedFilesSummaryItem", reason.Key, reason.Value, suppressionText));
			}
		}
		void IncrementProgress(string path) {
			processedFiles++;
			var pushUpdate = processedFiles == scanProgressMaxValue ||
								lastProgressUpdate + progressUpdateIntervall < DateTime.UtcNow;
			if (!pushUpdate) return;
			lastProgressUpdate = DateTime.UtcNow;
			var timeRemaining = TimeSpan.FromTicks(DateTime.UtcNow.Subtract(startTime).Ticks *
									(scanProgressMaxValue - (processedFiles + 1)) / (processedFiles + 1));
			Progress?.Invoke(this,
							new ScanProgressChangedEventArgs {
								CurrentPosition = processedFiles,
								CurrentFile = path,
								Elapsed = ElapsedTimer.Elapsed,
								Remaining = timeRemaining,
								MaxPosition = scanProgressMaxValue
							});
		}

		public static bool FFmpegExists => !string.IsNullOrEmpty(FfmpegEngine.FFmpegPath);
		public static bool FFprobeExists => !string.IsNullOrEmpty(FFProbeEngine.FFprobePath);
		public static bool NativeFFmpegExists => FFTools.FFmpegNative.FFmpegHelper.DoFFmpegLibraryFilesExist;

		public async void StartSearch() {
			PrepareSearch();
			SearchTimer.Start();
			ElapsedTimer.Start();
			Logger.Instance.InsertSeparator('-');
			Logger.Instance.Info(T("Log.BuildingFileList"));
			await BuildFileList(cancelationTokenSource.Token);
			Logger.Instance.Info(T("Log.FinishedBuildingFileList", SearchTimer.StopGetElapsedAndRestart()));
			FilesEnumerated?.Invoke(this, new EventArgs());
			Logger.Instance.Info(T("Log.GatheringMediaInfo"));
			if (!cancelationTokenSource.IsCancellationRequested)
				await GatherInfos();
			Logger.Instance.Info(T("Log.FinishedGatheringHashes", SearchTimer.StopGetElapsedAndRestart()));
			BuildingHashesDone?.Invoke(this, new EventArgs());
			DatabaseUtils.SaveDatabase();
			if (!cancelationTokenSource.IsCancellationRequested) {
				StartCompare();
			}
			else {
				ScanAborted?.Invoke(this, new EventArgs());
				Logger.Instance.Info(T("Log.ScanAborted"));
				isScanning = false;
			}
		}

		public async void StartCompare() {
			PrepareCompare();
			SearchTimer.Start();
			ElapsedTimer.Start();
			Logger.Instance.Info(T("Log.ScanForDuplicates"));
			if (!cancelationTokenSource.IsCancellationRequested)
				await Task.Run(ScanForDuplicates, cancelationTokenSource.Token);
			if (!cancelationTokenSource.IsCancellationRequested && Settings.EnablePartialClipDetection)
				await Task.Run(ScanForPartialDuplicates, cancelationTokenSource.Token);
			SearchTimer.Stop();
			ElapsedTimer.Stop();
			Logger.Instance.Info(T("Log.FinishedScanForDuplicates", SearchTimer.Elapsed));
			LogGroupStatistics();
			Logger.Instance.Info(T("Log.HighlightingBestResults"));
			HighlightBestMatches();
			ScanDone?.Invoke(this, new EventArgs());
			Logger.Instance.Info(T("Log.ScanDone"));
			DatabaseUtils.SaveDatabase();
			isScanning = false;
		}

		void PrepareSearch() {
			ResetExcludedLogging();
			//Using VDF.GUI we know fftools exist at this point but VDF.Core might be used in other projects as well
			if (!Settings.UseNativeFfmpegBinding && !FFmpegExists)
				throw new FFNotFoundException("Cannot find FFmpeg");
			if (!FFprobeExists)
				throw new FFNotFoundException("Cannot find FFprobe");
			if (Settings.UseNativeFfmpegBinding && !FFTools.FFmpegNative.FFmpegHelper.DoFFmpegLibraryFilesExist)
				throw new FFNotFoundException("Cannot find FFmpeg libraries");

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

			float positionCounter = 0f;
			for (int i = 0; i < Settings.ThumbnailCount; i++) {
				positionCounter += 1.0F / (Settings.ThumbnailCount + 1);
				positionList.Add(positionCounter);
			}

			isScanning = true;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		double GetGrayBytesIndex(FileEntry entry, float position) =>
			entry.GetGrayBytesIndex(position, Settings.MaxSamplingDurationSeconds);

		void PrepareCompare() {
			if (Settings.ThumbnailCount != positionList.Count) {
				throw new Exception("Number of thumbnails can't be changed between quick rescans! Rescan has been aborted.");
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

			foreach (string path in Settings.IncludeList) {
				if (cancellationToken.IsCancellationRequested)
					return;
				if (!Directory.Exists(path)) continue;

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
						Logger.Instance.Info($"Skipped file '{file}' because of {e}");
						continue;
					}
					if (!DatabaseUtils.Database.TryGetValue(fEntry, out var dbEntry))
						DatabaseUtils.Database.Add(fEntry);
					else if (fEntry.DateCreated != dbEntry.DateCreated ||
							fEntry.DateModified != dbEntry.DateModified ||
							fEntry.FileSize != dbEntry.FileSize) {
						// -> Modified or different file
						DatabaseUtils.Database.Remove(dbEntry);
						DatabaseUtils.Database.Add(fEntry);
					}
				}
			}

			Logger.Instance.Info($"Files in database: {DatabaseUtils.Database.Count:N0} ({DatabaseUtils.Database.Count - oldFileCount:N0} files added)");
		});

		// Check if entry should be excluded from the scan for any reason
		// Returns true if the entry is invalid (should be excluded)
		bool InvalidEntry(FileEntry entry, out bool reportProgress, out string? reason) {
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

			if (!Settings.ScanAgainstEntireDatabase) {
				/* Skip non-included file before checking if it exists
				 * This greatly improves performance if the file is on
				 * a disconnected network/mobile drive
				 */
				if (Settings.IncludeSubDirectories == false) {
					if (!Settings.IncludeList.Contains(entry.Folder)) {
						reportProgress = false;
						reason = "path is not in the included directories list";
						return true;
					}
				}
				else if (!Settings.IncludeList.Any(f => {
					if (!entry.Folder.StartsWith(f))
						return false;
					if (entry.Folder.Length == f.Length)
						return true;
					//Reason: https://github.com/0x90d/videoduplicatefinder/issues/249
					string relativePath = Path.GetRelativePath(f, entry.Folder);
					return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
				})) {
					reportProgress = false;
					reason = "path is not in the included directories list";
					return true;
				}
			}

			if (entry.Flags.Has(EntryFlags.ManuallyExcluded)) {
				reason = "file has been manually excluded";
				return true;
			}
			if (entry.Flags.Has(EntryFlags.TooDark)) {
				reason = "file is marked as too dark";
				return true;
			}
			if (!Settings.IncludeNonExistingFiles && !File.Exists(entry.Path))
			{
				reason = "file does not exist";
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

			if (Settings.IgnoreReparsePoints && File.Exists(entry.Path) && File.ResolveLinkTarget(entry.Path, returnFinalTarget: false) != null) {
				reason = "file is a reparse point";
				return true;
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
		public static void SaveDatabase() => DatabaseUtils.SaveDatabase();
		public static void RemoveFromDatabase(FileEntry dbEntry) => DatabaseUtils.Database.Remove(dbEntry);
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

		async Task GatherInfos() {
			try {
				InitProgress(DatabaseUtils.Database.Count);
				await Parallel.ForEachAsync(DatabaseUtils.Database, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, (entry, token) => {
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

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

					if (!skipEntry && !Settings.ScanAgainstEntireDatabase) {
						if (Settings.IncludeSubDirectories == false) {
							if (!Settings.IncludeList.Contains(entry.Folder)) {
								skipEntry = true;
								skipReason = "path is not in the included directories list";
							}
						}
						else if (!Settings.IncludeList.Any(f => {
							if (!entry.Folder.StartsWith(f))
								return false;
							if (entry.Folder.Length == f.Length)
								return true;
							//Reason: https://github.com/0x90d/videoduplicatefinder/issues/249
							string relativePath = Path.GetRelativePath(f, entry.Folder);
							return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
						})) {
							skipEntry = true;
							skipReason = "path is not in the included directories list";
						}
					}

					if (skipEntry) {
						entry.invalid = true;
						if (!wasInvalid && skipReason != null)
							LogExcludedFile(entry, skipReason);
						if (reportProgress)
							IncrementProgress(entry.Path);
						return ValueTask.CompletedTask;
					}
					if (Settings.IncludeNonExistingFiles && entry.grayBytes?.Count > 0) {
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
						if (hasAllInformation) {
							// Thumbnails are cached but audio fingerprint might still be needed
							if (Settings.EnablePartialClipDetection &&
								!entry.IsImage &&
								!entry.Flags.Has(EntryFlags.NoAudioTrack) &&
								!entry.Flags.Has(EntryFlags.AudioFingerprintError) &&
								entry.AudioFingerprint == null) {
								ExtractAudioFingerprint(entry, cancelationTokenSource.Token);
							}
							IncrementProgress(entry.Path);
							return ValueTask.CompletedTask;
						}
					}

					if (entry.mediaInfo == null && !entry.IsImage) {
						MediaInfo? info = FFProbeEngine.GetMediaInfo(entry.Path, Settings.ExtendedFFToolsLogging);
						if (info == null) {
							entry.invalid = true;
							entry.Flags.Set(EntryFlags.MetadataError);
							IncrementProgress(entry.Path);
							return ValueTask.CompletedTask;
						}

						entry.mediaInfo = info;
					}
					// This is for people upgrading from an older VDF version
					// Or if you create a new database, start and immediately stop the scan and then try to scan again
					entry.grayBytes ??= new Dictionary<double, byte[]?>();
					entry.PHashes ??= new Dictionary<double, ulong?>();


					if (entry.IsImage && entry.grayBytes.Count == 0) {
						if (!GetGrayBytesFromImage(entry, Settings.UseExifCreationDate))
							entry.invalid = true;
					}
					else if (!entry.IsImage) {
						if (!FfmpegEngine.GetGrayBytesFromVideo(entry, positionList, Settings.MaxSamplingDurationSeconds, Settings.ExtendedFFToolsLogging))
							entry.invalid = true;
					}

					// Audio fingerprint — videos only, only when enabled,
					// skipped if already cached or flagged as having no audio track.
					if (Settings.EnablePartialClipDetection &&
						!entry.IsImage &&
						!entry.Flags.Has(EntryFlags.NoAudioTrack) &&
						!entry.Flags.Has(EntryFlags.AudioFingerprintError) &&
						entry.AudioFingerprint == null) {
						ExtractAudioFingerprint(entry, cancelationTokenSource.Token);
					}

					IncrementProgress(entry.Path);
					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
			finally {
				LogExcludedSummary();
			}
		}

	
	static void ExtractAudioFingerprint(FileEntry entry, CancellationToken ct = default) {
		uint[]? fp = FFTools.ChromaprintEngine.ExtractFingerprint(entry.Path, false, ct);
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
		else {
			entry.AudioFingerprint = fp;
		}
	}

	Dictionary<double, byte[]?> CreateFlippedGrayBytes(FileEntry entry) {
			Dictionary<double, byte[]?>? flippedGrayBytes = new();
			if (entry.IsImage)
				flippedGrayBytes.Add(0, DatabaseUtils.DbVersion < 2 ? GrayBytesUtils.FlipGrayScale16x16(entry.grayBytes[0]!) : GrayBytesUtils.FlipGrayScale(entry.grayBytes[0]!));
			else {
				for (int j = 0; j < positionList.Count; j++) {
					double idx = GetGrayBytesIndex(entry, positionList[j]);
					flippedGrayBytes.Add(idx, DatabaseUtils.DbVersion < 2 ? GrayBytesUtils.FlipGrayScale16x16(entry.grayBytes[idx]!) : GrayBytesUtils.FlipGrayScale(entry.grayBytes[idx]!));
				}
			}
			return flippedGrayBytes;
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

	bool CheckIfDuplicate(FileEntry entry, Dictionary<double, byte[]?>? grayBytes, FileEntry compItem, out float difference) {
			grayBytes ??= entry.grayBytes;
			float differenceLimit = 1.0f - Settings.Percent / 100f;
			bool ignoreBlackPixels = Settings.IgnoreBlackPixels;
			bool ignoreWhitePixels = Settings.IgnoreWhitePixels;
			difference = 1f;

			if (entry.IsImage) {
				difference = ignoreBlackPixels || ignoreWhitePixels ?
								GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(grayBytes[0]!, compItem.grayBytes[0]!, ignoreBlackPixels, ignoreWhitePixels) :
								GrayBytesUtils.PercentageDifference(grayBytes[0]!, compItem.grayBytes[0]!);
				return difference <= differenceLimit;
			}

			if (Settings.UsePHashing) {
				float differenceLimitpHash = Settings.Percent / 100f;

				double entryIndex = GetGrayBytesIndex(entry, positionList[0]);
				double compIndex = GetGrayBytesIndex(compItem, positionList[0]);
				if (!entry.PHashes.TryGetValue(entryIndex, out ulong? phash))
					phash = pHash.PerceptualHash.ComputePHashFromGray32x32(grayBytes[entryIndex]);
				if (!compItem.PHashes.TryGetValue(compIndex, out ulong? phash_comp))
					phash_comp = pHash.PerceptualHash.ComputePHashFromGray32x32(compItem.grayBytes[compIndex]);
				if (phash == null || phash_comp == null) {
					Logger.Instance.Info($"Failed to compute pHash for {entry.Path} or {compItem.Path}");
					difference = 1f;
					return false;
				}
				bool isDup = pHash.PHashCompare.IsDuplicateByPercent(phash.Value, phash_comp.Value, out float similarity, differenceLimitpHash, strict: true);
				difference = 1f - similarity;
				return isDup;

			}



			differenceLimit *= positionList.Count;
			float diffSum = 0;
			for (int j = 0; j < positionList.Count; j++) {
				diffSum += ignoreBlackPixels || ignoreWhitePixels ?
							GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(
								grayBytes[GetGrayBytesIndex(entry, positionList[j])]!,
								compItem.grayBytes[GetGrayBytesIndex(compItem, positionList[j])]!, ignoreBlackPixels, ignoreWhitePixels) :
							GrayBytesUtils.PercentageDifference(
								grayBytes[GetGrayBytesIndex(entry, positionList[j])]!,
								compItem.grayBytes[GetGrayBytesIndex(compItem, positionList[j])]!);
				if (diffSum > differenceLimit) // already exceeding maximum tolerated diff -> exit early
					return false;
			}
			difference = diffSum / positionList.Count;
			return !float.IsNaN(difference);
		}

		void ScanForDuplicates() {
			Dictionary<string, DuplicateItem>? duplicateDict = new();
			// Maps GroupId -> representative FileEntry for that group.
			// Used to prevent merging groups whose representatives aren't similar.
			Dictionary<Guid, FileEntry> groupRepresentatives = new();
			int mergesBlocked = 0;

			//Exclude existing database entries which not met current scan settings
			List<FileEntry> ScanList = new();

			Logger.Instance.Info("Prepare list of items to compare...");
			foreach (FileEntry entry in DatabaseUtils.Database) {
				if (!InvalidEntryForDuplicateCheck(entry)) {
					ScanList.Add(entry);
				}
			}

			Logger.Instance.Info($"Scanning for duplicates in {ScanList.Count:N0} files");

			InitProgress(ScanList.Count);

			// Duration buckets are keyed by whole seconds to keep percent-based tolerance intact.
			const int bucketSizeSeconds = 1;
			// Avoid bucket overhead for small datasets; fall back to the linear path.
			const int bucketActivationThreshold = 5000;
			// scanIndex preserves original ordering so we can skip symmetric comparisons.
			var scanIndex = new Dictionary<FileEntry, int>(ScanList.Count);
			var imageEntries = new List<FileEntry>();
			var videoEntries = new List<FileEntry>();
			var videoBuckets = new Dictionary<int, List<FileEntry>>();
			const int largeBucketThreshold = 400;

			for (int i = 0; i < ScanList.Count; i++) {
				var entry = ScanList[i];
				scanIndex[entry] = i;
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
								!CheckIfDuplicate(repBase, null, repComp, out _)) {
								mergesBlocked++;
								return; // Representatives aren't similar — don't merge.
							}
							Guid groupID = existingComp!.GroupId;
							foreach (DuplicateItem? dup in duplicateDict.Values.Where(c =>
								c.GroupId == groupID))
								dup.GroupId = existingBase.GroupId;
							// Keep the representative of the absorbing group; remove the merged one.
							groupRepresentatives.Remove(groupID);
						}
					}
					else if (foundBase) {
						// New item joining an existing group — verify it matches the representative.
						if (groupRepresentatives.TryGetValue(existingBase!.GroupId, out var rep) &&
							!CheckIfDuplicate(rep, null, compItem, out _)) {
							mergesBlocked++;
							return;
						}
						duplicateDict.TryAdd(compItem.Path,
							new DuplicateItem(compItem, difference, existingBase!.GroupId, flags));
					}
					else if (foundComp) {
						// New item joining an existing group — verify it matches the representative.
						if (groupRepresentatives.TryGetValue(existingComp!.GroupId, out var rep) &&
							!CheckIfDuplicate(rep, null, entry, out _)) {
							mergesBlocked++;
							return;
						}
						duplicateDict.TryAdd(entry.Path,
							new DuplicateItem(entry, difference, existingComp!.GroupId, flags));
					}
					else {
						var groupId = Guid.NewGuid();
						duplicateDict.TryAdd(compItem.Path, new DuplicateItem(compItem, difference, groupId, flags));
						duplicateDict.TryAdd(entry.Path, new DuplicateItem(entry, difference, groupId, DuplicateFlags.None));
						groupRepresentatives[groupId] = entry;
					}
				}
			}

			bool TryCheckDuplicate(FileEntry entry, FileEntry compItem, Dictionary<double, byte[]?>? flippedGrayBytes, out float difference, out DuplicateFlags flags) {
				flags = DuplicateFlags.None;
				difference = 0;
				bool isDuplicate = CheckIfDuplicate(entry, null, compItem, out difference);
				if (Settings.CompareHorizontallyFlipped &&
					CheckIfDuplicate(entry, flippedGrayBytes, compItem, out float flippedDifference)) {
					if (!isDuplicate || flippedDifference < difference) {
						flags |= DuplicateFlags.Flipped;
						isDuplicate = true;
						difference = flippedDifference;
					}
				}
				return isDuplicate;
			}

			// Convert percent-based duration tolerance into seconds, clamped by optional min/max.
			double GetDurationToleranceSeconds(double durationSeconds) {
				double percentSeconds = durationSeconds * Settings.PercentDurationDifference / 100d;
				double toleranceSeconds = percentSeconds;
				if (Settings.DurationDifferenceMinSeconds > 0) {
					toleranceSeconds = Math.Max(toleranceSeconds, Settings.DurationDifferenceMinSeconds);
				}
				if (Settings.DurationDifferenceMaxSeconds > 0) {
					toleranceSeconds = Math.Min(toleranceSeconds, Settings.DurationDifferenceMaxSeconds);
				}
				return Math.Max(0d, toleranceSeconds);
			}

			// Compare one entry against candidate buckets (bucketed path).
			void CompareEntry(FileEntry entry, int entryIndex, IEnumerable<int> candidateBucketKeys) {
				while (pauseTokenSource.IsPaused) Thread.Sleep(50);

				float difference = 0;
				bool isDuplicate;
				DuplicateFlags flags;
				Dictionary<double, byte[]?>? flippedGrayBytes = null;
				double entryDurationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
				double entryToleranceSeconds = GetDurationToleranceSeconds(entryDurationSeconds);

				if (Settings.CompareHorizontallyFlipped)
					flippedGrayBytes = CreateFlippedGrayBytes(entry);

				foreach (int bucketKey in candidateBucketKeys) {
					if (!videoBuckets.TryGetValue(bucketKey, out var bucketEntries))
						continue;
					foreach (var compItem in bucketEntries) {
						int compIndex = scanIndex[compItem];
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

						isDuplicate = TryCheckDuplicate(entry, compItem, flippedGrayBytes, out difference, out flags);

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
					Dictionary<double, byte[]?>? flippedGrayBytes = null;
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
						bool isDuplicate = TryCheckDuplicate(entry, compItem, flippedGrayBytes, out difference, out flags);

						if (isDuplicate)
							MergeDuplicate(entry, compItem, difference, flags);
					}
					IncrementProgress(entry.Path);
				};

				try {
					if (imageEntries.Count >= largeBucketThreshold) {
						Parallel.For(0, imageEntries.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, compareAction);
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
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

					var entry = videoEntries[i];
					float difference = 0;
					DuplicateFlags flags;
					Dictionary<double, byte[]?>? flippedGrayBytes = null;
					double entryDurationSeconds = entry.mediaInfo!.Duration.TotalSeconds;
					double entryToleranceSeconds = GetDurationToleranceSeconds(entryDurationSeconds);

					if (Settings.CompareHorizontallyFlipped)
						flippedGrayBytes = CreateFlippedGrayBytes(entry);

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

						bool isDuplicate = TryCheckDuplicate(entry, compItem, flippedGrayBytes, out difference, out flags);
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
						Parallel.For(0, videoEntries.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, compareAction);
					}
					else {
						for (int i = 0; i < videoEntries.Count; i++)
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

					Parallel.ForEach(smallBuckets, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, bucket => {
						foreach (var entry in bucket.Value) {
							int entryIndex = scanIndex[entry];
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
						Parallel.For(0, bucket.Value.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, i => {
							var entry = bucket.Value[i];
							int entryIndex = scanIndex[entry];
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
			Duplicates = new HashSet<DuplicateItem>(duplicateDict.Values);
			SplitDaisyChainGroups();
		}


		/// <summary>
		/// Phase 2 comparison: find pairs where a shorter video is a partial clip of a longer one,
		/// using audio fingerprint sliding-window matching.  Results are added to Duplicates.
		/// </summary>
		void ScanForPartialDuplicates() {
			Logger.Instance.Info("Partial clip detection: building fingerprint index...");

			// Build a quick lookup for paths already covered by visual duplicate groups.
			var alreadyGrouped = new HashSet<string>(
				Duplicates.Select(d => d.Path),
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			// Collect eligible videos: not an image, has a usable fingerprint, not already grouped.
			var videos = DatabaseUtils.Database
				.Where(e => !e.invalid && !e.IsImage &&
						e.AudioFingerprint != null && e.AudioFingerprint.Length >= 2 &&
						!alreadyGrouped.Contains(e.Path))
				.OrderByDescending(e => e.mediaInfo?.Duration ?? TimeSpan.Zero)
				.ToList();

			if (videos.Count < 2) {
				Logger.Instance.Info("Partial clip detection: fewer than 2 eligible videos, skipping.");
				return;
			}

			Logger.Instance.Info($"Partial clip detection: comparing {videos.Count} video(s) (fingerprint blocks: min={videos.Min(e => e.AudioFingerprint!.Length)}, max={videos.Max(e => e.AudioFingerprint!.Length)})...");

			// sourceGroupId: path of source → Guid of the group it anchors
			var sourceGroupId = new Dictionary<string, Guid>(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
			// assignedClips: paths already assigned as clips (avoid adding same clip twice)
			var assignedClips = new HashSet<string>(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			int pairsChecked = 0;
			int matchesFound = 0;

			for (int i = 0; i < videos.Count - 1; i++) {
				if (cancelationTokenSource.IsCancellationRequested) break;
				FileEntry source = videos[i];
				double sourceSec = (source.mediaInfo?.Duration ?? TimeSpan.Zero).TotalSeconds;
				if (sourceSec < 1.0) continue;

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

					pairsChecked++;
					var (sim, offsetSec) = SlidingWindowCompare(fpClip, fpSource);

					if (sim < (float)Settings.PartialClipSimilarityThreshold) continue;

					// We have a match: source[i] contains clip[j] at offsetSec.
					if (Settings.ExtendedFFToolsLogging)
						Logger.Instance.Info($"[Partial] {System.IO.Path.GetFileName(clip.Path)} in {System.IO.Path.GetFileName(source.Path)}: sim={sim:P1} @ {offsetSec}s (threshold {Settings.PartialClipSimilarityThreshold:P0}, fp {fpClip.Length}/{fpSource.Length} blocks)");
					matchesFound++;

					if (!sourceGroupId.TryGetValue(source.Path, out Guid groupId)) {
						groupId = Guid.NewGuid();
						sourceGroupId[source.Path] = groupId;
						// Add the source as the anchor member (no PartialClip flag)
						Duplicates.Add(new DuplicateItem(source, 0f, groupId, DuplicateFlags.None));
					}

					if (!assignedClips.Contains(clip.Path)) {
						assignedClips.Add(clip.Path);
						var clipItem = new DuplicateItem(clip, 1f - sim, groupId, DuplicateFlags.PartialClip) {
							PartialClipOffset = TimeSpan.FromSeconds(offsetSec)
						};
						Duplicates.Add(clipItem);
					}
					else {
						// Clip already belongs to another source group — update its GroupId to this one
						// so everything lands in the same group (longest source wins as anchor).
						var existing = Duplicates.FirstOrDefault(d => d.Path == clip.Path && d.Flags.HasFlag(DuplicateFlags.PartialClip));
						if (existing != null) existing.GroupId = groupId;
					}
				}
			}

			Logger.Instance.Info($"Partial clip detection: checked {pairsChecked} pair(s), found {matchesFound} match(es).");
		}

		/// <summary>
		/// Slides <paramref name="shorter"/> over <paramref name="longer"/> and returns the
		/// best average Hamming similarity (0–1) and the offset (in seconds / blocks) at which
		/// it occurs.
		/// </summary>
		static (float similarity, int offsetBlocks) SlidingWindowCompare(uint[] shorter, uint[] longer) {
			int lenS = shorter.Length;
			int lenL = longer.Length;
			int maxOffset = lenL - lenS;

			float bestSim = 0f;
			int bestOffset = 0;

			for (int offset = 0; offset <= maxOffset; offset++) {
				int totalBits = 0;
				for (int k = 0; k < lenS; k++)
					totalBits += BitOperations.PopCount(shorter[k] ^ longer[offset + k]);
				// Average Hamming distance per block (max 32 bits each)
				float avgDist = (float)totalBits / (lenS * 32);
				float sim = 1f - avgDist;
				if (sim > bestSim) {
					bestSim = sim;
					bestOffset = offset;
				}
			}

			return (bestSim, bestOffset);
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

				// Resolve FileEntry for each member; skip group if any entry is missing.
				var entries = new FileEntry[n];
				bool allFound = true;
				for (int i = 0; i < n; i++) {
					if (!dbLookup.TryGetValue(members[i].Path, out var fe)) {
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
						bool isSimilar = CheckIfDuplicate(entries[i], null, entries[j], out _);
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
				DatabaseUtils.CleanupDatabase();
			});
			DatabaseCleaned?.Invoke(this, new EventArgs());
		}
		public static void ClearDatabase() => DatabaseUtils.ClearDatabase();
		public static bool ExportDataBaseToJson(string jsonFile, JsonSerializerOptions options) => DatabaseUtils.ExportDatabaseToJson(jsonFile, options);
		public static bool ImportDataBaseFromJson(string jsonFile, JsonSerializerOptions options) => DatabaseUtils.ImportDatabaseFromJson(jsonFile, options);
		public async Task RetrieveThumbnailsForItems(IEnumerable<DuplicateItem> items) {
			var dupList = items.Where(d => d.ImageList == null || d.ImageList.Count == 0).ToList();
			try {
				await Parallel.ForEachAsync(dupList, new ParallelOptions { MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, (entry, cancellationToken) => {
					List<Image>? list = null;
					bool needsThumbnails = !Settings.IncludeNonExistingFiles || File.Exists(entry.Path);
					List<TimeSpan>? timeStamps = null;

					if (needsThumbnails && entry.IsImage) {
						timeStamps = new(0);
						list = new List<Image>(1);
						try {
							Image bitmapImage = Image.Load(entry.Path);
							float resizeFactor = 1f;
							if (bitmapImage.Width > 100 || bitmapImage.Height > 100) {
								float widthFactor = bitmapImage.Width / 100f;
								float heightFactor = bitmapImage.Height / 100f;
								resizeFactor = Math.Max(widthFactor, heightFactor);
							}
							int width = Convert.ToInt32(bitmapImage.Width / resizeFactor);
							int height = Convert.ToInt32(bitmapImage.Height / resizeFactor);
							bitmapImage.Mutate(i => i.Resize(width, height));
							list.Add(bitmapImage);
						}
						catch (Exception ex) {
							Logger.Instance.Info($"Failed loading image from file: '{entry.Path}', reason: {ex.Message}, stacktrace {ex.StackTrace}");
							return ValueTask.CompletedTask;
						}
					}
					else if (needsThumbnails) {
						list = new List<Image>(positionList.Count);
						timeStamps = new List<TimeSpan>(positionList.Count);
						for (int j = 0; j < positionList.Count; j++) {
							var timestamp = TimeSpan.FromSeconds(entry.Duration.TotalSeconds * positionList[j]);
							timeStamps.Add(timestamp);
							var b = FfmpegEngine.GetThumbnail(new FfmpegSettings {
								File = entry.Path,
								Position = timestamp,
								GrayScale = 0,
							}, Settings.ExtendedFFToolsLogging);
							if (b == null || b.Length == 0) return ValueTask.CompletedTask;
							using var byteStream = new MemoryStream(b);
							var bitmapImage = Image.Load(byteStream);
							list.Add(bitmapImage);
						}
					}
					Debug.Assert(timeStamps != null);
					entry.SetThumbnails(list ?? (NoThumbnailImage != null ? new() { NoThumbnailImage } : new()), timeStamps!);

					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
		}
		public async void RetrieveThumbnails() {
			var dupList = Duplicates.Where(d => d.ImageList == null || d.ImageList.Count == 0).ToList();
			int total = dupList.Count;
			int done = 0;
			int lastNotified = 0;

			var sw = Stopwatch.StartNew();
			try {
				await Parallel.ForEachAsync(dupList, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, (entry, cancellationToken) => {
					List<Image>? list = null;
					bool needsThumbnails = !Settings.IncludeNonExistingFiles || File.Exists(entry.Path);
					List<TimeSpan>? timeStamps = null;

					int current = Interlocked.Increment(ref done);
					if (sw.ElapsedMilliseconds > 300)
						if (Interlocked.Exchange(ref lastNotified, current) < current) {
							sw.Restart(); // only this thread resets the stopwatch
							ThumbnailProgress?.Invoke(current, total);
						}

					if (needsThumbnails && entry.IsImage) {
						//For images it doesn't make sense to load the actual image more than once
						timeStamps = new(0);
						list = new List<Image>(1);
						try {
							Image bitmapImage = Image.Load(entry.Path);
							float resizeFactor = 1f;
							if (bitmapImage.Width > 100 || bitmapImage.Height > 100) {
								float widthFactor = bitmapImage.Width / 100f;
								float heightFactor = bitmapImage.Height / 100f;
								resizeFactor = Math.Max(widthFactor, heightFactor);

							}
							int width = Convert.ToInt32(bitmapImage.Width / resizeFactor);
							int height = Convert.ToInt32(bitmapImage.Height / resizeFactor);
							bitmapImage.Mutate(i => i.Resize(width, height));
							list.Add(bitmapImage);
						}
						catch (Exception ex) {
							Logger.Instance.Info($"Failed loading image from file: '{entry.Path}', reason: {ex.Message}, stacktrace {ex.StackTrace}");
							return ValueTask.CompletedTask;
						}

					}
					else if (needsThumbnails) {
						list = new List<Image>(positionList.Count);
						timeStamps = new List<TimeSpan>(positionList.Count);
						for (int j = 0; j < positionList.Count; j++) {
							var timestamp = TimeSpan.FromSeconds(entry.Duration.TotalSeconds * positionList[j]);
							timeStamps.Add(timestamp);
							var b = FfmpegEngine.GetThumbnail(new FfmpegSettings {
								File = entry.Path,
								Position = timestamp,
								GrayScale = 0,
							}, Settings.ExtendedFFToolsLogging);
							if (b == null || b.Length == 0) return ValueTask.CompletedTask;
							using var byteStream = new MemoryStream(b);
							var bitmapImage = Image.Load(byteStream);
							list.Add(bitmapImage);
						}
					}
					Debug.Assert(timeStamps != null);
					entry.SetThumbnails(list ?? (NoThumbnailImage != null ? new() { NoThumbnailImage } : new()), timeStamps!);

					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
			ThumbnailsRetrieved?.Invoke(this, new EventArgs());
		}

		static bool GetGrayBytesFromImage(FileEntry imageFile, bool useExifIfAvailable) {
			try {

				using var byteStream = File.OpenRead(imageFile.Path);
				using var bitmapImage = Image.Load(byteStream);
				//Set some props while we already loaded the image
				imageFile.mediaInfo = new MediaInfo {
					Streams = new[] {
							new MediaInfo.StreamInfo {Height = bitmapImage.Height, Width = bitmapImage.Width}
						}
				};

				// Extract EXIF creation date if enabled
				if (useExifIfAvailable) {
					var exifProfile = bitmapImage.Metadata.ExifProfile;
					if (exifProfile != null) {
						// Try DateTimeOriginal first (when photo was taken)						
						if (exifProfile.TryGetValue(ExifTag.DateTimeOriginal, out var dateTimeOriginal) && !string.IsNullOrWhiteSpace(dateTimeOriginal.Value)) {
							if (TryParseExifDateTime(dateTimeOriginal.Value, out DateTime exifDate)) {
								imageFile.DateCreated = exifDate;
							}
						}
						// Fallback to DateTime if DateTimeOriginal is not available
						else {
							if (exifProfile.TryGetValue(ExifTag.DateTime, out var dateTime) && !string.IsNullOrWhiteSpace(dateTime.Value)) {
								if (TryParseExifDateTime(dateTime.Value, out DateTime exifDate)) {
									imageFile.DateCreated = exifDate;
								}
							}
						}
					}
				}


				int size = DatabaseUtils.DbVersion < 2 ?
								16 :
								GrayBytesUtils.Side;
				bitmapImage.Mutate(a => a.Resize(size, size));

				var d = DatabaseUtils.DbVersion < 2 ?
							GrayBytesUtils.GetGrayScaleValues16x16(bitmapImage) :
							GrayBytesUtils.GetGrayScaleValues(bitmapImage);
				if (d == null) {
					imageFile.Flags.Set(EntryFlags.TooDark);
					Logger.Instance.Info($"ERROR: Graybytes too dark of: {imageFile.Path}");
					return false;
				}

				imageFile.grayBytes.Add(0, d);
				return true;
			}
			catch (Exception ex) {
				Logger.Instance.Info(
					$"Exception, file: {imageFile.Path}, reason: {ex.Message}, stacktrace {ex.StackTrace}");
				imageFile.Flags.Set(EntryFlags.ThumbnailError);
				return false;
			}
		}

		static bool TryParseExifDateTime(string exifDateTime, out DateTime result) {
			// EXIF DateTime format: "YYYY:MM:DD HH:MM:SS"
			result = DateTime.MinValue;

			if (DateTime.TryParseExact(exifDateTime, "yyyy:MM:dd HH:mm:ss",
				CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate)) {
				// Convert to UTC (assuming local time in EXIF)
				result = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
				return true;
			}

			return false;
		}

		void HighlightBestMatches() {
			HashSet<Guid> blackList = new();
			foreach (DuplicateItem item in Duplicates) {
				if (blackList.Contains(item.GroupId)) continue;
				var groupItems = Duplicates.Where(a => a.GroupId == item.GroupId);
				DuplicateItem bestMatch;
				//Duration
				if (!groupItems.First().IsImage) {
					groupItems = groupItems.OrderByDescending(d => d.Duration);
					bestMatch = groupItems.First();
					bestMatch.IsBestDuration = true;
					foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
						if (otherItem.Duration < bestMatch.Duration)
							break;
						otherItem.IsBestDuration = true;
					}
				}
				//Size
				groupItems = groupItems.OrderBy(d => d.SizeLong);
				bestMatch = groupItems.First();
				bestMatch.IsBestSize = true;
				foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
					if (otherItem.SizeLong > bestMatch.SizeLong)
						break;
					otherItem.IsBestSize = true;
				}
				//Fps
				if (!groupItems.First().IsImage) {
					groupItems = groupItems.OrderByDescending(d => d.Fps);
					bestMatch = groupItems.First();
					bestMatch.IsBestFps = true;
					foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
						if (otherItem.Fps < bestMatch.Fps)
							break;
						otherItem.IsBestFps = true;
					}
				}
				//BitRateKbs
				if (!groupItems.First().IsImage) {
					groupItems = groupItems.OrderByDescending(d => d.BitRateKbs);
					bestMatch = groupItems.First();
					bestMatch.IsBestBitRateKbs = true;
					foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
						if (otherItem.BitRateKbs < bestMatch.BitRateKbs)
							break;
						otherItem.IsBestBitRateKbs = true;
					}
				}
				//AudioSampleRate
				if (!groupItems.First().IsImage) {
					groupItems = groupItems.OrderByDescending(d => d.AudioSampleRate);
					bestMatch = groupItems.First();
					bestMatch.IsBestAudioSampleRate = true;
					foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
						if (otherItem.AudioSampleRate < bestMatch.AudioSampleRate)
							break;
						otherItem.IsBestAudioSampleRate = true;
					}
				}
				//AudioBitRateKbs
				if (!groupItems.First().IsImage) {
					groupItems = groupItems.OrderByDescending(d => d.AudioBitRateKbs);
					bestMatch = groupItems.First();
					bestMatch.IsBestAudioBitRateKbs = true;
					foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
						if (otherItem.AudioBitRateKbs < bestMatch.AudioBitRateKbs)
							break;
						otherItem.IsBestAudioBitRateKbs = true;
					}
				}
				//HdrFormat
				if (!groupItems.First().IsImage) {
					groupItems = groupItems.OrderByDescending(d => d.HdrFormatRank);
					bestMatch = groupItems.First();
					bestMatch.IsBestHdrFormat = true;
					foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
						if (otherItem.HdrFormatRank < bestMatch.HdrFormatRank)
							break;
						otherItem.IsBestHdrFormat = true;
					}
				}
			//FrameSizeInt
				groupItems = groupItems.OrderByDescending(d => d.FrameSizeInt);
				bestMatch = groupItems.First();
				bestMatch.IsBestFrameSize = true;
					foreach (DuplicateItem otherItem in groupItems.Skip(1)) {
					if (otherItem.FrameSizeInt < bestMatch.FrameSizeInt)
						break;
					otherItem.IsBestFrameSize = true;
				}
				blackList.Add(item.GroupId);
			}
		}

		public void Pause() {
			if (!isScanning || pauseTokenSource.IsPaused) return;
			Logger.Instance.Info("Scan paused by user");
			ElapsedTimer.Stop();
			SearchTimer.Stop();
			pauseTokenSource.IsPaused = true;

		}

		public void Resume() {
			if (!isScanning || pauseTokenSource.IsPaused != true) return;
			Logger.Instance.Info("Scan resumed by user");
			ElapsedTimer.Start();
			SearchTimer.Start();
			pauseTokenSource.IsPaused = false;
		}

		public void Stop() {
			if (pauseTokenSource.IsPaused)
				Resume();
			Logger.Instance.Info("Scan stopped by user");
			if (isScanning)
				cancelationTokenSource.Cancel();
		}
	}
}
