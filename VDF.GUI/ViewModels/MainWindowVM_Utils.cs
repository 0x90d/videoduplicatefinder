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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM {

		static StackPanel MultiHeader(params string[] lines) {
			var sp = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
			foreach (var s in lines)
				sp.Children.Add(new TextBlock { Text = s });
			return sp;
		}
		IEnumerable<DuplicateItemVM> EnumerateAllItems() {
			for (int gi = 0; gi < _allGroups.Count; gi++) {
				var g = _allGroups[gi];
				if (!g.IsGroup) continue;
				var children = g.AllChildren;
				for (int ci = 0; ci < children.Count; ci++) {
					var itemVm = children[ci].Item;
					if (itemVm != null)
						yield return itemVm;
				}
			}
		}
		IEnumerable<DuplicateItemVM> EnumerateItemsInGroup(Guid gid) {
			if (_groupIndex.TryGetValue(gid, out var grp)) {
				var children = grp.AllChildren;
				for (int i = 0; i < children.Count; i++) {
					var vm = children[i].Item;
					if (vm != null)
						yield return vm;
				}
				yield break;
			}

			for (int gi = 0; gi < _allGroups.Count; gi++) {
				var g = _allGroups[gi];
				if (!g.IsGroup) continue;

				var children = g.AllChildren;
				bool hit = false;
				for (int ci = 0; ci < children.Count; ci++) {
					var vm = children[ci].Item;
					if (vm != null && vm.ItemInfo.GroupId == gid) { hit = true; break; }
				}
				if (!hit) continue;

				for (int ci = 0; ci < children.Count; ci++) {
					var vm = children[ci].Item;
					if (vm != null && vm.ItemInfo.GroupId == gid)
						yield return vm;
				}
				yield break;
			}
		}
		public void RemovePathsFromGroup(Guid gid, IReadOnlyCollection<string> paths) {
			if (paths == null || paths.Count == 0) return;

			// Get group from index otherwise scan allgroups
			RowNode? grp = null;
			if (!_groupIndex.TryGetValue(gid, out grp) || grp is null) {
				for (int gi = 0; gi < _allGroups.Count; gi++) {
					var g = _allGroups[gi];
					var kids = g.AllChildren;
					for (int ci = 0; ci < kids.Count; ci++) {
						var vm = kids[ci].Item;
						if (vm != null && vm.ItemInfo.GroupId == gid) { grp = g; break; }
					}
					if (grp != null) break;
				}
			}
			if (grp is null) return;

			// Build selection from leafs
			var sel = new List<RowNode>(grp.AllChildren.Count);
			var all = grp.AllChildren;
			for (int i = 0; i < all.Count; i++) {
				var vm = all[i].Item;
				if (vm != null && paths.Contains(vm.ItemInfo.Path))
					sel.Add(all[i]);
			}

			if (sel.Count > 0)
				RemoveSelectionFromTree(sel);
		}
		public void RemoveSelectionFromTree(IReadOnlyList<RowNode?>? selected) {
			if (selected is null || selected.Count == 0) return;

			// Remove leafs
			for (int si = 0; si < selected.Count; si++) {
				var leaf = selected[si];
				if (leaf == null || leaf.IsGroup) continue;

				// find parent group in _allGroups
				RowNode? parent = null;
				for (int gi = 0; gi < _allGroups.Count; gi++) {
					var g = _allGroups[gi];
					if (g.AllChildren.Contains(leaf)) { parent = g; break; }
				}
				if (parent is null) continue;

				if (leaf.Item is { } vm) DetachChecked(vm);

				// remove from master and visible list
				var all = parent.AllChildren;
				for (int i = all.Count - 1; i >= 0; i--)
					if (ReferenceEquals(all[i], leaf)) { all.RemoveAt(i); break; }

				var vis = parent.Children;
				for (int i = vis.Count - 1; i >= 0; i--)
					if (ReferenceEquals(vis[i], leaf)) { vis.RemoveAt(i); break; }

				UpdateGroupHeader(parent);

				// if empty -> remove group everywhere (Master, visible, index)
				if (parent.AllChildren.Count == 0)
					RemoveGroupCompletely(parent);
			}

			// finally remove selected groups
			for (int si = 0; si < selected.Count; si++) {
				var grp = selected[si];
				if (grp == null || !grp.IsGroup) continue;
				RemoveGroupCompletely(grp);
			}

		}
		private void RemoveGroupCompletely(RowNode g) {
			foreach (var leaf in g.AllChildren)
				if (leaf.Item is { } vm) DetachChecked(vm);

			// master
			for (int i = _allGroups.Count - 1; i >= 0; i--)
				if (ReferenceEquals(_allGroups[i], g)) { _allGroups.RemoveAt(i); break; }

			// visible
			for (int i = Duplicates.Count - 1; i >= 0; i--)
				if (ReferenceEquals(Duplicates[i], g)) { Duplicates.RemoveAt(i); break; }

			// index
			Guid? keyToRemove = null;
			foreach (var kv in _groupIndex)
				if (ReferenceEquals(kv.Value, g)) { keyToRemove = kv.Key; break; }
			if (keyToRemove.HasValue) _groupIndex.Remove(keyToRemove.Value);
		}
		private void ApplyDeletionsAndDropSingles(IReadOnlySet<DuplicateItemVM> actuallyDeleted) {
			for (int gi = _allGroups.Count - 1; gi >= 0; gi--) {
				var g = _allGroups[gi];

				// 1) Remove affected leaves – Master (AllChildren)
				var all = g.AllChildren;
				for (int i = all.Count - 1; i >= 0; i--) {
					var leaf = all[i];
					var vm = leaf.Item;
					if (vm != null && actuallyDeleted.Contains(vm)) {
						all.RemoveAt(i);

						// Also remove from visible children (by reference)
						var vis = g.Children;
						for (int j = vis.Count - 1; j >= 0; j--)
							if (ReferenceEquals(vis[j], leaf)) { vis.RemoveAt(j); break; }
					}
				}

				// 2) Groups with <= 1 element: remove completely
				if (g.AllChildren.Count <= 1) {
					RemoveGroupCompletely(g);
					continue;
				}

				// 3) Group remains: Update header
				UpdateGroupHeader(g);
			}
		}
		private static void UpdateGroupHeader(RowNode g) => g.Header = $"{g.Header.AsSpan().Slice(0, g.Header.LastIndexOf('('))}({g.Children.Count})";

		static bool HasTieOn(string lastCriterion, List<DuplicateItemVM> list, DuplicateItemVM keep) => lastCriterion switch {
			"Duration" => list.Count(d => d.ItemInfo.Duration == keep.ItemInfo.Duration) > 1,
			"Resolution" => list.Count(d => d.ItemInfo.FrameSizeInt == keep.ItemInfo.FrameSizeInt) > 1,
			"FPS" => list.Count(d => d.ItemInfo.Fps == keep.ItemInfo.Fps) > 1,
			"Bitrate" => list.Count(d => d.ItemInfo.BitRateKbs == keep.ItemInfo.BitRateKbs) > 1,
			"Audio Bitrate" => list.Count(d => d.ItemInfo.AudioSampleRate == keep.ItemInfo.AudioSampleRate) > 1,
			_ => false
		};
		static DuplicateItemVM ApplyCriterion(string criterion, List<DuplicateItemVM> list) => criterion switch {
			"Duration" => list.OrderByDescending(d => d.ItemInfo.Duration).First(),
			"Resolution" => list.OrderByDescending(d => d.ItemInfo.FrameSizeInt).First(),
			"FPS" => list.OrderByDescending(d => d.ItemInfo.Fps).First(),
			"Bitrate" => list.OrderByDescending(d => d.ItemInfo.BitRateKbs).First(),
			"Audio Bitrate" => list.OrderByDescending(d => d.ItemInfo.AudioSampleRate).First(),
			_ => list[0]
		};
	}
	sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class {
		public static readonly ReferenceEqualityComparer<T> Instance = new();
		public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
		public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
	}
}
