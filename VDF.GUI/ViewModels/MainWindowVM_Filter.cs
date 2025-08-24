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

using System.Linq;
using Avalonia.Collections;
using Avalonia.Threading;
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {
		public KeyValuePair<string, Comparison<RowNode>>[] SortOrders { get; private set; }
		private static int CompareLeafBy<T>(RowNode a, RowNode b, Func<DuplicateItemVM, T> key, bool desc = false) where T : IComparable<T> {
			if (a.IsGroup || b.IsGroup) return 0;

			var va = key(a.Item!);
			var vb = key(b.Item!);
			var c = va.CompareTo(vb);
			return desc ? -c : c;
		}

		private static int CompareGroupsByInt(RowNode a, RowNode b, Func<RowNode, int> key, bool desc = false) {
			if (!a.IsGroup || !b.IsGroup) return 0;

			var c = key(a).CompareTo(key(b));
			return desc ? -c : c;
		}

		private static bool GroupHasChecked(RowNode g) {
			var kids = g.Children;
			for (int i = 0; i < kids.Count; i++)
				if (kids[i].Item is { Checked: true }) return true;
			return false;
		}
		public sealed class CheckedGroupsComparer : System.Collections.IComparer {
			readonly MainWindowVM mainVM;
			readonly Dictionary<Guid, bool> _hasChecked = new();
			public CheckedGroupsComparer(MainWindowVM vm) => mainVM = vm;
			public int Compare(object? x, object? y) {
				if (x is not DuplicateItemVM dx || y is not DuplicateItemVM dy) return -1;

				bool X() => _hasChecked.TryGetValue(dx.ItemInfo.GroupId, out var b)
					? b
					: (_hasChecked[dx.ItemInfo.GroupId] =
						mainVM.EnumerateItemsInGroup(dx.ItemInfo.GroupId).Any(a => a.Checked));

				bool Y() => dx.ItemInfo.GroupId == dy.ItemInfo.GroupId
							? X()
							: (_hasChecked.TryGetValue(dy.ItemInfo.GroupId, out var b)
							   ? b
							   : (_hasChecked[dy.ItemInfo.GroupId] =
								   mainVM.EnumerateItemsInGroup( dy.ItemInfo.GroupId).Any(a => a.Checked)));

				return X().CompareTo(Y());
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
					groupSizeX = mainVM.EnumerateItemsInGroup(dupX.ItemInfo.GroupId).Count();
					guidMap[dupX.ItemInfo.GroupId] = groupSizeX;
				}
				if (guidMap.ContainsKey(dupY.ItemInfo.GroupId)) {
					groupSizeY = guidMap[dupY.ItemInfo.GroupId];
				}
				else {
					groupSizeY = mainVM.EnumerateItemsInGroup(dupY.ItemInfo.GroupId).Count();
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
				RefreshGroupStats();
			}
		}
		KeyValuePair<string, Comparison<RowNode>> _SortOrder;

		public KeyValuePair<string, Comparison<RowNode>> SortOrder {
			get => _SortOrder;
			set {
				if (value.Key == _SortOrder.Key) return;
				_SortOrder = value;
				TreeSource.Sort(_SortOrder.Value);
				this.RaisePropertyChanged(nameof(SortOrder));
			}
		}

		private HashSet<Guid> _groupsWithPathHit = new();
		void RebuildSearchPathIndex() {
			var needle = FilterByPath;
			if (string.IsNullOrEmpty(needle)) { _groupsWithPathHit.Clear(); return; }

			_groupsWithPathHit = EnumerateAllItems()
				.Where(d => d.ItemInfo.Path.Contains(needle, StringComparison.OrdinalIgnoreCase))
				.Select(d => d.ItemInfo.GroupId)
				.ToHashSet();
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
				ApplyFilter();
				RefreshGroupStats();
			}
		}
		int _FilterSimilarityTo = 100;
		public int FilterSimilarityTo {
			get => _FilterSimilarityTo;
			set {
				if (value == _FilterSimilarityTo) return;
				this.RaiseAndSetIfChanged(ref _FilterSimilarityTo, value);
				ApplyFilter();
				RefreshGroupStats();
			}
		}

		bool Matches(DuplicateItemVM data) {
			bool ok = true;
			if (!string.IsNullOrEmpty(FilterByPath)) {
				ok = data.ItemInfo.Path.Contains(FilterByPath, StringComparison.OrdinalIgnoreCase)
					 || _groupsWithPathHit.Contains(data.ItemInfo.GroupId);
			}

			if (ok && FileType.Value != FileTypeFilter.All)
				ok = FileType.Value == FileTypeFilter.Images ? data.ItemInfo.IsImage : !data.ItemInfo.IsImage;

			if (ok)
				ok = data.ItemInfo.Similarity >= FilterSimilarityFrom && data.ItemInfo.Similarity <= FilterSimilarityTo;

			data.IsVisibleInFilter = ok;
			return ok;
		}

		public void ApplyFilter() {
			if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ApplyFilter); return; }

			RebuildSearchPathIndex();

			// Set visible children per group
			foreach (var g in _allGroups) {
				g.Children.Clear();
				foreach (var leaf in g.AllChildren)
					if (leaf.Item is { } vm && Matches(vm))
						g.Children.Add(leaf);

				UpdateGroupHeader(g);
			}

			// Remove what is empty
			foreach (var g in Duplicates.ToList())
				if (g.Children.Count == 0)
					Duplicates.Remove(g);

			// Insert what should be visible
			foreach (var g in _allGroups)
				if (g.Children.Count > 0 && !Duplicates.Contains(g)) {
					// Insert at old position (according to _allGroups order)
					var idx = 0;
					while (idx < Duplicates.Count && _allGroups.IndexOf(Duplicates[idx]) < _allGroups.IndexOf(g))
						idx++;
					Duplicates.Insert(idx, g);
				}

		}
	}
}
