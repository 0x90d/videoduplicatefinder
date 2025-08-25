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
global using System.Collections.Generic;
global using System.IO;
global using System.Threading;
global using System.Threading.Tasks;
global using SixLabors.ImageSharp;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
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


		void InitProgress(int count) {
			startTime = DateTime.UtcNow;
			scanProgressMaxValue = count;
			processedFiles = 0;
			lastProgressUpdate = DateTime.MinValue;
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
			Logger.Instance.Info("Building file list...");
			await BuildFileList();
			Logger.Instance.Info($"Finished building file list in {SearchTimer.StopGetElapsedAndRestart()}");
			FilesEnumerated?.Invoke(this, new EventArgs());
			Logger.Instance.Info("Gathering media info and buildings hashes...");
			if (!cancelationTokenSource.IsCancellationRequested)
				await GatherInfos();
			Logger.Instance.Info($"Finished gathering and hashing in {SearchTimer.StopGetElapsedAndRestart()}");
			BuildingHashesDone?.Invoke(this, new EventArgs());
			DatabaseUtils.SaveDatabase();
			if (!cancelationTokenSource.IsCancellationRequested) {
				StartCompare();
			}
			else {
				ScanAborted?.Invoke(this, new EventArgs());
				Logger.Instance.Info("Scan aborted.");
				isScanning = false;
			}
		}

		public async void StartCompare() {
			PrepareCompare();
			SearchTimer.Start();
			ElapsedTimer.Start();
			Logger.Instance.Info("Scan for duplicates...");
			if (!cancelationTokenSource.IsCancellationRequested)
				await Task.Run(ScanForDuplicates, cancelationTokenSource.Token);
			SearchTimer.Stop();
			ElapsedTimer.Stop();
			Logger.Instance.Info($"Finished scanning for duplicates in {SearchTimer.Elapsed}");
			Logger.Instance.Info("Highlighting best results...");
			HighlightBestMatches();
			ScanDone?.Invoke(this, new EventArgs());
			Logger.Instance.Info("Scan done.");
			DatabaseUtils.SaveDatabase();
			isScanning = false;
		}

		void PrepareSearch() {
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

		Task BuildFileList() => Task.Run(() => {

			DatabaseUtils.LoadDatabase();
			if (DatabaseUtils.DbVersion < 2)
				Settings.UsePHashing = false;

			int oldFileCount = DatabaseUtils.Database.Count;

			foreach (string path in Settings.IncludeList) {
				if (!Directory.Exists(path)) continue;

				foreach (FileInfo file in FileUtils.GetFilesRecursive(path, Settings.IgnoreReadOnlyFolders, Settings.IgnoreReparsePoints,
					Settings.IncludeSubDirectories, Settings.IncludeImages, Settings.BlackList.ToList())) {
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
		bool InvalidEntry(FileEntry entry, out bool reportProgress) {
			reportProgress = true;

			if (Settings.IncludeImages == false && entry.IsImage)
				return true;
			if (Settings.BlackList.Any(f => {
				if (!entry.Folder.StartsWith(f))
					return false;
				if (entry.Folder.Length == f.Length)
					return true;
				//Reason: https://github.com/0x90d/videoduplicatefinder/issues/249
				string relativePath = Path.GetRelativePath(f, entry.Folder);
				return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
			}))
				return true;

			if (!Settings.ScanAgainstEntireDatabase) {
				/* Skip non-included file before checking if it exists
				 * This greatly improves performance if the file is on
				 * a disconnected network/mobile drive
				 */
				if (Settings.IncludeSubDirectories == false) {
					if (!Settings.IncludeList.Contains(entry.Folder)) {
						reportProgress = false;
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
					return true;
				}
			}

			if (entry.Flags.Any(EntryFlags.ManuallyExcluded | EntryFlags.TooDark))
				return true;
			if (!Settings.IncludeNonExistingFiles && !File.Exists(entry.Path))
				return true;

			if (Settings.FilterByFileSize && (entry.FileSize.BytesToMegaBytes() > Settings.MaximumFileSize ||
				entry.FileSize.BytesToMegaBytes() < Settings.MinimumFileSize)) {
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
				if (!contains)
					return true;
			}

			if (Settings.IgnoreReparsePoints && File.Exists(entry.Path) && File.ResolveLinkTarget(entry.Path, returnFinalTarget: false) != null)
				return true;
			if (Settings.FilterByFilePathNotContains) {
				bool contains = false;
				foreach (var f in Settings.FilePathNotContainsTexts) {
					if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(f, entry.Path)) {
						contains = true;
						break;
					}
				}
				if (contains)
					return true;
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

		async Task GatherInfos() {
			try {
				InitProgress(DatabaseUtils.Database.Count);
				await Parallel.ForEachAsync(DatabaseUtils.Database, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, (entry, token) => {
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

					entry.invalid = InvalidEntry(entry, out bool reportProgress);

					bool skipEntry = false;
					skipEntry |= entry.invalid;
					skipEntry |= entry.Flags.Has(EntryFlags.ThumbnailError) && !Settings.AlwaysRetryFailedSampling;

					if (!skipEntry && !Settings.ScanAgainstEntireDatabase) {
						if (Settings.IncludeSubDirectories == false) {
							if (!Settings.IncludeList.Contains(entry.Folder))
								skipEntry = true;
						}
						else if (!Settings.IncludeList.Any(f => {
							if (!entry.Folder.StartsWith(f))
								return false;
							if (entry.Folder.Length == f.Length)
								return true;
							//Reason: https://github.com/0x90d/videoduplicatefinder/issues/249
							string relativePath = Path.GetRelativePath(f, entry.Folder);
							return !relativePath.StartsWith('.') && !Path.IsPathRooted(relativePath);
						}))
							skipEntry = true;
					}

					if (skipEntry) {
						entry.invalid = true;
						if (reportProgress)
							IncrementProgress(entry.Path);
						return ValueTask.CompletedTask;
					}
					if (Settings.IncludeNonExistingFiles && entry.grayBytes?.Count > 0) {
						bool hasAllInformation = entry.IsImage;
						if (!hasAllInformation) {
							hasAllInformation = true;
							for (int i = 0; i < positionList.Count; i++) {
								if (entry.grayBytes.ContainsKey(entry.GetGrayBytesIndex(positionList[i])))
									continue;
								hasAllInformation = false;
								break;
							}
						}
						if (hasAllInformation) {
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
						if (!GetGrayBytesFromImage(entry))
							entry.invalid = true;
					}
					else if (!entry.IsImage) {
						if (!FfmpegEngine.GetGrayBytesFromVideo(entry, positionList, Settings.ExtendedFFToolsLogging))
							entry.invalid = true;
					}

					IncrementProgress(entry.Path);
					return ValueTask.CompletedTask;
				});
			}
			catch (OperationCanceledException) { }
		}

		Dictionary<double, byte[]?> CreateFlippedGrayBytes(FileEntry entry) {
			Dictionary<double, byte[]?>? flippedGrayBytes = new();
			if (entry.IsImage)
				flippedGrayBytes.Add(0, DatabaseUtils.DbVersion < 2 ? GrayBytesUtils.FlipGrayScale16x16(entry.grayBytes[0]!) : GrayBytesUtils.FlipGrayScale(entry.grayBytes[0]!));
			else {
				for (int j = 0; j < positionList.Count; j++) {
					double idx = entry.GetGrayBytesIndex(positionList[j]);
					flippedGrayBytes.Add(idx, DatabaseUtils.DbVersion < 2 ? GrayBytesUtils.FlipGrayScale16x16(entry.grayBytes[idx]!) : GrayBytesUtils.FlipGrayScale(entry.grayBytes[idx]!));
				}
			}
			return flippedGrayBytes;
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

				if (!entry.PHashes.TryGetValue(entry.GetGrayBytesIndex(positionList[0]), out ulong? phash))
					phash = pHash.PerceptualHash.ComputePHashFromGray32x32(grayBytes[positionList[0]]);
				if (!compItem.PHashes.TryGetValue(compItem.GetGrayBytesIndex(positionList[0]), out ulong? phash_comp))
					phash_comp = pHash.PerceptualHash.ComputePHashFromGray32x32(compItem.grayBytes[positionList[0]]);
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
								grayBytes[entry.GetGrayBytesIndex(positionList[j])]!,
								compItem.grayBytes[compItem.GetGrayBytesIndex(positionList[j])]!, ignoreBlackPixels, ignoreWhitePixels) :
							GrayBytesUtils.PercentageDifference(
								grayBytes[entry.GetGrayBytesIndex(positionList[j])]!,
								compItem.grayBytes[compItem.GetGrayBytesIndex(positionList[j])]!);
				if (diffSum > differenceLimit) // already exceeding maximum tolerated diff -> exit early
					return false;
			}
			difference = diffSum / positionList.Count;
			return !float.IsNaN(difference);
		}

		void ScanForDuplicates() {
			Dictionary<string, DuplicateItem>? duplicateDict = new();

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

			double maxPercentDurationDifference = 100d + Settings.PercentDurationDifference;
			double minPercentDurationDifference = 100d - Settings.PercentDurationDifference;

			try {
				Parallel.For(0, ScanList.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, i => {
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

					FileEntry? entry = ScanList[i];
					float difference = 0;
					DuplicateFlags flags = DuplicateFlags.None;
					bool isDuplicate;
					Dictionary<double, byte[]?>? flippedGrayBytes = null;

					if (Settings.CompareHorizontallyFlipped)
						flippedGrayBytes = CreateFlippedGrayBytes(entry);

					for (int n = i + 1; n < ScanList.Count; n++) {
						FileEntry? compItem = ScanList[n];
						if (entry.IsImage != compItem.IsImage)
							continue;
						if (!entry.IsImage) {
							double p = entry.mediaInfo!.Duration.TotalSeconds / compItem.mediaInfo!.Duration.TotalSeconds * 100d;
							if (p > maxPercentDurationDifference ||
								p < minPercentDurationDifference)
								continue;
						}


						flags = DuplicateFlags.None;
						isDuplicate = CheckIfDuplicate(entry, null, compItem, out difference);
						if (Settings.CompareHorizontallyFlipped &&
							CheckIfDuplicate(entry, flippedGrayBytes, compItem, out float flippedDifference)) {
							if (!isDuplicate || flippedDifference < difference) {
								flags |= DuplicateFlags.Flipped;
								isDuplicate = true;
								difference = flippedDifference;
							}
						}

						if (isDuplicate &&
							entry.FileSize == compItem.FileSize &&
							entry.mediaInfo!.Duration == compItem.mediaInfo!.Duration &&
							Settings.ExcludeHardLinks) {
							foreach (var link in HardLinkUtils.GetHardLinks(entry.Path))
								if (compItem.Path == link) {
									isDuplicate = false;
									break;
								}
						}

						if (isDuplicate) {
							lock (duplicateDict) {
								bool foundBase = duplicateDict.TryGetValue(entry.Path, out DuplicateItem? existingBase);
								bool foundComp = duplicateDict.TryGetValue(compItem.Path, out DuplicateItem? existingComp);

								if (foundBase && foundComp) {
									//this happens with 4+ identical items:
									//first, 2+ duplicate groups are found independently, they are merged in this branch
									if (existingBase!.GroupId != existingComp!.GroupId) {
										Guid groupID = existingComp!.GroupId;
										foreach (DuplicateItem? dup in duplicateDict.Values.Where(c =>
											c.GroupId == groupID))
											dup.GroupId = existingBase.GroupId;
									}
								}
								else if (foundBase) {
									duplicateDict.TryAdd(compItem.Path,
										new DuplicateItem(compItem, difference, existingBase!.GroupId, flags));
								}
								else if (foundComp) {
									duplicateDict.TryAdd(entry.Path,
										new DuplicateItem(entry, difference, existingComp!.GroupId, flags));
								}
								else {
									var groupId = Guid.NewGuid();
									duplicateDict.TryAdd(compItem.Path, new DuplicateItem(compItem, difference, groupId, flags));
									duplicateDict.TryAdd(entry.Path, new DuplicateItem(entry, difference, groupId, DuplicateFlags.None));
								}
							}
						}
					}
					IncrementProgress(entry.Path);
				});
			}
			catch (OperationCanceledException) { }
			Duplicates = new HashSet<DuplicateItem>(duplicateDict.Values);
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
		public async void RetrieveThumbnails() {
			var dupList = Duplicates.Where(d => d.ImageList == null || d.ImageList.Count == 0).ToList();
			try {
				await Parallel.ForEachAsync(dupList, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, (entry, cancellationToken) => {
					List<Image>? list = null;
					bool needsThumbnails = !Settings.IncludeNonExistingFiles || File.Exists(entry.Path);
					List<TimeSpan>? timeStamps = null;
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

		static bool GetGrayBytesFromImage(FileEntry imageFile) {
			try {

				using var byteStream = File.OpenRead(imageFile.Path);
				using var bitmapImage = Image.Load(byteStream);
				//Set some props while we already loaded the image
				imageFile.mediaInfo = new MediaInfo {
					Streams = new[] {
							new MediaInfo.StreamInfo {Height = bitmapImage.Height, Width = bitmapImage.Width}
						}
				};
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

