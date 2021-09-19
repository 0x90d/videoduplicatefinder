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
using Avalonia.Media.Imaging;
using ReactiveUI;
using VDF.Core.ViewModels;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {

	[DebuggerDisplay("{ItemInfo.Path,nq} - {ItemInfo.GroupId}")]
	public sealed class DuplicateItemVM : ReactiveObject {
		//For JSON deserialization only
		public DuplicateItemVM() {  }

		public DuplicateItemVM(DuplicateItem item) {
			ItemInfo = item;
			ItemInfo.ThumbnailsUpdated += () => {
				Thumbnail = ImageUtils.JoinImages(ItemInfo.ImageList);
				this.RaisePropertyChanged(nameof(Thumbnail));
			};
		}
		public DuplicateItem ItemInfo { get; set; }

		public Bitmap Thumbnail { get; set; }

		bool _Checked;
		public bool Checked {
			get => _Checked;
			set => this.RaiseAndSetIfChanged(ref _Checked, value);
		}

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
