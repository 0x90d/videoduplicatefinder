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

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace VDF.GUI.Utils {
	static class TreeHelper {

		static List<T> GetVisualTreeObjects<T>(this Control obj) where T : Control {
			var objects = new List<T>();
			foreach (Control child in obj.GetVisualChildren()) {
				if (child is T requestedType)
					objects.Add(requestedType);
				objects.AddRange(child.GetVisualTreeObjects<T>());
			}
			return objects;
		}
		public static void ToggleExpander(this DataGrid datagrid, bool expand) {
			List<DataGridRowGroupHeader> groupHeaderList = GetVisualTreeObjects<DataGridRowGroupHeader>(datagrid);
			if (groupHeaderList.Count == 0) return;
			foreach (DataGridRowGroupHeader header in groupHeaderList) {
				foreach (var e in GetVisualTreeObjects<ToggleButton>(header))
					e.IsChecked = expand;
			}
		}
	}
}
