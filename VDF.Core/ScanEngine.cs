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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
		public event EventHandler? ThumbnailsRetrieved;
		public event EventHandler? FilesEnumerated;
		public event EventHandler? DatabaseCleaned;


		PauseTokenSource pauseTokenSource = new PauseTokenSource();
		CancellationTokenSource cancelationTokenSource = new CancellationTokenSource();
		readonly List<float> positionList = new List<float>();

		bool isScanning;
		int scanProgressMaxValue;
		readonly Stopwatch SearchTimer = new Stopwatch();
		public Stopwatch ElapsedTimer = new Stopwatch();
		int processedFiles;
		DateTime startTime = DateTime.Now;
		DateTime lastProgressUpdate = DateTime.MinValue;
		static readonly TimeSpan progressUpdateIntervall = TimeSpan.FromMilliseconds(300);


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

		public bool FFmpegExists => !string.IsNullOrEmpty(FfmpegEngine.FFmpegPath);
		public bool FFprobeExists => !string.IsNullOrEmpty(FFProbeEngine.FFprobePath);

		public async void StartSearch() {
			Prepare();
			SearchTimer.Start();
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
			foreach (var path in Settings.IncludeList) {
				foreach (var file in FileUtils.GetFilesRecursive(path, Settings.IgnoreReadOnlyFolders, Settings.IgnoreHardlinks,
					Settings.IncludeSubDirectories, Settings.IncludeImages, Settings.BlackList.ToList())) {
					var fEntry = new FileEntry(file);
					if (!DatabaseUtils.Database.Contains(fEntry))
						DatabaseUtils.Database.Add(fEntry);
				}
			}

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

			if (entry.Flags.Any(EntryFlags.ManuallyExcluded | EntryFlags.AllErrors))
				return true;
			return false;
		}
		bool InvalidEntryForDuplicateCheck(FileEntry entry) =>
			InvalidEntry(entry) || entry.mediaInfo == null || (!entry.IsImage && entry.grayBytes?.Count < Settings.ThumbnailCount);

		public Task LoadDatabase() => Task.Run(DatabaseUtils.LoadDatabase);
		public void SaveDatabase() => DatabaseUtils.SaveDatabase();

		public void BlackListFileEntry(string filePath) => DatabaseUtils.BlacklistFileEntry(filePath);

		void GatherInfos() {
			try {
				InitProgress(DatabaseUtils.Database.Count);
				Parallel.ForEach(DatabaseUtils.Database, new ParallelOptions { CancellationToken = cancelationTokenSource.Token }, entry => {
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

					if (InvalidEntry(entry)) return;

					if (entry.mediaInfo == null && !entry.IsImage) {
						var info = FFProbeEngine.GetMediaInfo(entry.Path);
						if (info == null) {
							entry.Flags.Set(EntryFlags.MetadataError);
							return;
						}

						entry.mediaInfo = info;
					}

					if (entry.grayBytes == null || (entry.IsImage ? entry.grayBytes.Count == 0 : entry.grayBytes.Count < Settings.ThumbnailCount)) {
						List<byte[]> grayBytes;
						var error = entry.IsImage
							? GetGrayBytesFromImage(entry, out grayBytes)
							: FfmpegEngine.GetGrayBytesFromVideo(entry, positionList, out grayBytes);
						if (error > 0)
							entry.Flags.Set(error);
						else
							entry.grayBytes = grayBytes;
					}

					IncrementProgress(entry.Path);
				});
			}
			catch (OperationCanceledException) { }
		}

		void ScanForDuplicates() {

			var percentageDifference = 1.0f - Settings.Percent / 100f;
			var duplicateDict = new Dictionary<string, DuplicateItem>();


			//Exclude existing database entries which not met current scan settings
			List<FileEntry> ScanList = new List<FileEntry>(DatabaseUtils.Database);
			ScanList.RemoveAll(InvalidEntryForDuplicateCheck);

			InitProgress(ScanList.Count);

			try {
				Parallel.For(0, ScanList.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token }, i => {
					while (pauseTokenSource.IsPaused) Thread.Sleep(50);

					var entry = ScanList[i];
					for (var n = i + 1; n < ScanList.Count; n++) {
						var compItem = ScanList[n];
						if (entry.IsImage && !compItem.IsImage) continue;
						var duplicateCounter = 0;
						float[] percent;
						if (entry.IsImage) {
							percent = new float[1];
							percent[0] = GrayBytesUtils.PercentageDifference(entry.grayBytes![0], compItem.grayBytes![0]);
							if (percent[0] < percentageDifference)
								duplicateCounter++;
						}
						else {
							percent = new float[entry.grayBytes!.Count];
							for (var j = 0; j < entry.grayBytes.Count; j++) {
								percent[j] =
									GrayBytesUtils.PercentageDifference(entry.grayBytes[j], compItem.grayBytes![j]);
								if (percent[j] < percentageDifference) {
									duplicateCounter++;
								}
								else { break; }
							}
						}


						if (entry.IsImage && duplicateCounter == 0 || !entry.IsImage && duplicateCounter != entry.grayBytes.Count) {
							IncrementProgress(entry.Path);
							continue;
						}

						lock (duplicateDict) {
							var percSame = percent.Average();
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
									new DuplicateItem(compItem, percSame, existingBase!.GroupId));
							}
							else if (foundComp) {
								duplicateDict.TryAdd(entry.Path,
									new DuplicateItem(entry, percSame, existingComp!.GroupId));
							}
							else {
								var groupId = Guid.NewGuid();
								duplicateDict.TryAdd(compItem.Path, new DuplicateItem(compItem, percSame, groupId));
								duplicateDict.TryAdd(entry.Path, new DuplicateItem(entry, percSame, groupId));
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
		public async void RetrieveThumbnails() {
			await Task.Run(() => {
				var dupList = Duplicates.Where(d => d.ImageList == null || d.ImageList.Count == 0).ToList();
				try {
					Parallel.For(0, dupList.Count, new ParallelOptions { CancellationToken = cancelationTokenSource.Token }, i => {
						var entry = dupList[i];
						List<Image> list;
						if (entry.IsImage) {
							//For images it doesn't make sense to load the actual image more than once
							list = new List<Image>(1);
							try {
								Image bitmapImage = Image.FromFile(entry.Path);
								float resizeFactor = 1f;
								if (bitmapImage.Width > 100 || bitmapImage.Height > 100) {
									float widthFactor = bitmapImage.Width / 100f;
									float heightFactor = bitmapImage.Height / 100f;
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

		static EntryFlags GetGrayBytesFromImage(FileEntry videoFile, out List<byte[]> grayBytes) {
			grayBytes = new List<byte[]>();
			try {
				using var byteStream = File.OpenRead(videoFile.Path);
				using var bitmapImage = Image.FromStream(byteStream);
				//Set some props while we already loaded the image
				videoFile.mediaInfo = new MediaInfo {
					Streams = new[] {
							new MediaInfo.StreamInfo {Height = bitmapImage.Height, Width = bitmapImage.Width}
						}
				};
				var b = new Bitmap(16, 16);
				using (var g = Graphics.FromImage(b)) {
					g.DrawImage(bitmapImage, 0, 0, 16, 16);
				}

				var d = GrayBytesUtils.GetGrayScaleValues(b);
				if (d == null) return EntryFlags.TooDark;

				grayBytes.Add(d);
			}
			catch (Exception ex) {
				Logger.Instance.Info(
					$"Exception, file: {videoFile.Path}, reason: {ex.Message}, stacktrace {ex.StackTrace}");
				return EntryFlags.ThumbnailError;
			}

			return 0;
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

