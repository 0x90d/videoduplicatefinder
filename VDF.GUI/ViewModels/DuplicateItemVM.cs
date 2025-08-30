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

using System.Diagnostics;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core.ViewModels;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {

	[DebuggerDisplay("{ItemInfo.Path,nq} - {ItemInfo.GroupId}")]
	public sealed class DuplicateItemVM : ReactiveObject {
		//For JSON deserialization only
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		public DuplicateItemVM() { }

		public DuplicateItemVM(DuplicateItem item) {
			ItemInfo = item;
			ItemInfo.ThumbnailsUpdated += () => {
				try {

					var key = ThumbCacheHelpers.XxHash64Hex(ItemInfo.Path);

					ThumbCacheHelpers.Provider?.AppendIfMissing(key, stream => {
						var uiBmp = ImageUtils.JoinImages(ItemInfo.ImageList, stream);
						if (uiBmp != null) {
							LRUBitmapCache.GetOrCreate(key, () => uiBmp);
						}
					});
					ThumbnailKey = key;

				}
				catch { /* ignore */ }
				Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(Thumbnail)), DispatcherPriority.Render);

			};
		}
		public DuplicateItem ItemInfo { get; set; }

		[JsonInclude]
		public string ThumbnailKey { get; set; }

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
						using var s = ThumbCacheHelpers.Provider.OpenKey(ThumbnailKey) ??
#if DEBUG
						throw new FileNotFoundException($"Thumbnail {ThumbnailKey} not found");
#else
						null;
#endif
						return new Avalonia.Media.Imaging.Bitmap(s);

					});
				}
				catch {
#if DEBUG
					throw;
#endif
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
