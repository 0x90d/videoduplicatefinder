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
	/// Every results rebuild replaces all row objects, and the row that had keyboard focus
	/// went with them: after deleting, collapsing a group or any filter change, focus was
	/// nowhere, and a keyboard or screen reader user had to find the list again from the
	/// top of the window. This picks the row of the NEW list that takes the focus over: the
	/// same file or group header when it is still listed, else whatever now stands where the
	/// focused row stood, so the user continues from the same place. Pure logic; the view
	/// captures the focused row and moves the focus.
	/// </summary>
	public static class ResultsFocusKeeper {
		public static object? FindFocusTarget(object? focusedRow, int focusedIndex, IReadOnlyList<object> newRows) {
			if (focusedRow == null || newRows.Count == 0) return null;

			foreach (var row in newRows) {
				bool same = (focusedRow, row) switch {
					(ResultsItemRow a, ResultsItemRow b) => ReferenceEquals(a.Item, b.Item),
					(ResultsDetailsRow a, ResultsDetailsRow b) => ReferenceEquals(a.Item, b.Item),
					(ResultsGroupHeader a, ResultsGroupHeader b) => a.GroupId == b.GroupId,
					_ => false,
				};
				if (same) return row;
			}
			// A details panel that closed hands the focus back to its file.
			if (focusedRow is ResultsDetailsRow details)
				foreach (var row in newRows)
					if (row is ResultsItemRow r && ReferenceEquals(r.Item, details.Item))
						return r;

			return focusedIndex < 0 ? null : newRows[Math.Min(focusedIndex, newRows.Count - 1)];
		}
	}
}
