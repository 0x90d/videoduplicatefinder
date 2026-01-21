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
	public enum CompareMode { Single, Swipe, SideBySide, ThumbnailStep }
	public sealed class ThumbnailComparerVM : ReactiveObject {
		public ObservableCollection<LargeThumbnailDuplicateItem> Items { get; }
		public ObservableCollection<object> SelectedItems { get; } = new();
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
				// If user selects Swipe/SideBySide/ThumbnailStep but only 1 selection: warn & cancel
				if ((value == CompareMode.Swipe || value == CompareMode.SideBySide || value == CompareMode.ThumbnailStep) &&
				SelectedItems.OfType<LargeThumbnailDuplicateItem>().Take(2).Count() < 2) {
					ShowMessage(App.Lang["ThumbnailComparerDialog.SelectTwoElementsMessage"]);
					// return to single
					value = CompareMode.Single;
				}
				this.RaiseAndSetIfChanged(ref _selectedCompareMode, value);
				this.RaisePropertyChanged(nameof(IsSwipe));
				this.RaisePropertyChanged(nameof(IsSideBySide));
				this.RaisePropertyChanged(nameof(IsSingle));
				this.RaisePropertyChanged(nameof(IsThumbnailStep));
				this.RaisePropertyChanged(nameof(IsModeSliderEnabled));
				this.RaisePropertyChanged(nameof(ModeSliderLabel));
				UpdateImagesFromSelection();
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
					RxApp.MainThreadScheduler.Schedule(() => {
						IsMessageVisible = false;
					});
				}
				catch { /* abort */ }
			});
		}
		private double _swipeSeparatorX;
		public double SwipeSeparatorX { get => _swipeSeparatorX; set => this.RaiseAndSetIfChanged(ref _swipeSeparatorX, value); }
		private double _swipeWidth;
		public double SwipeWidth { get => _swipeWidth; set => this.RaiseAndSetIfChanged(ref _swipeWidth, value); }


		public bool IsSwipe => SelectedCompareMode == CompareMode.Swipe;
		public bool IsSideBySide => SelectedCompareMode == CompareMode.SideBySide || SelectedCompareMode == CompareMode.ThumbnailStep;
		public bool IsSingle => SelectedCompareMode == CompareMode.Single;
		public bool IsThumbnailStep => SelectedCompareMode == CompareMode.ThumbnailStep;
		private double _modeSliderValue = 0.5;
		public double ModeSliderValue {
			get => _modeSliderValue;
			set {
				this.RaiseAndSetIfChanged(ref _modeSliderValue, value);
				Recalc();
			}
		}

		public bool IsModeSliderEnabled => IsSwipe || SelectedCompareMode == CompareMode.Single;
		public string ModeSliderLabel => IsSwipe ? "Swipe:" : "Opacity:";
		private int _frameIndex;
		public int FrameIndex {
			get => _frameIndex;
			set {
				this.RaiseAndSetIfChanged(ref _frameIndex, value);
				UpdateThumbnailStepImages();
			}
		}
		private int _frameIndexMax;
		public int FrameIndexMax {
			get => _frameIndexMax;
			set => this.RaiseAndSetIfChanged(ref _frameIndexMax, value);
		}
		private double _zoom = 1.0;
		public double Zoom { get => _zoom; set => this.RaiseAndSetIfChanged(ref _zoom, value); }

		private Thickness _swipeThumbMargin;
		public Thickness SwipeThumbMargin { get => _swipeThumbMargin; set => this.RaiseAndSetIfChanged(ref _swipeThumbMargin, value); }
		public ReactiveCommand<Unit, Unit> FitToViewCommand => ReactiveCommand.Create(() => {
			Zoom = 1.0;
		});
		public ReactiveCommand<Unit, Unit> ResetZoomCommand => ReactiveCommand.Create(() => {
			Zoom = 1.0;
			_ = 0;
		});
		public ReactiveCommand<Unit, Unit> ZoomInCommand => ReactiveCommand.Create(() => {
			Zoom = Math.Min(Zoom * 1.25, 8.0);
		});
		public ReactiveCommand<Unit, Unit> ZoomOutCommand => ReactiveCommand.Create(() => {
			Zoom = Math.Max(Zoom / 1.25, 0.1);
		});
		public double DisplayWidth { get => _dispW; private set => this.RaiseAndSetIfChanged(ref _dispW, value); }
		public double DisplayHeight { get => _dispH; private set => this.RaiseAndSetIfChanged(ref _dispH, value); }
		public double LeftOffset { get => _left; private set => this.RaiseAndSetIfChanged(ref _left, value); }
		public double TopOffset { get => _top; private set => this.RaiseAndSetIfChanged(ref _top, value); }
		double _dispW, _dispH, _left, _top;
		private Geometry? _swipeClip;
		public Geometry? SwipeClip { get => _swipeClip; private set => this.RaiseAndSetIfChanged(ref _swipeClip, value); }

		public Thickness SeparatorMargin => new(SeparatorX, TopOffset, 0, 0);
		private double _separatorX;
		public double SeparatorX { get => _separatorX; private set { this.RaiseAndSetIfChanged(ref _separatorX, value); this.RaisePropertyChanged(nameof(SeparatorMargin)); } }
		private double _viewportWidth;
		public double ViewportWidth {
			get => _viewportWidth;
			set {
				this.RaiseAndSetIfChanged(ref _viewportWidth, value);
				Recalc();
			}
		}

		private double _viewportHeight;
		public double ViewportHeight {
			get => _viewportHeight;
			set {
				this.RaiseAndSetIfChanged(ref _viewportHeight, value);
				Recalc();
			}
		}

		public ThumbnailComparerVM(List<LargeThumbnailDuplicateItem> duplicateItemVMs) {
			Items = new(duplicateItemVMs);
			var modes = new ObservableCollection<CompareMode>
			{
			CompareMode.Single, CompareMode.Swipe, CompareMode.SideBySide
			};
			foreach (var mode in modes)
				compareModes.Add(mode);
			CompareModes = new ReadOnlyObservableCollection<CompareMode>(compareModes);

			SelectedItems.CollectionChanged += (_, __) => UpdateImagesFromSelection();

			this.WhenAnyValue(vm => vm.ModeSliderValue, vm => vm.SelectedCompareMode, vm => vm.ImageA, vm => vm.ImageB)
				.Throttle(TimeSpan.FromMilliseconds(16))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => Recalc());

		}

		private void UpdateImagesFromSelection() {
			var sel = SelectedItems.OfType<LargeThumbnailDuplicateItem>().Take(2).ToList();
			if (SelectedCompareMode == CompareMode.ThumbnailStep) {
				UpdateFrameBounds(sel);
				UpdateThumbnailStepImages(sel);
			}
			else {
				ImageA = sel.Count > 0 ? sel[0].Thumbnail : null;
				ImageB = sel.Count > 1 ? sel[1].Thumbnail : null;
			}

			UpdateCompareModes(sel);

			// Fallbacks
			if (SelectedCompareMode != CompareMode.Single && (ImageA is null || ImageB is null))
				SelectedCompareMode = CompareMode.Single;

			this.RaisePropertyChanged(nameof(ImageSingle));
			this.RaisePropertyChanged(nameof(IsSwipe));
			this.RaisePropertyChanged(nameof(IsSideBySide));
			this.RaisePropertyChanged(nameof(IsSingle));
			this.RaisePropertyChanged(nameof(IsThumbnailStep));

			Recalc();
		}

		void UpdateFrameBounds(List<LargeThumbnailDuplicateItem> selection) {
			var maxFrames = selection.Count == 2
				? Math.Min(selection[0].Frames.Count, selection[1].Frames.Count)
				: 0;
			FrameIndexMax = Math.Max(0, maxFrames - 1);
			if (FrameIndex > FrameIndexMax)
				FrameIndex = FrameIndexMax;
		}

		void UpdateThumbnailStepImages(List<LargeThumbnailDuplicateItem>? selection = null) {
			if (!IsThumbnailStep)
				return;
			selection ??= SelectedItems.OfType<LargeThumbnailDuplicateItem>().Take(2).ToList();
			if (selection.Count < 2) {
				ImageA = selection.Count == 1 ? selection[0].Thumbnail : null;
				ImageB = null;
				return;
			}
			var frameIndex = FrameIndex;
			ImageA = selection[0].GetFrame(frameIndex) ?? selection[0].Thumbnail;
			ImageB = selection[1].GetFrame(frameIndex) ?? selection[1].Thumbnail;
		}

		void UpdateCompareModes(List<LargeThumbnailDuplicateItem> selection) {
			var allowThumbnailStep = selection.Count == 2 &&
				selection.All(item => item.Frames.Count > 1);
			if (allowThumbnailStep) {
				if (!compareModes.Contains(CompareMode.ThumbnailStep))
					compareModes.Add(CompareMode.ThumbnailStep);
			}
			else if (compareModes.Contains(CompareMode.ThumbnailStep)) {
				compareModes.Remove(CompareMode.ThumbnailStep);
				if (SelectedCompareMode == CompareMode.ThumbnailStep) {
					SelectedCompareMode = selection.Count == 2 ? CompareMode.SideBySide : CompareMode.Single;
				}
			}
		}

		void Recalc() {
			if (!IsSwipe || ImageA is null || ImageB is null || ViewportWidth <= 0 || ViewportHeight <= 0) {
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
			var w = frameW * ModeSliderValue;
			SwipeClip = new RectangleGeometry(new Rect(0, 0, w, frameH));
			SeparatorX = frameLeft + Math.Max(0, w - 1);

		}

		public async Task LoadThumbnailsAsync(int maxParallel = 2, CancellationToken ct = default) {
			if (Items.Count == 0) return;

			IsLoadingThumbnails = true;
			LoadProgress = 0;
			this.RaisePropertyChanged(nameof(LoadProgressText));

			var sem = new SemaphoreSlim(maxParallel, maxParallel);
			int done = 0;
			int total = Items.Count;

			var tasks = Items.Select(async item => {
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

			try { await Task.WhenAll(tasks); }
			finally {
				IsLoadingThumbnails = false;
				UpdateImagesFromSelection();
			}
		}

	}

	public sealed class LargeThumbnailDuplicateItem : ReactiveObject {
		public DuplicateItemVM Item { get; }

		public Bitmap? Thumbnail { get; set; }
		public IReadOnlyList<Bitmap> Frames => _frames;
		readonly List<Bitmap> _frames = new();

		bool _IsLoadingThumbnail = true;
		public bool IsLoadingThumbnail {
			get => _IsLoadingThumbnail;
			set => this.RaiseAndSetIfChanged(ref _IsLoadingThumbnail, value);
		}
		public LargeThumbnailDuplicateItem(DuplicateItemVM duplicateItem) {
			Item = duplicateItem;
		}

		public void LoadThumbnail() {
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

			Thumbnail = ImageUtils.JoinImages(l)!;
			IsLoadingThumbnail = false;
			this.RaisePropertyChanged(nameof(Thumbnail));
		}

		public Bitmap? GetFrame(int index) {
			if (index < 0 || index >= _frames.Count)
				return null;
			return _frames[index];
		}
	}

}
