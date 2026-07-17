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

using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {

	[DebuggerDisplay("{ItemInfo.Path,nq} - {ItemInfo.GroupId}")]
	public sealed class DuplicateItemVM : ReactiveObject, IJsonOnDeserialized {
		//For JSON deserialization only
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		public DuplicateItemVM() { }

		public DuplicateItemVM(DuplicateItem item) {
			ItemInfo = item;
			WireThumbnailUpdates();
		}

		// When a DuplicateItemVM is restored from a saved scan results backup it is created
		// through the parameterless constructor, so the ThumbnailsUpdated handler below would
		// never be attached. Without it an explicit "load thumbnails" pass fills ItemInfo.ImageList
		// but never writes the thumbnail pack, sets ThumbnailKey, or raises the UI, leaving restored
		// rows blank forever (issue #775). Re-wire it once deserialization has populated ItemInfo.
		public void OnDeserialized() => WireThumbnailUpdates();

		void WireThumbnailUpdates() {
			if (ItemInfo == null) return;
			ItemInfo.ThumbnailsUpdated += () => {
				try {
					// The grid layout is decided HERE, not inside JoinImages, and persisted
					// on the item: the results view needs it to slice the composite back
					// into frames and re-wrap them to the Preview column width
					// (WrappedFilmstrip, #834/#847) — including when the composite itself
					// comes straight from the cache and JoinImages never runs.
					int frameCount = ItemInfo.ImageList.Count;
					int gridColumns = frameCount > 1
						? Utils.ThumbnailGridLayout.Columns(frameCount, ThumbnailFrameAspect(ItemInfo))
						: 1;

					// Width is part of the key so thumbnails generated at different
					// ThumbnailMaxWidth values don't collide. Without it, re-scanning at a
					// larger width keeps serving the old, lower-resolution JPEG (AppendIfMissing
					// never overwrites), which the UI then upscales -> fuzzy/pixelated (issue #776).
					// Frame count and grid-layout version ("g2") are keyed for the same reason:
					// the composite's shape depends on both, so a rescan with a different
					// thumbnail count must not keep serving the old composite (#834). "g2"
					// = per-index cell placement with caller-chosen columns.
					var key = ThumbCacheHelpers.XxHash64Hex(
						ItemInfo.Path + "|w=" + SettingsFile.Instance.ThumbnailMaxWidth
						+ "|n=" + frameCount + "|g2");

					ThumbCacheHelpers.Provider?.AppendIfMissing(key, stream => {
						var uiBmp = ImageUtils.JoinImages(ItemInfo.ImageList, stream, gridColumns);
						if (uiBmp != null) {
							LRUBitmapCache.GetOrCreate(key, () => uiBmp);
						}
					});
					ThumbnailKey = key;
					_ThumbnailFrameCount = frameCount;
					_ThumbnailGridColumns = gridColumns;

				}
				catch { /* ignore */ }
				Dispatcher.UIThread.Post(() => {
					this.RaisePropertyChanged(nameof(ThumbnailFrameCount));
					this.RaisePropertyChanged(nameof(ThumbnailGridColumns));
					this.RaisePropertyChanged(nameof(Thumbnail));
				}, DispatcherPriority.Render);

			};
		}

		/// <summary>
		/// Aspect ratio hint for choosing the composite's storage grid. The extracted
		/// frames keep the source aspect (scaled to ThumbnailMaxWidth), so the media's
		/// FrameSize ("WxH") is used without decoding anything. Only the CHOICE of grid
		/// depends on this — compose and display both receive the chosen column count,
		/// so a wrong hint (e.g. rotated video) can't misalign the slices.
		/// </summary>
		internal static double ThumbnailFrameAspect(DuplicateItem item) {
			var frameSize = item.FrameSize;
			if (!string.IsNullOrEmpty(frameSize)) {
				int split = frameSize.IndexOf('x');
				if (split > 0
					&& int.TryParse(frameSize.AsSpan(0, split), out int width)
					&& int.TryParse(frameSize.AsSpan(split + 1), out int height)
					&& width > 0 && height > 0)
					return (double)width / height;
			}
			return 16.0 / 9;
		}
		public DuplicateItem ItemInfo { get; set; }

		// A row whose file is gone. Tombstone = intentionally deleted (drive mounted): its
		// fingerprint is still in the database, which is how this "already deleted" content got
		// matched again. Offline = drive unmounted (unplugged USB / reassigned letter): shown but
		// never a deletion target. Computed live, so a rescan or a replugged drive re-evaluates.
		[JsonIgnore]
		public bool IsTombstone => ItemInfo != null && VDF.Core.ScanEngine.PathIsTombstone(ItemInfo.Path);
		[JsonIgnore]
		public bool IsOffline => ItemInfo != null && VDF.Core.ScanEngine.PathIsOffline(ItemInfo.Path);

		[JsonInclude]
		public string ThumbnailKey { get; set; }

		// Storage-grid geometry of the composite behind ThumbnailKey, persisted with it
		// so restored results can slice the frames back out. 0 = unknown (results saved
		// before display-time wrapping existed): the composite renders as one image.
		int _ThumbnailFrameCount;
		[JsonInclude]
		public int ThumbnailFrameCount {
			get => _ThumbnailFrameCount;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailFrameCount, value);
		}
		int _ThumbnailGridColumns;
		[JsonInclude]
		public int ThumbnailGridColumns {
			get => _ThumbnailGridColumns;
			set => this.RaiseAndSetIfChanged(ref _ThumbnailGridColumns, value);
		}

		[JsonIgnore]
		private Bitmap? _thumbnail;

		[JsonIgnore]
		public Bitmap? Thumbnail {
			get {
				if (string.IsNullOrEmpty(ThumbnailKey)) return null;
				if (ThumbCacheHelpers.Provider == null)
#if DEBUG
					throw new InvalidOperationException("No active thumbnail provider");
#else
					return null;
#endif
				try {
					return LRUBitmapCache.GetOrCreate(ThumbnailKey, () => {
						using var s = ThumbCacheHelpers.Provider.OpenKey(ThumbnailKey);
						if (s == null) return null!;
						return new Avalonia.Media.Imaging.Bitmap(s);
					});
				}
				catch {
					return null;
				}
			}
			set => this.RaiseAndSetIfChanged(ref _thumbnail, value);
		}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

		bool _Checked;
		public bool Checked {
			get => _Checked;
			set => this.RaiseAndSetIfChanged(ref _Checked, value);
		}

		bool _PathCopiedFlash;
		/// <summary>Transient "Copied" badge next to the path after a deliberate copy click (#849).</summary>
		[JsonIgnore]
		public bool PathCopiedFlash {
			get => _PathCopiedFlash;
			set => this.RaiseAndSetIfChanged(ref _PathCopiedFlash, value);
		}

		int pathCopiedFlashVersion;
		/// <summary>
		/// Shows the badge for <paramref name="durationMs"/>; rapid re-copies extend the
		/// flash instead of an older timer hiding a newer badge early.
		/// </summary>
		internal async Task FlashPathCopiedAsync(int durationMs = 1500) {
			int version = Interlocked.Increment(ref pathCopiedFlashVersion);
			PathCopiedFlash = true;
			await Task.Delay(durationMs);
			if (version == Volatile.Read(ref pathCopiedFlashVersion))
				PathCopiedFlash = false;
		}

		// Hover-diff display: when non-null, shown instead of the normal value
		string? _DurationDiff;
		[JsonIgnore]
		public string? DurationDiff {
			get => _DurationDiff;
			set => this.RaiseAndSetIfChanged(ref _DurationDiff, value);
		}

		string? _FrameSizeDiff;
		[JsonIgnore]
		public string? FrameSizeDiff {
			get => _FrameSizeDiff;
			set => this.RaiseAndSetIfChanged(ref _FrameSizeDiff, value);
		}

		string? _SizeDiff;
		[JsonIgnore]
		public string? SizeDiff {
			get => _SizeDiff;
			set => this.RaiseAndSetIfChanged(ref _SizeDiff, value);
		}

		string? _FpsDiff;
		[JsonIgnore]
		public string? FpsDiff {
			get => _FpsDiff;
			set => this.RaiseAndSetIfChanged(ref _FpsDiff, value);
		}

		string? _BitRateDiff;
		[JsonIgnore]
		public string? BitRateDiff {
			get => _BitRateDiff;
			set => this.RaiseAndSetIfChanged(ref _BitRateDiff, value);
		}

		string? _AudioSampleRateDiff;
		[JsonIgnore]
		public string? AudioSampleRateDiff {
			get => _AudioSampleRateDiff;
			set => this.RaiseAndSetIfChanged(ref _AudioSampleRateDiff, value);
		}

		string? _AudioBitRateDiff;
		[JsonIgnore]
		public string? AudioBitRateDiff {
			get => _AudioBitRateDiff;
			set => this.RaiseAndSetIfChanged(ref _AudioBitRateDiff, value);
		}

		/// <summary>
		///   Returns if item matches the filter conditions
		/// </summary>
		[JsonIgnore]
		internal bool IsVisibleInFilter = true;

		public bool EqualsFull(DuplicateItemVM other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return ItemInfo.SizeLong == other.ItemInfo.SizeLong &&
				   ItemInfo.GroupId.Equals(other.ItemInfo.GroupId) &&
				   ItemInfo.Duration.Equals(other.ItemInfo.Duration) &&
				   ItemInfo.FrameSizeInt == other.ItemInfo.FrameSizeInt &&
				   string.Equals(ItemInfo.Format, other.ItemInfo.Format) &&
				   string.Equals(ItemInfo.AudioFormat, other.ItemInfo.AudioFormat) &&
				   string.Equals(ItemInfo.AudioChannel, other.ItemInfo.AudioChannel) &&
				   ItemInfo.AudioSampleRate == other.ItemInfo.AudioSampleRate &&
				   ItemInfo.BitRateKbs == other.ItemInfo.BitRateKbs && ItemInfo.Fps.Equals(other.ItemInfo.Fps);
		}
		public bool EqualsButSize(DuplicateItemVM other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return ItemInfo.GroupId.Equals(other.ItemInfo.GroupId) &&
				   ItemInfo.Duration.Equals(other.ItemInfo.Duration) &&
				   ItemInfo.FrameSizeInt == other.ItemInfo.FrameSizeInt &&
				   string.Equals(ItemInfo.Format, other.ItemInfo.Format) &&
				   string.Equals(ItemInfo.AudioFormat, other.ItemInfo.AudioFormat) &&
				   string.Equals(ItemInfo.AudioChannel, other.ItemInfo.AudioChannel) &&
				   ItemInfo.AudioSampleRate == other.ItemInfo.AudioSampleRate &&
				   ItemInfo.BitRateKbs == other.ItemInfo.BitRateKbs &&
				   ItemInfo.Fps.Equals(other.ItemInfo.Fps);
		}
		public bool EqualsButQuality(DuplicateItemVM other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return ItemInfo.GroupId.Equals(other.ItemInfo.GroupId);
		}
		public bool EqualsOnlyLength(DuplicateItemVM other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return ItemInfo.GroupId.Equals(other.ItemInfo.GroupId) && ItemInfo.Duration.Equals(other.ItemInfo.Duration);
		}

	}
}
