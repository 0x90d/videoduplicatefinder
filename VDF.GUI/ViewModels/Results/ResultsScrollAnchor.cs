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

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// Every results rebuild replaces all row objects, so the ScrollViewer's pixel offset
	/// afterwards points at unrelated content — deleting a few groups used to dump the
	/// user at a random list position. This maps the pre-rebuild anchor (whatever row was
	/// topmost in the viewport) to the row of the NEW flattened list the view should
	/// scroll back to. Pure logic; the view captures and consumes the rows.
	/// </summary>
	public static class ResultsScrollAnchor {

		/// <param name="anchor">Topmost visible row before the rebuild (header, item or details row), or null.</param>
		/// <param name="oldGroupOrder">Group ids in the PREVIOUS build's display order.</param>
		/// <param name="newRows">The flattened rows of the new build.</param>
		/// <returns>The row to scroll to the top of the viewport, or null to leave the scroll alone.</returns>
		public static object? FindRestoreTarget(object? anchor, IReadOnlyList<Guid> oldGroupOrder, IReadOnlyList<object> newRows) {
			if (anchor == null || newRows.Count == 0) return null;

			DuplicateItemVM? item = anchor switch {
				ResultsItemRow r => r.Item,
				ResultsDetailsRow d => d.Item,
				_ => null,
			};
			Guid groupId = anchor switch {
				ResultsGroupHeader h => h.GroupId,
				ResultsItemRow r => r.Item.ItemInfo.GroupId,
				ResultsDetailsRow d => d.Item.ItemInfo.GroupId,
				_ => Guid.Empty,
			};

			var headers = new Dictionary<Guid, ResultsGroupHeader>();
			foreach (var row in newRows) {
				if (item != null && row is ResultsItemRow ir && ReferenceEquals(ir.Item, item))
					return ir; // the exact item survived (also holds across re-sort/filter)
				if (row is ResultsGroupHeader h)
					headers.TryAdd(h.GroupId, h);
			}

			// The item is gone (deleted, filtered out, or its group collapsed) — its group.
			if (headers.TryGetValue(groupId, out var sameGroup))
				return sameGroup;

			// The whole group is gone — nearest surviving neighbor in the OLD display
			// order, preferring the one after it: after deleting the groups you just
			// worked through, you continue right where they ended.
			int oldIndex = -1;
			for (int i = 0; i < oldGroupOrder.Count; i++)
				if (oldGroupOrder[i] == groupId) { oldIndex = i; break; }
			if (oldIndex >= 0) {
				for (int i = oldIndex + 1; i < oldGroupOrder.Count; i++)
					if (headers.TryGetValue(oldGroupOrder[i], out var after)) return after;
				for (int i = oldIndex - 1; i >= 0; i--)
					if (headers.TryGetValue(oldGroupOrder[i], out var before)) return before;
			}
			return null;
		}
	}
}
