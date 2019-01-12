using DuplicateFinderEngine.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;

namespace DuplicateFinderEngine {
	public class ScanEngine {
		public Settings Settings { get; } = new Settings();
		public event EventHandler<OwnScanProgress> Progress;
		public event EventHandler ScanDone;
		public event EventHandler FilesEnumerated;
		public int ScanProgressMaxValue;
		public int ScanProgressValue;
		public TimeSpan TimeElapsed;
		private DateTime startTime;
		public TimeSpan RemainingTime;
		private bool _isScanning;

		public Stopwatch ElapsedTimer = new Stopwatch();
		private PauseTokenSource m_pauseTokeSource;
		private CancellationTokenSource m_cancelationTokenSource;


		public HashSet<DuplicateItem> Duplicates { get; set; } = new HashSet<DuplicateItem>();
		private List<VideoFileEntry> FileList = new List<VideoFileEntry>();
		private int processedFiles;
		private readonly List<float> positionList = new List<float>();


		public async void StartSearch() {
			Duplicates.Clear();
			positionList.Clear();
			ElapsedTimer.Reset();
			for (var i = 0; i < Settings.ThumbnailCount; i++) {
				positionList.Add(1.0F / (Settings.ThumbnailCount + 1));
			}
			_isScanning = true;
			m_pauseTokeSource = new PauseTokenSource();
			m_cancelationTokenSource = new CancellationTokenSource();

			//get files
			Logger.Instance.Info(Properties.Resources.BuildingFileList);
			await Task.Run(() => InternalBuildFileList());
			FilesEnumerated?.Invoke(this, null);
			//set properties
			ScanProgressMaxValue = FileList.Count;
			//start scan
			startTime = DateTime.Now;
			Logger.Instance.Info(Properties.Resources.StartScan);
			if (!m_cancelationTokenSource.IsCancellationRequested)
				await Task.Run(() => InternalSearch(m_cancelationTokenSource.Token, m_pauseTokeSource));
			ScanDone?.Invoke(this, null);
			Logger.Instance.Info(Properties.Resources.ScanDone);
			_isScanning = false;
			ScanProgressValue = 0;
			SaveScannedFileList();
		}

		private void InternalBuildFileList() {
			FileList.Clear();
			LoadScannedFileList();
			var hasLoadedData = FileList.Count > 0;
			var st = Stopwatch.StartNew();
			foreach (var item in Settings.IncludeList) {
				foreach (var f in FileHelper.GetFilesRecursive(item, Settings.IgnoreReadOnlyFolders,
					Settings.IncludeSubDirectories, Settings.IncludeImages, Settings.BlackList.ToList())) {
					var vf = new VideoFileEntry(f);
					if (!hasLoadedData || !FileList.Any(a => a.Path.Equals(vf.Path)))
						FileList.Add(vf);
				}
			}

			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.FinishedBuildingFileListIn, st.Elapsed));
		}

		private void LoadScannedFileList() {
			var path = new FileInfo(Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
				"ScannedFiles.db"));
			if (path.Exists && path.Length == 0) //invalid data
			{
				path.Delete();
				return;
			}
			if (!path.Exists)
				return;
			Logger.Instance.Info(Properties.Resources.FoundPreviouslyScannedFilesImporting);
			var st = Stopwatch.StartNew();
			using (var file = new FileStream(path.FullName, FileMode.Open)) {
				FileList = Serializer.Deserialize<List<VideoFileEntry>>(file);
			}
			//Cleanup deleted files
			for (int i = FileList.Count - 1; i >= 0; i--) {
				if (!File.Exists(FileList[i].Path))
					FileList.RemoveAt(i);
			}

			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.PreviouslyScannedFilesImportedFilesInNoLongerExistingFilesHaveBeenRemoved, FileList.Count, st.Elapsed));
		}
		private void SaveScannedFileList() {
			Logger.Instance.Info(string.Format(Properties.Resources.SaveScannedFilesToDisk0N0Files, FileList.Count));
			using (var stream = new FileStream(Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
				"ScannedFiles.db"), FileMode.OpenOrCreate)) {
				Serializer.Serialize(stream, FileList);
			}
		}

		public void Pause() {
			if (!_isScanning || m_pauseTokeSource.IsPaused) return;
			Logger.Instance.Info(Properties.Resources.ScanPaused);
			ElapsedTimer.Stop();
			m_pauseTokeSource.IsPaused = true;

		}

		public void Resume() {
			if (!_isScanning || m_pauseTokeSource.IsPaused != true) return;
			Logger.Instance.Info(Properties.Resources.ScanResumed);
			m_pauseTokeSource.IsPaused = false;
			ElapsedTimer.Start();
			m_pauseTokeSource.IsPaused = false;
		}

		public void Stop() {
			if (m_pauseTokeSource.IsPaused)
				Resume();
			Logger.Instance.Info(Properties.Resources.ScanStopped);
			if (_isScanning)
				m_cancelationTokenSource.Cancel();
		}

		static readonly object AddDuplicateLock = new object();
		private void InternalSearch(CancellationToken cancelToken, PauseTokenSource pauseTokenSource) {
			ElapsedTimer.Start();
			processedFiles = 0;
			try {
				var st = Stopwatch.StartNew();
				Parallel.For(0, FileList.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancelToken }, i => {
					while (pauseTokenSource.IsPaused) {
						Thread.Sleep(500);
					}

					if (FileList[i].mediaInfo == null && !FileList[i].IsImage) {
						var ffProbe = new FFProbeWrapper.FFProbeWrapper();
						var info = ffProbe.GetMediaInfo(FileList[i].Path);
						if (info == null) return;
						FileList[i].mediaInfo = info;

					}

					if (FileList[i].grayBytes == null) {
						FileList[i].grayBytes = FileList[i].IsImage ? GetImageAsBitmaps(FileList[i], positionList.Count) : GetVideoThumbnailAsBitmaps(FileList[i], positionList);
						if (FileList[i].grayBytes == null) return;
					}


					//report progress
					Interlocked.Increment(ref processedFiles);
					var timeRemaining = TimeSpan.FromTicks(DateTime.Now.Subtract(startTime).Ticks * (ScanProgressMaxValue - (processedFiles + 1)) / (processedFiles + 1));
					Progress?.Invoke(this,
						new OwnScanProgress {
							CurrentPosition = processedFiles,
							CurrentFile = FileList[i].Path,
							Elapsed = ElapsedTimer.Elapsed,
							Remaining = timeRemaining
						});

				});
				st.Stop();
				Logger.Instance.Info(string.Format(Properties.Resources.ThumbnailsFinished, st.Elapsed, processedFiles));
				processedFiles = 0;
				st.Restart();

				var percentageDifference = 1.0f - Settings.Percent / 100f;

				Parallel.For(0, FileList.Count,
					new ParallelOptions {
						MaxDegreeOfParallelism = Environment.ProcessorCount,
						CancellationToken = cancelToken
					},
					i => {
						while (pauseTokenSource.IsPaused) {
							Thread.Sleep(500);
						}
						foreach (var itm in FileList) {
							if (itm == FileList[i]) continue;
							if (itm.grayBytes == null || itm.grayBytes.Count == 0) continue;
							if (FileList[i].grayBytes == null || FileList[i].grayBytes.Count == 0) continue;
							if (itm.grayBytes.Count != FileList[i].grayBytes.Count) continue;
							var duplicateCounter = 0;
							for (var j = 0; j < itm.grayBytes.Count; j++) {
								if (ExtensionMethods.PercentageDifference2(itm.grayBytes[j], FileList[i].grayBytes[j]) < percentageDifference) {
									duplicateCounter++;
								}
								else { break; }
							}
							if (duplicateCounter != itm.grayBytes.Count) continue;


							lock (AddDuplicateLock) {
								var firstInList = false;
								var secondInList = false;
								var groupId = Guid.NewGuid();
								foreach (var v in Duplicates) {
									if (v.Path == itm.Path) {
										groupId = v.GroupId;
										firstInList = true;
									}
									else if (v.Path == FileList[i].Path) {
										secondInList = true;
									}
								}
								if (!firstInList) {
									var origDup = new DuplicateItem(itm) {
										GroupId = groupId
									};
									var origImages = itm.IsImage ? GetImageThumbnail(origDup, positionList.Count) : GetVideoThumbnail(origDup, positionList);
									if (origImages == null) continue;
									origDup.Thumbnail = origImages;
									Duplicates.Add(origDup);
								}

								if (!secondInList) {
									var dup = new DuplicateItem(FileList[i]) {
										GroupId = groupId
									};
									var images = FileList[i].IsImage ? GetImageThumbnail(dup, positionList.Count) : GetVideoThumbnail(dup, positionList);
									if (images == null) continue;
									dup.Thumbnail = images;
									Duplicates.Add(dup);
								}
							}

							//we found a matching source then duplicate was added no need to go deeper
							break;
						}
						processedFiles++;
						//report progress
						var timeRemaining = TimeSpan.FromTicks(DateTime.Now.Subtract(startTime).Ticks * (FileList.Count - (processedFiles + 1)) / (processedFiles + 1));
						Progress?.Invoke(this,
							new OwnScanProgress {
								CurrentPosition = processedFiles,
								CurrentFile = FileList[i].Path,
								Elapsed = ElapsedTimer.Elapsed,
								Remaining = timeRemaining
							});

					});

				st.Stop();
				Logger.Instance.Info(string.Format(Properties.Resources.DuplicatesCheckFinishedIn, st.Elapsed));
			}
			catch (OperationCanceledException) {
				Logger.Instance.Info(Properties.Resources.CancellationExceptionCaught);
			}

		}

		public struct OwnScanProgress {
			public string CurrentFile;
			public int CurrentPosition;
			public TimeSpan Elapsed;
			public TimeSpan Remaining;
		}

		private List<Image> GetVideoThumbnail(DuplicateItem videoFile, List<float> positions) {
			var ffMpeg = new FFmpegWrapper.FFmpegWrapper();
			var images = new List<Image>();
			try {
				for (var i = 0; i < positions.Count; i++) {
					var b = ffMpeg.GetVideoThumbnail(videoFile.Path, Convert.ToSingle(videoFile.Duration.TotalSeconds * positionList[i]), false);
					if (b == null || b.Length == 0) return null;
					using (var byteStream = new MemoryStream(b)) {
						var bitmapImage = Image.FromStream(byteStream);
						images.Add(bitmapImage);
					}
				}
			}
			catch (FFmpegWrapper.FFMpegException) {
				//Logger.Instance.Info($"File: {videoFile.Path}, {ex.Message}");
				//Trace.TraceError(ex.Message);
				return null;
			}
			return images;
		}
		private List<Image> GetImageThumbnail(DuplicateItem videoFile, int count) {
			var images = new List<Image>();
			for (var i = 0; i < count; i++) {
				Image bitmapImage;
				try {
					bitmapImage = Image.FromFile(videoFile.Path);
				}
				catch {
					return null;
				}
				//Fill some missing data now when we have the information
				videoFile.FrameSize = $"{bitmapImage.Width}x{bitmapImage.Height}";
				videoFile.FrameSizeInt = bitmapImage.Width + bitmapImage.Height;

				double resizeFactor = 1;
				if (bitmapImage.Width > 100 || bitmapImage.Height > 100) {
					double widthFactor = Convert.ToDouble(bitmapImage.Width) / 100;
					double heightFactor = Convert.ToDouble(bitmapImage.Height) / 100;
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

				images.Add(newImage);
			}
			return images;
		}

		private List<byte[]> GetVideoThumbnailAsBitmaps(VideoFileEntry videoFile, List<float> positions) {
			var ffMpeg = new FFmpegWrapper.FFmpegWrapper();
			var images = new List<byte[]>();
			try {
				for (var i = 0; i < positions.Count; i++) {

					var b = ffMpeg.GetVideoThumbnail(videoFile.Path, Convert.ToSingle(videoFile.mediaInfo.Duration.TotalSeconds * positionList[i]), true);
					if (b == null || b.Length == 0) return null;
					using (var byteStream = new MemoryStream(b)) {
						using (var bitmapImage = Image.FromStream(byteStream)) {
							var d = ExtensionMethods.GetGrayScaleValues(new Bitmap(bitmapImage));
							if (d == null) return null;
							images.Add(d);
						}
					}
				}
			}
			catch (FFmpegWrapper.FFMpegException) {
				return null;
			}
			return images;

		}

		private List<byte[]> GetImageAsBitmaps(VideoFileEntry videoFile, int count) {
			var images = new List<byte[]>();
			for (var i = 0; i < count; i++) {
				try {
					using (var byteStream = File.OpenRead(videoFile.Path)) {
						using (var bitmapImage = Image.FromStream(byteStream)) {
							var b = new Bitmap(16, 16);
							using (var g = Graphics.FromImage(b)) {
								g.DrawImage(bitmapImage, 0, 0, 16, 16);
							}
							var d = ExtensionMethods.GetGrayScaleValues(b);
							if (d == null) return null;
							images.Add(d);
						}
					}
				}
				catch {
					return null;
				}
			}
			return images;
		}


		private static class ExtensionMethods {
			//TODO: Find a way to get the bytes directly from file without creating a new bitmap for it
			public static unsafe byte[] GetGrayScaleValues(Bitmap original, double darkProcent = 75) {
				// Lock the bitmap's bits.  
				var rect = new Rectangle(0, 0, original.Width, original.Height);
				var bmpData = original.LockBits(rect, ImageLockMode.ReadOnly, original.PixelFormat);

				// Get the address of the first line.
				var ptr = bmpData.Scan0;

				// Declare an array to hold the bytes of the bitmap.
				var bytes = bmpData.Stride * original.Height;
				var rgbValues = new byte[bytes];
				var buffer = new byte[256];

				// Copy the RGB values into the array.
				fixed (byte* byteArrayPtr = rgbValues) {
					Buffer.MemoryCopy((void*)ptr, byteArrayPtr, rgbValues.Length, rgbValues.Length);
				}
				original.UnlockBits(bmpData);

				int count = 0, all = bmpData.Width * bmpData.Height;
				var buffercounter = 0;
				for (var i = 0; i < rgbValues.Length; i += 4) {
					byte r = rgbValues[i + 2], g = rgbValues[i + 1], b = rgbValues[i];
					buffer[buffercounter] = r;
					buffercounter++;
					var brightness = (byte)Math.Round(0.299 * r + 0.5876 * g + 0.114 * b);
					if (brightness <= 0x40)
						count++;
				}
				return 100d / all * count >= darkProcent ? null : buffer;

			}
			public static float PercentageDifference2(IReadOnlyList<byte> img1, IReadOnlyList<byte> img2) {
				float diff = 0;
				for (var y = 0; y < img1.Count; y++) {
					diff += (float)Math.Abs(img1[y] - img2[y]) / 255;
				}
				return diff / (16 * 16);
			}
		}
	}
}
