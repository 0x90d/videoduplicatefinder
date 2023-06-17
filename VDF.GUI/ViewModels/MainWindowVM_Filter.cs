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

using System.Linq;
using Avalonia.Collections;
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

		DataGridCollectionView? view;
		public KeyValuePair<string, DataGridSortDescription>[] SortOrders { get; private set; }
		public sealed class CheckedGroupsComparer : System.Collections.IComparer {
			readonly MainWindowVM mainVM;
			public CheckedGroupsComparer(MainWindowVM vm) => mainVM = vm;
			public int Compare(object? x, object? y) {
				if (x == null || y == null)
					return -1;
				var dupX = (DuplicateItemVM)x;
				var dupY = (DuplicateItemVM)y;
				bool xHasChecked = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupX.ItemInfo.GroupId).Where(a => a.Checked).Any();
				bool yHasChecked = dupY.ItemInfo.GroupId == dupX.ItemInfo.GroupId ?
					xHasChecked :
					mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupY.ItemInfo.GroupId).Where(a => a.Checked).Any();
				return xHasChecked.CompareTo(yHasChecked);
			}
		}
		public sealed class GroupSizeComparer : System.Collections.IComparer {
			readonly MainWindowVM mainVM;
			public GroupSizeComparer(MainWindowVM vm) => mainVM = vm;
			public int Compare(object? x, object? y) {
				if (x == null || y == null)
					return -1;
				var dupX = (DuplicateItemVM)x;
				var dupY = (DuplicateItemVM)y;
				int groupSizeX = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupX.ItemInfo.GroupId).Count();
				int groupSizeY = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupY.ItemInfo.GroupId).Count();
				return groupSizeX.CompareTo(groupSizeY);
			}
		}
		public KeyValuePair<string, FileTypeFilter>[] TypeFilters { get; } = {
			new KeyValuePair<string, FileTypeFilter>("All",  FileTypeFilter.All),
			new KeyValuePair<string, FileTypeFilter>("Videos",  FileTypeFilter.Videos),
			new KeyValuePair<string, FileTypeFilter>("Images",  FileTypeFilter.Images),
		};

		KeyValuePair<string, FileTypeFilter> _FileType;

		public KeyValuePair<string, FileTypeFilter> FileType {
			get => _FileType;
			set {
				if (value.Key == _FileType.Key) return;
				_FileType = value;
				this.RaisePropertyChanged(nameof(FileType));
				view?.Refresh();
			}
		}
		KeyValuePair<string, DataGridSortDescription> _SortOrder;

		public KeyValuePair<string, DataGridSortDescription> SortOrder {
			get => _SortOrder;
			set {
				if (value.Key == _SortOrder.Key) return;
				_SortOrder = value;
				this.RaisePropertyChanged(nameof(SortOrder));
				view?.SortDescriptions.Clear();
				if (_SortOrder.Value != null)
					view?.SortDescriptions.Add(_SortOrder.Value);
				view?.Refresh();
			}
		}
		string _FilterByPath = string.Empty;

		public string FilterByPath {
			get => _FilterByPath;
			set {
				if (value == _FilterByPath) return;
				_FilterByPath = value;
				this.RaisePropertyChanged(nameof(FilterByPath));
				view?.Refresh();
			}
		}
		int _FilterSimilarityFrom = 0;
		public int FilterSimilarityFrom {
			get => _FilterSimilarityFrom;
			set {
				if (value == _FilterSimilarityFrom) return;
				this.RaiseAndSetIfChanged(ref _FilterSimilarityFrom, value);
				view?.Refresh();
			}
		}
		int _FilterSimilarityTo = 100;
		public int FilterSimilarityTo {
			get => _FilterSimilarityTo;
			set {
				if (value == _FilterSimilarityTo) return;
				this.RaiseAndSetIfChanged(ref _FilterSimilarityTo, value);
				view?.Refresh();
			}
		}

		bool DuplicatesFilter(object obj) {
			if (obj is not DuplicateItemVM data) return false;
			var success = true;
			if (!string.IsNullOrEmpty(FilterByPath)) {
				success = data.ItemInfo.Path.Contains(FilterByPath, StringComparison.OrdinalIgnoreCase);
				//see if a group member matches, then this should be considered as match too
				if (!success)
					success = Duplicates.Any(s =>
						s.ItemInfo.GroupId == data.ItemInfo.GroupId &&
						s.ItemInfo.Path.Contains(FilterByPath, StringComparison.OrdinalIgnoreCase));
			}
			if (success && FileType.Value != FileTypeFilter.All)
				success = FileType.Value == FileTypeFilter.Images ? data.ItemInfo.IsImage : !data.ItemInfo.IsImage;
			if (success) {
				success = data.ItemInfo.Similarity >= FilterSimilarityFrom && data.ItemInfo.Similarity <= FilterSimilarityTo;
			}
			data.IsVisibleInFilter = success;
			return success;
		}
	}
}
