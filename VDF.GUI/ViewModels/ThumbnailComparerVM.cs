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
using Avalonia.Media.Imaging;
using ReactiveUI;
using SixLabors.ImageSharp;
using VDF.Core.FFTools;
using VDF.GUI.Data;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {
	public sealed class ThumbnailComparerVM : ReactiveObject {
		public ObservableCollection<LargeThumbnailDuplicateItem> Items { get; }
		public ThumbnailComparerVM(List<LargeThumbnailDuplicateItem> duplicateItemVMs)
			=> Items = new(duplicateItemVMs);

		public void LoadThumbnails() {
			foreach (var item in Items) {
				item.LoadThumbnail();
			}
		}
	}

	public sealed class LargeThumbnailDuplicateItem : ReactiveObject {
		public DuplicateItemVM Item { get; }

		public Bitmap? Thumbnail { get; set; }

		bool _IsLoadingThumbnail = true;
		public bool IsLoadingThumbnail {
			get => _IsLoadingThumbnail;
			set => this.RaiseAndSetIfChanged(ref _IsLoadingThumbnail, value);
		}
		public LargeThumbnailDuplicateItem(DuplicateItemVM duplicateItem) {
			Item = duplicateItem;
		}

		public void LoadThumbnail() {
			List<Image> l = new(Item.ItemInfo.IsImage ? 1 : Item.ItemInfo.ThumbnailTimestamps.Count);

			if (Item.ItemInfo.IsImage) {
				l.Add(Image.Load(Item.ItemInfo.Path));
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
						l.Add(Image.Load(byteStream));
					}
				}
			}

			Thumbnail = ImageUtils.JoinImages(l)!;
			IsLoadingThumbnail = false;
			this.RaisePropertyChanged(nameof(Thumbnail));
		}
	}

}
