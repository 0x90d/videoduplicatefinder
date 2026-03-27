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

using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ReactiveUI;
using SixLabors.ImageSharp;
using VDF.Core.FFTools;
using VDF.GUI.Data;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {
	public enum CompareMode { Single, Swipe, SideBySide, Stacked }
	public sealed class ThumbnailComparerVM : ReactiveObject {
		public ObservableCollection<LargeThumbnailDuplicateItem> Items { get; }

		private LargeThumbnailDuplicateItem? _selectedItemA;
		public LargeThumbnailDuplicateItem? SelectedItemA {
			get => _selectedItemA;
			set {
				if (_selectedItemA == value) return;
				if (_selectedItemA != null) _selectedItemA.IsSourceA = false;
				this.RaiseAndSetIfChanged(ref _selectedItemA, value);
				if (_selectedItemA != null) _selectedItemA.IsSourceA = true;
				OnSelectionChanged();
			}
		}

		private LargeThumbnailDuplicateItem? _selectedItemB;
		public LargeThumbnailDuplicateItem? SelectedItemB {
			get => _selectedItemB;
			set {
				if (_selectedItemB == value) return;
				if (_selectedItemB != null) _selectedItemB.IsSourceB = false;
				this.RaiseAndSetIfChanged(ref _selectedItemB, value);
				if (_selectedItemB != null) _selectedItemB.IsSourceB = true;
				OnSelectionChanged();
			}
		}

		private Bitmap? _imageA;
		public Bitmap? ImageA { get => _imageA; set => this.RaiseAndSetIfChanged(ref _imageA, value); }

		private Bitmap? _imageB;
		public Bitmap? ImageB { get => _imageB; set => this.RaiseAndSetIfChanged(ref _imageB, value); }
		public Bitmap? ImageSingle => ImageA ?? ImageB;

		public ReadOnlyObservableCollection<CompareMode> CompareModes { get; }
		readonly ObservableCollection<CompareMode> compareModes = new();
		private CompareMode _selectedCompareMode = CompareMode.Swipe;
		public CompareMode SelectedCompareMode {
			get => _selectedCompareMode;
			set {
				if ((value != CompareMode.Single) && (SelectedItemA is null || SelectedItemB is null)) {
					ShowMessage(App.Lang["ThumbnailComparerDialog.SelectTwoElementsMessage"]);
					value = CompareMode.Single;
				}
				this.RaiseAndSetIfChanged(ref _selectedCompareMode, value);
				this.RaisePropertyChanged(nameof(IsSwipe));
				this.RaisePropertyChanged(nameof(IsSideBySide));
				this.RaisePropertyChanged(nameof(IsStacked));
				this.RaisePropertyChanged(nameof(IsSingle));
				this.RaisePropertyChanged(nameof(IsDualView));
				this.RaisePropertyChanged(nameof(SwipeHintVisible));
				UpdateImages();
			}
		}

		private bool _isLoadingThumbnails;
		public bool IsLoadingThumbnails { get => _isLoadingThumbnails; set => this.RaiseAndSetIfChanged(ref _isLoadingThumbnails, value); }

		private double _loadProgress;
		public double LoadProgress { get => _loadProgress; set => this.RaiseAndSetIfChanged(ref _loadProgress, value); }
		public string LoadProgressText => $"{Math.Round(LoadProgress * 100)}%";

		private string? _userMessage;
		public string? UserMessage { get => _userMessage; set => this.RaiseAndSetIfChanged(ref _userMessage, value); }

		private bool _isMessageVisible;
		public bool IsMessageVisible { get => _isMessageVisible; set => this.RaiseAndSetIfChanged(ref _isMessageVisible, value); }

		CancellationTokenSource? _messageCts;
		public void ShowMessage(string msg, int millis = 3000) {
			_messageCts?.Cancel();
			_messageCts = new CancellationTokenSource();
			UserMessage = msg;
			IsMessageVisible = true;
			var token = _messageCts.Token;
			_ = Task.Run(async () => {
				try {
					await Task.Delay(millis, token);
					RxApp.MainThreadScheduler.Schedule(() => IsMessageVisible = false);
				}
				catch { }
			});
		}

		public bool IsSwipe => SelectedCompareMode == CompareMode.Swipe;
		public bool IsSideBySide => SelectedCompareMode == CompareMode.SideBySide;
		public bool IsStacked => SelectedCompareMode == CompareMode.Stacked;
		public bool IsSingle => SelectedCompareMode == CompareMode.Single;
		// True for SideBySide or Stacked (both show two images with labels)
		public bool IsDualView => IsSideBySide || IsStacked;
		public bool SwipeHintVisible => IsSwipe;

		private double _modeSliderValue = 0.5;
		public double ModeSliderValue {
			get => _modeSliderValue;
			set {
				this.RaiseAndSetIfChanged(ref _modeSliderValue, value);
				Recalc();
			}
		}

		// --- Frame stepping (works in ALL modes) ---

		private bool _showFrameControls;
		public bool ShowFrameControls {
			get => _showFrameControls;
			private set => this.RaiseAndSetIfChanged(ref _showFrameControls, value);
		}

		// Only show position slider when there are multiple base positions to choose from
		private bool _showPositionControls;
		public bool ShowPositionControls {
			get => _showPositionControls;
			private set => this.RaiseAndSetIfChanged(ref _showPositionControls, value);
		}

		// Selects which pre-extracted thumbnail position to start from
		private int _baseThumbnailIndex;
		public int BaseThumbnailIndex {
			get => _baseThumbnailIndex;
			set {
				this.RaiseAndSetIfChanged(ref _baseThumbnailIndex, Math.Clamp(value, 0, Math.Max(BaseThumbnailIndexMax, 0)));
				StepA = 0;
				StepB = 0;
			}
		}
		private int _baseThumbnailIndexMax;
		public int BaseThumbnailIndexMax {
			get => _baseThumbnailIndexMax;
			private set => this.RaiseAndSetIfChanged(ref _baseThumbnailIndexMax, value);
		}

		// Independent frame step for A
		private int _stepA;
		public int StepA {
			get => _stepA;
			set {
				this.RaiseAndSetIfChanged(ref _stepA, value);
				UpdateFrameImages();
			}
		}

		// Independent frame step for B
		private int _stepB;
		public int StepB {
			get => _stepB;
			set {
				this.RaiseAndSetIfChanged(ref _stepB, value);
				UpdateFrameImages();
			}
		}

		// Display labels (visible in ALL modes)
		private string _frameLabelA = string.Empty;
		public string FrameLabelA { get => _frameLabelA; set => this.RaiseAndSetIfChanged(ref _frameLabelA, value); }

		private string _frameLabelB = string.Empty;
		public string FrameLabelB { get => _frameLabelB; set => this.RaiseAndSetIfChanged(ref _frameLabelB, value); }

		private bool _isExtractingFrame;
		public bool IsExtractingFrame { get => _isExtractingFrame; set => this.RaiseAndSetIfChanged(ref _isExtractingFrame, value); }

		private double _zoom = 1.0;
		public double Zoom { get => _zoom; set => this.RaiseAndSetIfChanged(ref _zoom, value); }

		private double _panOffsetX;
		public double PanOffsetX { get => _panOffsetX; set => this.RaiseAndSetIfChanged(ref _panOffsetX, value); }

		private double _panOffsetY;
		public double PanOffsetY { get => _panOffsetY; set => this.RaiseAndSetIfChanged(ref _panOffsetY, value); }

		public ReactiveCommand<Unit, Unit> FitToViewCommand { get; }
		public ReactiveCommand<Unit, Unit> ResetZoomCommand { get; }
		public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }
		public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }

		public ReactiveCommand<Unit, Unit> PrevBaseCommand { get; }
		public ReactiveCommand<Unit, Unit> NextBaseCommand { get; }
		public ReactiveCommand<Unit, Unit> StepAMinusCommand { get; }
		public ReactiveCommand<Unit, Unit> StepAPlusCommand { get; }
		public ReactiveCommand<Unit, Unit> StepBMinusCommand { get; }
		public ReactiveCommand<Unit, Unit> StepBPlusCommand { get; }
		public ReactiveCommand<Unit, Unit> ResetStepsCommand { get; }

		public double DisplayWidth { get => _dispW; private set => this.RaiseAndSetIfChanged(ref _dispW, value); }
		public double DisplayHeight { get => _dispH; private set => this.RaiseAndSetIfChanged(ref _dispH, value); }
		public double LeftOffset { get => _left; private set => this.RaiseAndSetIfChanged(ref _left, value); }
		public double TopOffset { get => _top; private set => this.RaiseAndSetIfChanged(ref _top, value); }
		double _dispW, _dispH, _left, _top;

		private Geometry? _swipeClip;
		public Geometry? SwipeClip { get => _swipeClip; private set => this.RaiseAndSetIfChanged(ref _swipeClip, value); }

		public Thickness SeparatorMargin => new(SeparatorX, TopOffset, 0, 0);
		private double _separatorX;
		public double SeparatorX {
			get => _separatorX;
			private set { this.RaiseAndSetIfChanged(ref _separatorX, value); this.RaisePropertyChanged(nameof(SeparatorMargin)); }
		}

		private double _separatorY;
		public double SeparatorY {
			get => _separatorY;
			private set => this.RaiseAndSetIfChanged(ref _separatorY, value);
		}

		private double _viewportWidth;
		public double ViewportWidth {
			get => _viewportWidth;
			set { this.RaiseAndSetIfChanged(ref _viewportWidth, value); Recalc(); }
		}

		private double _viewportHeight;
		public double ViewportHeight {
			get => _viewportHeight;
			set { this.RaiseAndSetIfChanged(ref _viewportHeight, value); Recalc(); }
		}

		CancellationTokenSource? _frameExtractCts;

		public ThumbnailComparerVM(List<LargeThumbnailDuplicateItem> duplicateItemVMs) {
			Items = new(duplicateItemVMs);

			var modes = new ObservableCollection<CompareMode> {
				CompareMode.Single, CompareMode.Swipe, CompareMode.SideBySide, CompareMode.Stacked
			};
			foreach (var mode in modes)
				compareModes.Add(mode);
			CompareModes = new ReadOnlyObservableCollection<CompareMode>(compareModes);

			this.WhenAnyValue(vm => vm.ModeSliderValue, vm => vm.SelectedCompareMode, vm => vm.ImageA, vm => vm.ImageB)
				.Throttle(TimeSpan.FromMilliseconds(16))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => Recalc());

			FitToViewCommand = ReactiveCommand.Create(() => { Zoom = 1.0; PanOffsetX = 0; PanOffsetY = 0; });
			ResetZoomCommand = ReactiveCommand.Create(() => { Zoom = 1.0; PanOffsetX = 0; PanOffsetY = 0; });
			ZoomInCommand = ReactiveCommand.Create(() => { Zoom = Math.Min(Zoom * 1.25, 8.0); });
			ZoomOutCommand = ReactiveCommand.Create(() => { Zoom = Math.Max(Zoom / 1.25, 0.1); });

			PrevBaseCommand = ReactiveCommand.Create(() => { if (BaseThumbnailIndex > 0) BaseThumbnailIndex--; });
			NextBaseCommand = ReactiveCommand.Create(() => { if (BaseThumbnailIndex < BaseThumbnailIndexMax) BaseThumbnailIndex++; });
			StepAMinusCommand = ReactiveCommand.Create(() => { StepA--; });
			StepAPlusCommand = ReactiveCommand.Create(() => { StepA++; });
			StepBMinusCommand = ReactiveCommand.Create(() => { StepB--; });
			StepBPlusCommand = ReactiveCommand.Create(() => { StepB++; });
			ResetStepsCommand = ReactiveCommand.Create(() => { StepA = 0; StepB = 0; });
		}

		public void AssignDefaultSelections() {
			if (Items.Count >= 2) {
				SelectedItemA = Items[0];
				SelectedItemB = Items[1];
			}
			else if (Items.Count == 1) {
				SelectedItemA = Items[0];
			}
			// Re-evaluate after thumbnails are loaded
			UpdateShowFrameControls();
			UpdateImages();
		}

		void OnSelectionChanged() {
			UpdateShowFrameControls();
			UpdateImages();
		}

		void UpdateShowFrameControls() {
			var a = SelectedItemA;
			var b = SelectedItemB;
			bool hasVideoA = a != null && !a.Item.ItemInfo.IsImage && a.Frames.Count > 0;
			bool hasVideoB = b != null && !b.Item.ItemInfo.IsImage && b.Frames.Count > 0;
			ShowFrameControls = hasVideoA || hasVideoB;

			int maxA = hasVideoA ? a!.Frames.Count : 0;
			int maxB = hasVideoB ? b!.Frames.Count : 0;
			if (maxA > 0 && maxB > 0)
				BaseThumbnailIndexMax = Math.Min(maxA, maxB) - 1;
			else if (maxA > 0)
				BaseThumbnailIndexMax = maxA - 1;
			else if (maxB > 0)
				BaseThumbnailIndexMax = maxB - 1;
			else
				BaseThumbnailIndexMax = 0;

			if (BaseThumbnailIndex > BaseThumbnailIndexMax)
				BaseThumbnailIndex = BaseThumbnailIndexMax;

			ShowPositionControls = BaseThumbnailIndexMax > 0;
		}

		void UpdateImages() {
			if (ShowFrameControls) {
				UpdateFrameImages();
			}
			else {
				ImageA = SelectedItemA?.Thumbnail;
				ImageB = SelectedItemB?.Thumbnail;
			}

			if (SelectedCompareMode != CompareMode.Single && (ImageA is null || ImageB is null))
				SelectedCompareMode = CompareMode.Single;

			this.RaisePropertyChanged(nameof(ImageSingle));
			UpdateFrameLabels();
			Recalc();
		}

		Bitmap? GetItemFrameSync(LargeThumbnailDuplicateItem? item, int baseIdx, int step) {
			if (item == null) return null;
			if (item.Item.ItemInfo.IsImage || item.Frames.Count == 0)
				return item.Thumbnail;
			if (step == 0)
				return item.GetFrame(baseIdx) ?? item.Thumbnail;
			return item.GetCachedOffsetFrame(baseIdx, step) ?? item.GetFrame(baseIdx) ?? item.Thumbnail;
		}

		void UpdateFrameImages() {
			var baseIdx = BaseThumbnailIndex;
			var itemA = SelectedItemA;
			var itemB = SelectedItemB;
			var stepA = StepA;
			var stepB = StepB;

			bool isVideoA = itemA != null && !itemA.Item.ItemInfo.IsImage && itemA.Frames.Count > 0;
			bool isVideoB = itemB != null && !itemB.Item.ItemInfo.IsImage && itemB.Frames.Count > 0;

			// Show cached/base frames immediately
			ImageA = GetItemFrameSync(itemA, baseIdx, isVideoA ? stepA : 0);
			ImageB = GetItemFrameSync(itemB, baseIdx, isVideoB ? stepB : 0);
			this.RaisePropertyChanged(nameof(ImageSingle));

			bool needExtractA = isVideoA && stepA != 0 && itemA!.GetCachedOffsetFrame(baseIdx, stepA) == null;
			bool needExtractB = isVideoB && stepB != 0 && itemB!.GetCachedOffsetFrame(baseIdx, stepB) == null;

			if (!needExtractA && !needExtractB) {
				UpdateFrameLabels();
				return;
			}

			_frameExtractCts?.Cancel();
			var cts = new CancellationTokenSource();
			_frameExtractCts = cts;
			IsExtractingFrame = true;

			_ = Task.Run(() => {
				try {
					Bitmap? bmpA = needExtractA ? itemA!.ExtractFrameAtOffset(baseIdx, stepA) : null;
					if (cts.IsCancellationRequested) return;
					Bitmap? bmpB = needExtractB ? itemB!.ExtractFrameAtOffset(baseIdx, stepB) : null;
					if (cts.IsCancellationRequested) return;

					RxApp.MainThreadScheduler.Schedule(() => {
						if (cts.IsCancellationRequested) return;
						if (needExtractA && bmpA != null)
							ImageA = bmpA;
						if (needExtractB && bmpB != null)
							ImageB = bmpB;
						this.RaisePropertyChanged(nameof(ImageSingle));
						IsExtractingFrame = false;
						UpdateFrameLabels();
					});
				}
				catch {
					RxApp.MainThreadScheduler.Schedule(() => IsExtractingFrame = false);
				}
			});

			UpdateFrameLabels();
		}

		string FormatFrameLabel(LargeThumbnailDuplicateItem item, int baseIdx, int step) {
			var name = item.FileName;
			if (item.Item.ItemInfo.IsImage || item.Frames.Count == 0)
				return name;

			var fps = item.Item.ItemInfo.Fps;
			if (fps <= 0) fps = 30;

			if (baseIdx >= 0 && baseIdx < item.Item.ItemInfo.ThumbnailTimestamps.Count) {
				var baseTs = item.Item.ItemInfo.ThumbnailTimestamps[baseIdx];
				var currentTs = baseTs + TimeSpan.FromSeconds(step / (double)fps);
				if (currentTs < TimeSpan.Zero) currentTs = TimeSpan.Zero;
				var approxFrame = (int)Math.Round(currentTs.TotalSeconds * fps);
				var stepStr = step == 0 ? "" : $"  (step {(step > 0 ? "+" : "")}{step})";
				return $"{name}  |  ~Frame {approxFrame}  |  {currentTs:hh\\:mm\\:ss\\.ff}{stepStr}";
			}

			return name;
		}

		void UpdateFrameLabels() {
			if (SelectedItemA != null && ShowFrameControls)
				FrameLabelA = FormatFrameLabel(SelectedItemA, BaseThumbnailIndex, StepA);
			else
				FrameLabelA = SelectedItemA?.FileName ?? string.Empty;

			if (SelectedItemB != null && ShowFrameControls)
				FrameLabelB = FormatFrameLabel(SelectedItemB, BaseThumbnailIndex, StepB);
			else
				FrameLabelB = SelectedItemB?.FileName ?? string.Empty;
		}

		void Recalc() {
			if ((!IsSwipe && !IsStacked) || ImageA is null || ImageB is null || ViewportWidth <= 0 || ViewportHeight <= 0) {
				SwipeClip = null;
				return;
			}

			var wa = ImageA.PixelSize.Width;
			var ha = ImageA.PixelSize.Height;

			var scale = Math.Min(ViewportWidth / wa, ViewportHeight / ha);
			var frameW = wa * scale;
			var frameH = ha * scale;
			var frameLeft = (ViewportWidth - frameW) / 2.0;
			var frameTop = (ViewportHeight - frameH) / 2.0;

			DisplayWidth = frameW;
			DisplayHeight = frameH;
			LeftOffset = frameLeft;
			TopOffset = frameTop;

			if (IsSwipe) {
				var w = frameW * ModeSliderValue;
				SwipeClip = new RectangleGeometry(new Rect(0, 0, w, frameH));
				SeparatorX = frameLeft + Math.Max(0, w - 1);
			}
			else if (IsStacked) {
				var h = frameH * ModeSliderValue;
				SwipeClip = new RectangleGeometry(new Rect(0, 0, frameW, h));
				SeparatorY = frameTop + Math.Max(0, h - 1);
			}
		}

		public async Task LoadThumbnailsAsync(int maxParallel = 2, CancellationToken ct = default) {
			if (Items.Count == 0) return;

			IsLoadingThumbnails = true;
			LoadProgress = 0;
			this.RaisePropertyChanged(nameof(LoadProgressText));

			await LoadItemsWithRetry(maxParallel, 3, ct);

			// Check for still-failed items
			var failed = Items.Where(i => i.Thumbnail == null).ToList();
			if (failed.Count > 0) {
				// Show notification and wait for main window thumbnail retrieval to finish
				ShowMessage(string.Format(App.Lang["ThumbnailComparerDialog.ThumbnailLoadFailed"], failed.Count), 60000);

				while (!ct.IsCancellationRequested) {
					try {
						if (!ApplicationHelpers.MainWindowDataContext.ShowThumbnailRetrievalProgressBar &&
							!ApplicationHelpers.MainWindowDataContext.IsScanning) break;
						await Task.Delay(1000, ct);
					}
					catch { break; }
				}

				// Final retry now that main window is done
				IsLoadingThumbnails = true;
				LoadProgress = 0;
				this.RaisePropertyChanged(nameof(LoadProgressText));
				IsMessageVisible = false;

				await LoadItemsWithRetry(maxParallel, 1, ct, failed);
			}

			IsLoadingThumbnails = false;
			AssignDefaultSelections();
		}

		async Task LoadItemsWithRetry(int maxParallel, int maxRetries, CancellationToken ct, List<LargeThumbnailDuplicateItem>? subset = null) {
			var itemsToLoad = subset ?? Items.ToList();
			for (int attempt = 0; attempt <= maxRetries; attempt++) {
				if (attempt > 0) {
					itemsToLoad = itemsToLoad.Where(i => i.Thumbnail == null).ToList();
					if (itemsToLoad.Count == 0) break;
				}

				var sem = new SemaphoreSlim(maxParallel, maxParallel);
				int done = 0;
				int total = itemsToLoad.Count;

				var tasks = itemsToLoad.Select(async item => {
					await sem.WaitAsync(ct).ConfigureAwait(false);
					try {
						await Task.Run(() => item.LoadThumbnail(), ct).ConfigureAwait(false);
					}
					finally {
						sem.Release();
						var finished = Interlocked.Increment(ref done);
						var p = Math.Clamp((double)finished / total, 0, 1);
						RxApp.MainThreadScheduler.Schedule(() => {
							LoadProgress = p;
							this.RaisePropertyChanged(nameof(LoadProgressText));
						});
					}
				});

				await Task.WhenAll(tasks);
			}
		}
	}

	public sealed class LargeThumbnailDuplicateItem : ReactiveObject {
		public DuplicateItemVM Item { get; }

		public Bitmap? Thumbnail { get; set; }
		public IReadOnlyList<Bitmap> Frames => _frames;
		readonly List<Bitmap> _frames = new();
		readonly Dictionary<(int, int), Bitmap?> _offsetFrameCache = new();

		public string FileName => System.IO.Path.GetFileName(Item.ItemInfo.Path);

		bool _IsLoadingThumbnail = true;
		public bool IsLoadingThumbnail {
			get => _IsLoadingThumbnail;
			set => this.RaiseAndSetIfChanged(ref _IsLoadingThumbnail, value);
		}

		bool _isSourceA;
		public bool IsSourceA { get => _isSourceA; set => this.RaiseAndSetIfChanged(ref _isSourceA, value); }

		bool _isSourceB;
		public bool IsSourceB { get => _isSourceB; set => this.RaiseAndSetIfChanged(ref _isSourceB, value); }

		public LargeThumbnailDuplicateItem(DuplicateItemVM duplicateItem) {
			Item = duplicateItem;
		}

		public void LoadThumbnail() {
			try {
				List<Bitmap> l = new(Item.ItemInfo.IsImage ? 1 : Item.ItemInfo.ThumbnailTimestamps.Count);
				_frames.Clear();

				if (Item.ItemInfo.IsImage) {
					var bmp = new Bitmap(Item.ItemInfo.Path);
					l.Add(bmp);
					_frames.Add(bmp);
				}
				else {
					for (int i = 0; i < Item.ItemInfo.ThumbnailTimestamps.Count; i++) {
						var b = FfmpegEngine.GetThumbnail(new FfmpegSettings {
							File = Item.ItemInfo.Path,
							Position = Item.ItemInfo.ThumbnailTimestamps[i],
							GrayScale = 0,
							Fullsize = 1
						}, SettingsFile.Instance.ExtendedFFToolsLogging);
						if (b != null && b.Length > 0) {
							using var byteStream = new MemoryStream(b);
							var bmp = new Bitmap(byteStream);
							l.Add(bmp);
							_frames.Add(bmp);
						}
					}
				}

				if (l.Count > 0)
					Thumbnail = ImageUtils.JoinImages(l);
			}
			catch { }
			finally {
				IsLoadingThumbnail = false;
				this.RaisePropertyChanged(nameof(Thumbnail));
			}
		}

		public Bitmap? GetFrame(int index) {
			if (index < 0 || index >= _frames.Count)
				return null;
			return _frames[index];
		}

		public Bitmap? GetCachedOffsetFrame(int thumbnailIndex, int offset) {
			if (offset == 0) return GetFrame(thumbnailIndex);
			var key = (thumbnailIndex, offset);
			return _offsetFrameCache.TryGetValue(key, out var cached) ? cached : null;
		}

		public Bitmap? ExtractFrameAtOffset(int thumbnailIndex, int offset) {
			if (offset == 0) return GetFrame(thumbnailIndex);

			var key = (thumbnailIndex, offset);
			lock (_offsetFrameCache) {
				if (_offsetFrameCache.TryGetValue(key, out var cached))
					return cached;
			}

			if (Item.ItemInfo.IsImage) return GetFrame(thumbnailIndex);
			if (thumbnailIndex < 0 || thumbnailIndex >= Item.ItemInfo.ThumbnailTimestamps.Count)
				return null;

			var fps = Item.ItemInfo.Fps;
			if (fps <= 0) fps = 30;
			var baseTimestamp = Item.ItemInfo.ThumbnailTimestamps[thumbnailIndex];
			var targetTimestamp = baseTimestamp + TimeSpan.FromSeconds(offset / (double)fps);

			if (targetTimestamp < TimeSpan.Zero) targetTimestamp = TimeSpan.Zero;
			if (Item.ItemInfo.Duration > TimeSpan.Zero && targetTimestamp > Item.ItemInfo.Duration)
				targetTimestamp = Item.ItemInfo.Duration;

			var b = FfmpegEngine.GetThumbnail(new FfmpegSettings {
				File = Item.ItemInfo.Path,
				Position = targetTimestamp,
				GrayScale = 0,
				Fullsize = 1
			}, SettingsFile.Instance.ExtendedFFToolsLogging);

			Bitmap? bmp = null;
			if (b != null && b.Length > 0) {
				using var byteStream = new MemoryStream(b);
				bmp = new Bitmap(byteStream);
			}

			lock (_offsetFrameCache) {
				_offsetFrameCache[key] = bmp;
			}
			return bmp;
		}
	}
}
