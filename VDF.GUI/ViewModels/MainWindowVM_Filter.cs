// /*
//     Copyright (C) 2026 0x90d
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

using System.ComponentModel;
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
				if (x is not DuplicateItemVM dx || y is not DuplicateItemVM dy) return -1;
				return mainVM.GroupHasCheckedItems(dx.ItemInfo.GroupId)
					.CompareTo(mainVM.GroupHasCheckedItems(dy.ItemInfo.GroupId));
			}
		}
		public sealed class GroupTotalSizeComparer : System.Collections.IComparer {
		readonly MainWindowVM mainVM;
		private readonly Dictionary<Guid, long> guidMap = new();
		public GroupTotalSizeComparer(MainWindowVM vm) => mainVM = vm;
		public int Compare(object? x, object? y) {
			if (x == null || y == null)
				return -1;
			var dupX = (DuplicateItemVM)x;
			var dupY = (DuplicateItemVM)y;
			long totalSizeX, totalSizeY;
			if (guidMap.ContainsKey(dupX.ItemInfo.GroupId)) {
				totalSizeX = guidMap[dupX.ItemInfo.GroupId];
			}
			else {
				totalSizeX = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupX.ItemInfo.GroupId).Sum(a => a.ItemInfo.SizeLong);
				guidMap[dupX.ItemInfo.GroupId] = totalSizeX;
			}
			if (guidMap.ContainsKey(dupY.ItemInfo.GroupId)) {
				totalSizeY = guidMap[dupY.ItemInfo.GroupId];
			}
			else {
				totalSizeY = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupY.ItemInfo.GroupId).Sum(a => a.ItemInfo.SizeLong);
				guidMap[dupY.ItemInfo.GroupId] = totalSizeY;
			}
			return totalSizeX.CompareTo(totalSizeY);
		}
	}
	public sealed class GroupSizeComparer : System.Collections.IComparer {
			readonly MainWindowVM mainVM;
			private readonly Dictionary<Guid, int> guidMap = new();
			public GroupSizeComparer(MainWindowVM vm) => mainVM = vm;
			public int Compare(object? x, object? y) {
				if (x == null || y == null)
					return -1;
				var dupX = (DuplicateItemVM)x;
				var dupY = (DuplicateItemVM)y;
				int groupSizeX, groupSizeY;
				if (guidMap.ContainsKey(dupX.ItemInfo.GroupId)) {
					groupSizeX = guidMap[dupX.ItemInfo.GroupId];
				}
				else {
					groupSizeX = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupX.ItemInfo.GroupId).Count();
					guidMap[dupX.ItemInfo.GroupId] = groupSizeX;
				}
				if (guidMap.ContainsKey(dupY.ItemInfo.GroupId)) {
					groupSizeY = guidMap[dupY.ItemInfo.GroupId];
				}
				else {
					groupSizeY = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupY.ItemInfo.GroupId).Count();
					guidMap[dupY.ItemInfo.GroupId] = groupSizeY;
				}
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

		bool _FilterGroupsWithCheckedItems;
		public bool FilterGroupsWithCheckedItems {
			get => _FilterGroupsWithCheckedItems;
			set {
				if (value == _FilterGroupsWithCheckedItems) return;
				this.RaiseAndSetIfChanged(ref _FilterGroupsWithCheckedItems, value);
				view?.Refresh();
			}
		}

		bool _IsFilterEnabled;
		public bool IsFilterEnabled {
			get => _IsFilterEnabled;
			set {
				if (value == _IsFilterEnabled) return;
				this.RaiseAndSetIfChanged(ref _IsFilterEnabled, value);
				view?.Refresh();
			}
		}

		private HashSet<Guid> _groupsWithPathHit = new();
		void RebuildSearchPathIndex() {
			var needle = FilterByPath;
			if (string.IsNullOrEmpty(needle)) { _groupsWithPathHit.Clear(); return; }

			_groupsWithPathHit = Duplicates
				.Where(d => PathMatchesFilter(d.ItemInfo.Path, needle))
				.Select(d => d.ItemInfo.GroupId)
				.ToHashSet();
		}

		/// <summary>
		/// Substring match by default; when the needle contains * or ? it is treated
		/// as a wildcard pattern instead (unanchored, so "*season?\ep*" works without
		/// the user having to wrap it in stars themselves).
		/// </summary>
		internal static bool PathMatchesFilter(string path, string needle) {
			if (needle.IndexOfAny(['*', '?']) < 0)
				return path.Contains(needle, StringComparison.OrdinalIgnoreCase);
			string pattern = needle;
			if (!pattern.StartsWith('*')) pattern = "*" + pattern;
			if (!pattern.EndsWith('*')) pattern += "*";
			return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, path);
		}

		string _FilterByPath = string.Empty;
		public string FilterByPath {
			get => _FilterByPath;
			set {
				if (value == _FilterByPath) return;
				_FilterByPath = value;
				this.RaisePropertyChanged(nameof(FilterByPath));
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
			if (!IsFilterEnabled) {
				data.IsVisibleInFilter = true;
				return true;
			}
			bool ok = true;
			if (!string.IsNullOrEmpty(FilterByPath)) {
				ok = PathMatchesFilter(data.ItemInfo.Path, FilterByPath)
					 || _groupsWithPathHit.Contains(data.ItemInfo.GroupId);
			}

			if (ok && FileType.Value != FileTypeFilter.All)
				ok = FileType.Value == FileTypeFilter.Images ? data.ItemInfo.IsImage : !data.ItemInfo.IsImage;

			if (ok)
				ok = data.ItemInfo.Similarity >= FilterSimilarityFrom && data.ItemInfo.Similarity <= FilterSimilarityTo;

			if (ok && FilterGroupsWithCheckedItems)
				ok = GroupHasCheckedItems(data.ItemInfo.GroupId);

			data.IsVisibleInFilter = ok;
			return ok;
		}
	}
}
