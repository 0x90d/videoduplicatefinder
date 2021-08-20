// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using System.Runtime.CompilerServices;

namespace VDF.Core {
	public sealed class ScanEngine {
		public HashSet<DuplicateItem> Duplicates { get; set; } = new HashSet<DuplicateItem>();
		public Settings Settings { get; } = new Settings();
		public event EventHandler<ScanProgressChangedEventArgs>? Progress;
		public event EventHandler? BuildingHashesDone;
		public event EventHandler? ScanDone;
		public event EventHandler? ThumbnailsRetrieved;
		public event EventHandler? FilesEnumerated;
		public event EventHandler? DatabaseCleaned;


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
		static readonly int grayScaleWidth = 16;	// Default: 16, GrayBytesUtils performance may decrease at other values 
		static readonly int thumbnailWidth = 100;	// Default: 100, UI display errors may occur at other values 

		void InitProgress(int count) {
			startTime = DateTime.Now;
			scanProgressMaxValue = count;
			processedFiles = 0;
			lastProgressUpdate = DateTime.MinValue;
		}
		void IncrementProgress(string path) {
			processedFiles++;
			var pushUpdate = processedFiles == scanProgressMaxValue ||
								lastProgressUpdate + progressUpdateIntervall < DateTime.Now;
			if (!pushUpdate) return;
			lastProgressUpdate = DateTime.Now;
			var timeRemaining = TimeSpan.FromTicks(DateTime.Now.Subtract(startTime).Ticks *
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

		public async void StartSearch() {
			Prepare();
			SearchTimer.Start();
			ElapsedTimer.Start();
			Logger.Instance.Info("Building file list...");
			await BuildFileList();
			Logger.Instance.Info($"Finished building file list in {SearchTimer.StopGetElapsedAndRestart()}");
			FilesEnumerated?.Invoke(this, new EventArgs());
			Logger.Instance.Info("Gathering media info and buildings hashes...");
			if (!cancelationTokenSource.IsCancellationRequested)
				await Task.Run(GatherInfos, cancelationTokenSource.Token);
			Logger.Instance.Info($"Finished gathering and hashing in {SearchTimer.StopGetElapsedAndRestart()}");
			BuildingHashesDone?.Invoke(this, new EventArgs());
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

		void Prepare() {
			//Using VDF.GUI we know fftools exist at this point but VDF.Core might be used in other projects as well
			if (!FFmpegExists)
				throw new FFNotFoundException("Cannot find FFmpeg");
			if (!FFprobeExists)
				throw new FFNotFoundException("Cannot find FFprobe");

			FfmpegEngine.UseCuda = Settings.UseCuda;
			Duplicates.Clear();
			positionList.Clear();
			ElapsedTimer.Reset();
			SearchTimer.Reset();
			pauseTokenSource = new PauseTokenSource();
			cancelationTokenSource = new CancellationTokenSource();
			float positionCounter = 0f;
			for (var i = 0; i < Settings.ThumbnailCount; i++) {
				positionCounter += 1.0F / (Settings.ThumbnailCount + 1);
				positionList.Add(positionCounter);
			}
			isScanning = true;
		}

		Task BuildFileList() => Task.Run(() => {
						
			DatabaseUtils.LoadDatabase();
			int oldFileCount = DatabaseUtils.Database.Count;

			foreach (var path in Settings.IncludeList) {
				if (!Directory.Exists(path)) continue;

				foreach (var file in FileUtils.GetFilesRecursive(path, Settings.IgnoreReadOnlyFolders, Settings.IgnoreHardlinks,
					Settings.IncludeSubDirectories, Settings.IncludeImages, Settings.BlackList.ToList())) {
					var fEntry = new FileEntry(file);
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

			Logger.Instance.Info($"Files in database: {DatabaseUtils.Database.Count:N0} ({DatabaseUtils.Database.Count-oldFileCount:N0} files added)");
		});

		bool InvalidEntry(FileEntry entry) {
			if (Settings.IncludeImages == false && entry.IsImage)
				return true;
			if (Settings.IncludeSubDirectories == false) {
				if (!Settings.IncludeList.Contains(entry.Folder))
					return true;
			}
			else if (!Settings.IncludeList.Any(f => entry.Folder.StartsWith(f)))
				return true;
			if (Settings.BlackList.Any(s => entry.Folder.StartsWith(s)))
				return true;

			if (entry.Flags.Any(EntryFlags.ManuallyExcluded | EntryFlags.TooDark))
				return true;
			if (!File.Exists(entry.Path))
				return true;
			return false;
		}
		bool InvalidEntryForDuplicateCheck(FileEntry entry) =>
			InvalidEntry(entry) || entry.mediaInfo == null || entry.Flags.Has(EntryFlags.ThumbnailError) || (!entry.IsImage && entry.grayBytes.Count < Settings.ThumbnailCount);

		public static Task<bool> LoadDatabase() => Task.Run(DatabaseUtils.LoadDatabase);
		public static void SaveDatabase() => DatabaseUtils.SaveDatabase();

		public static void BlackListFileEntry(string filePath) => DatabaseUtils.BlacklistFileEntry(filePath);

		void GatherInfos() {
			try {
				InitProgress(DatabaseUtils.Database.Count);
				Parallel.ForEach(DatabaseUtils.Database, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, entry => {
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

					if (InvalidEntry(entry)) return;

					if (entry.mediaInfo == null && !entry.IsImage) {
						MediaInfo? info = FFProbeEngine.GetMediaInfo(entry.Path);
						if (info == null) {
							Logger.Instance.Info($"ERROR: Failed to retrieve media info from: {entry.Path}");
							entry.Flags.Set(EntryFlags.MetadataError);
							return;
						}

						entry.mediaInfo = info;
					}
					// 08/17/21: This is for people upgrading from an older VDF version
					if (entry.grayBytes == null)
						entry.grayBytes = new Dictionary<double, byte[]?>();

					
					if (entry.IsImage && entry.grayBytes.Count == 0)
						GetGrayBytesFromImage(entry, grayScaleWidth);
					else if (!entry.IsImage)
						FfmpegEngine.GetGrayBytesFromVideo(entry, positionList, grayScaleWidth);

					IncrementProgress(entry.Path);
				});
			}
			catch (OperationCanceledException) { }
		}

		Dictionary<double, byte[]?> createFlippedGrayBytes(FileEntry entry)
		{
			var flippedGrayBytes = new Dictionary<double, byte[]?>();
			if (entry.IsImage)
				flippedGrayBytes.Add(0, GrayBytesUtils.FlipGrayScale(entry.grayBytes[0]!, grayScaleWidth));
			else
				for (int j = 0; j < positionList.Count; j++)
				{
					double idx = entry.GetGrayBytesIndex(positionList[j]);
					flippedGrayBytes.Add(idx, GrayBytesUtils.FlipGrayScale(entry.grayBytes[idx]!, grayScaleWidth));
				}

			return flippedGrayBytes;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		bool isDuplicate(FileEntry entry1, Dictionary<double, byte[]?>? grayBytes1, FileEntry entry2, float differenceLimit, out float difference)
		{
			difference = float.PositiveInfinity;
			grayBytes1 ??= entry1.grayBytes;

			if (entry1.IsImage && !entry2.IsImage) 
				return false; 

			if (entry1.IsImage)
			{
				difference = GrayBytesUtils.PercentageDifference(grayBytes1[0]!, entry2.grayBytes[0]!);
				return difference < differenceLimit;
			}
		
			float diff, diffSum = 0;
			for (int j = 0; j < positionList.Count; j++) {
				diff = GrayBytesUtils.PercentageDifference(
					grayBytes1[entry1.GetGrayBytesIndex(positionList[j])]!, 
					entry2.grayBytes[entry2.GetGrayBytesIndex(positionList[j])]!
				);
				if (diff >= differenceLimit)
					return false;
				diffSum += diff;
			}
			difference = diffSum / positionList.Count; 
			return true;
		}

		void ScanForDuplicates() {
			var differenceLimit = 1.0f - Settings.Percent / 100f;
			var duplicateDict = new Dictionary<string, DuplicateItem>();

			//Exclude existing database entries which not met current scan settings
			List<FileEntry> ScanList = new List<FileEntry>(DatabaseUtils.Database);
			ScanList.RemoveAll(InvalidEntryForDuplicateCheck);

			Logger.Instance.Info($"Scanning for duplicates in {ScanList.Count:N0} files");

			InitProgress(ScanList.Count);

			try {
				Parallel.For(0, ScanList.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, i => {
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

					var entry = ScanList[i];
					Dictionary<double, byte[]?>? flippedGrayBytes = null;
					if (Settings.CompareHorizontallyFlipped)
						flippedGrayBytes = createFlippedGrayBytes(entry);

					for (var n = i + 1; n < ScanList.Count; n++) {
						FileEntry compItem = ScanList[n];
						DuplicateFlags flags = DuplicateFlags.None; 
						bool dupplicate = isDuplicate(entry, null, compItem, differenceLimit, out var difference);
						if (!dupplicate && Settings.CompareHorizontallyFlipped)
						{
							dupplicate = isDuplicate(entry, flippedGrayBytes, compItem, differenceLimit, out difference);
							if (dupplicate)
								flags |= DuplicateFlags.Flipped; 
						}

						if (!dupplicate) {
							IncrementProgress(entry.Path);
							continue;
						}

						lock (duplicateDict) {
							var foundBase = duplicateDict.TryGetValue(entry.Path, out var existingBase);
							var foundComp = duplicateDict.TryGetValue(compItem.Path, out var existingComp);

							if (foundBase && foundComp) {
								//this happens with 4+ identical items:
								//first, 2+ duplicate groups are found independently, they are merged in this branch
								if (existingBase!.GroupId != existingComp!.GroupId) {
									foreach (var dup in duplicateDict.Values.Where(c =>
										c.GroupId == existingComp.GroupId))
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
						IncrementProgress(entry.Path);
					}
				});
			}
			catch (OperationCanceledException) { }
			Duplicates = new HashSet<DuplicateItem>(duplicateDict.Values);
		}
		public void CleanupDatabase() {
			DatabaseUtils.CleanupDatabase();
			DatabaseCleaned?.Invoke(this, new EventArgs());
		}
		public static bool ExportDataBaseToJson(string jsonFile, JsonSerializerOptions options) => DatabaseUtils.ExportDatabaseToJson(jsonFile, options);
		public async void RetrieveThumbnails() {
			await Task.Run(() => {
				var dupList = Duplicates.Where(d => d.ImageList == null || d.ImageList.Count == 0).ToList();
				try {
					Parallel.For(0, dupList.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token, MaxDegreeOfParallelism = Settings.MaxDegreeOfParallelism }, i => {
						var entry = dupList[i];
						List<Image> list;
						if (entry.IsImage) {
							//For images it doesn't make sense to load the actual image more than once
							list = new List<Image>(1);
							try {
								Image bitmapImage = Image.FromFile(entry.Path);
								float resizeFactor = 1f;
								if (bitmapImage.Width > thumbnailWidth || bitmapImage.Height > thumbnailWidth) {
									float widthFactor = bitmapImage.Width / (float)thumbnailWidth;
									float heightFactor = bitmapImage.Height / (float)thumbnailWidth;
									resizeFactor = Math.Max(widthFactor, heightFactor);

								}
								int width = Convert.ToInt32(bitmapImage.Width / resizeFactor);
								int height = Convert.ToInt32(bitmapImage.Height / resizeFactor);
								var newImage = new Bitmap(width, height);
								using (var g = Graphics.FromImage(newImage)) {
									g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
									g.DrawImage(bitmapImage, 0, 0, newImage.Width, newImage.Height);
								}

								bitmapImage.Dispose();
								list.Add(newImage);
							}
							catch (Exception ex) {
								Logger.Instance.Info($"Failed loading image from file: '{entry.Path}', reason: {ex.Message}, stacktrace {ex.StackTrace}");
								return;
							}

						}
						else {
							list = new List<Image>(positionList.Count);
							for (int j = 0; j < positionList.Count; j++) {
								var b = FfmpegEngine.GetThumbnail(new FfmpegSettings {
									File = entry.Path,
									Position = TimeSpan.FromSeconds(entry.Duration.TotalSeconds * positionList[j]),
									GrayScale = 0,
									Width = thumbnailWidth,
								});
								if (b == null || b.Length == 0) return;
								using var byteStream = new MemoryStream(b);
								var bitmapImage = Image.FromStream(byteStream);
								list.Add(bitmapImage);
							}
						}
						entry.SetThumbnails(list);

					});
				}
				catch (OperationCanceledException) { }
			});
			ThumbnailsRetrieved?.Invoke(this, new EventArgs());
		}

		static void GetGrayBytesFromImage(FileEntry imageFile, int width) {
			try {

				using var byteStream = File.OpenRead(imageFile.Path);
				using var bitmapImage = Image.FromStream(byteStream);
				//Set some props while we already loaded the image
				imageFile.mediaInfo = new MediaInfo {
					Streams = new[] {
							new MediaInfo.StreamInfo {Height = bitmapImage.Height, Width = bitmapImage.Width}
						}
				};
				var b = new Bitmap(width, width);
				using (var g = Graphics.FromImage(b)) {
					g.DrawImage(bitmapImage, 0, 0, width, width);
				}

				var d = GrayBytesUtils.GetGrayScaleValues(b, width);
				if (d == null) {
					imageFile.Flags.Set(EntryFlags.TooDark);
					Logger.Instance.Info($"ERROR: Graybytes too dark of: {imageFile.Path}");
					return;
				}

				imageFile.grayBytes.Add(0, d);
			}
			catch (Exception ex) {
				Logger.Instance.Info(
					$"Exception, file: {imageFile.Path}, reason: {ex.Message}, stacktrace {ex.StackTrace}");
				imageFile.Flags.Set(EntryFlags.ThumbnailError);
			}
		}

		void HighlightBestMatches() {
			HashSet<Guid> blackList = new();
			foreach (DuplicateItem item in Duplicates) {
				if (blackList.Contains(item.GroupId)) continue;
				var groupItems = Duplicates.Where(a => a.GroupId == item.GroupId);
				groupItems.OrderByDescending(d => d.Duration).First().IsBestDuration = true;
				groupItems.OrderBy(d => d.SizeLong).First().IsBestSize = true;
				groupItems.OrderByDescending(d => d.Duration).First().IsBestFps = true;
				groupItems.OrderByDescending(d => d.BitRateKbs).First().IsBestBitRateKbs = true;
				groupItems.OrderByDescending(d => d.AudioSampleRate).First().IsBestAudioSampleRate = true;
				groupItems.OrderByDescending(d => d.FrameSizeInt).First().IsBestFrameSize = true;
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

