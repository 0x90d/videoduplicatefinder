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
		private readonly Dictionary<(Guid gid, Type kind), object?> _groupKeyCache = new();
		private void InvalidateGroupCaches() {
			_groupKeyCache.Clear();
			_groupNodeToGuid.Clear();
		}
		private readonly Dictionary<RowNode, Guid> _groupNodeToGuid =
			new(RowNodeReferenceComparer.Instance);
		public KeyValuePair<string, Comparison<RowNode>>[] SortOrders { get; private set; }
		//private static int CompareLeafBy<T>(RowNode a, RowNode b, Func<DuplicateItemVM, T> key, bool desc = false) where T : IComparable<T> {
		//	if (a.IsGroup || b.IsGroup) return 0;

		//	var va = key(a.Item!);
		//	var vb = key(b.Item!);
		//	var c = va.CompareTo(vb);
		//	return desc ? -c : c;
		//}

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
								   mainVM.EnumerateItemsInGroup(dy.ItemInfo.GroupId).Any(a => a.Checked)));

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
				ApplyFilter();
				RefreshGroupStats();
			}
		}
		KeyValuePair<string, Comparison<RowNode>> _SortOrder;

		public KeyValuePair<string, Comparison<RowNode>> SortOrder {
			get => _SortOrder;
			set {
				if (value.Key == _SortOrder.Key) return;
				_SortOrder = value;
				InvalidateGroupCaches();
				TreeSource.Sort(_SortOrder.Value);
				this.RaisePropertyChanged(nameof(SortOrder));
			}
		}
		bool _FilterGroupsWithSelection;
		public bool FilterGroupsWithSelection {
			get => _FilterGroupsWithSelection;
			set {
				if (value == _FilterGroupsWithSelection) return;
				this.RaiseAndSetIfChanged(ref _FilterGroupsWithSelection, value);
				ApplyFilter();
				RefreshGroupStats();
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
			using (Dispatcher.UIThread.DisableProcessing()) {
				// Set visible children per group
				foreach (var g in _allGroups) {
					bool hasSelection = false;
					var newKids = new List<RowNode>(g.AllChildren.Count);
					foreach (var leaf in g.AllChildren)
						if (leaf.Item is { } vm && Matches(vm)) {
							newKids.Add(leaf);
							if (FilterGroupsWithSelection && vm.Checked)
								hasSelection = true;
						}
					if (FilterGroupsWithSelection && !hasSelection)
						newKids.Clear();
					g.Children.Clear();
					if (newKids.Count > 0)
						g.Children.AddRange(newKids);
					UpdateGroupHeader(g);
				}

				// Remove what is empty
				var newGroups = new List<RowNode>(_allGroups.Count);
				foreach (var g in _allGroups)
					if (g.Children.Count > 0)
						newGroups.Add(g);

				Duplicates.Clear();
				if (newGroups.Count > 0)
					Duplicates.AddRange(newGroups);

			}
			InvalidateGroupCaches();
		}
		private sealed class RowNodeReferenceComparer : IEqualityComparer<RowNode> {
			public static readonly RowNodeReferenceComparer Instance = new();
			private RowNodeReferenceComparer() { }
			public bool Equals(RowNode? x, RowNode? y) => ReferenceEquals(x, y);
			public int GetHashCode(RowNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
		}
		private Guid GetGroupId(RowNode n) {
			if (!n.IsGroup)
				return n.Item!.ItemInfo.GroupId;

			if (_groupNodeToGuid.TryGetValue(n, out var gid))
				return gid;

			// Find first leaf in AllChildren to read its GroupId
			for (int i = 0; i < n.AllChildren.Count; i++) {
				var c = n.AllChildren[i];
				if (!c.IsGroup && c.Item is not null) {
					gid = c.Item.ItemInfo.GroupId;
					_groupNodeToGuid[n] = gid;
					return gid;
				}
			}
			// Empty group (should be rare / usually filtered out)
			_groupNodeToGuid[n] = Guid.Empty;
			return Guid.Empty;
		}
		private int CompareBy<T>(RowNode a, RowNode b, Func<DuplicateItemVM, T> key, bool desc = false, Func<IEnumerable<T>, T>? groupAggregate = null) where T : IComparable<T> {
			// Default aggregator: Max
			groupAggregate ??= static values => {
				using var e = values.GetEnumerator();
				if (!e.MoveNext()) return default!;
				T best = e.Current;
				var cmp = Comparer<T>.Default;
				while (e.MoveNext())
					if (cmp.Compare(e.Current, best) > 0)
						best = e.Current;
				return best;
			};

			T KeyForNode(RowNode n, out Guid groupIdOfNode) {
				if (!n.IsGroup) {
					groupIdOfNode = n.Item!.ItemInfo.GroupId;
					return key(n.Item!);
				}

				groupIdOfNode = GetGroupId(n);

				// Lookup aggregate per group Guid
				if (!_groupKeyCache.TryGetValue((groupIdOfNode, typeof(T)), out var boxed)) {
					// Aggregate over currently visible children
					var kids = n.Children;

					var list = new List<T>(kids.Count);
					for (int i = 0; i < kids.Count; i++) {
						var c = kids[i];
						if (!c.IsGroup && c.Item is not null)
							list.Add(key(c.Item));
					}
					var agg = groupAggregate(list);
					_groupKeyCache[(groupIdOfNode, typeof(T))] = agg!;
					return agg!;
				}
				return (T)boxed!;
			}

			var ka = KeyForNode(a, out var ga);
			var kb = KeyForNode(b, out var gb);

			int c = ka.CompareTo(kb);
			if (c != 0) return desc ? -c : c;

			// --- Stable tie-breakers ---

			// If both are leaves and belong to the same group, keep insertion order (return 0).
			if (!a.IsGroup && !b.IsGroup && ga == gb)
				return 0;

			// Prefer groups to come before their children when equal (or flip if you like)
			if (a.IsGroup != b.IsGroup)
				return a.IsGroup ? -1 : 1;

			// Fallback to path to avoid flicker for identical values across different groups/leaves
			if (!a.IsGroup && !b.IsGroup) {
				string pa = a.Item!.ItemInfo.Path;
				string pb = b.Item!.ItemInfo.Path;
				int pc = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
				if (pc != 0) return pc;
			}
			// As a last resort, compare group ids to ensure deterministic total ordering
			if (ga != gb)
				return ga.CompareTo(gb);

			return 0;
		}
	}
}
