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

namespace VDF.GUI.Data {
	public static class KeyboardShortcutDefaults {
		public static readonly Dictionary<string, string> MainWindowDefaults = new() {
			["RenameFile"] = "F2",
			["ToggleCheckbox"] = "Space",
			["OpenItemsByColId"] = "Enter",
			["OpenItemInFolder"] = "Shift+Enter",
			["DeleteCheckedItemsWithPrompt"] = "Delete",
			["DeleteCheckedItems"] = "Shift+Delete",
			["DeleteHighlighted"] = "Ctrl+Delete",
			["ShowGroupInThumbnailComparer"] = "Alt+C",
			["MarkGroupAsNotAMatch"] = "Alt+X",
			["CopyCheckedItems"] = "",
			["MoveCheckedItems"] = "",
			["CheckLowestQuality"] = "",
			["ClearCheckedItems"] = "",
			["InvertCheckedItems"] = "",
			["ExpandAllGroups"] = "",
			["CollapseAllGroups"] = "",
			["RemoveCheckedItemsFromList"] = "",
			["NavigateNextGroup"] = "Ctrl+Down",
			["NavigatePreviousGroup"] = "Ctrl+Up",
		};

		public static readonly Dictionary<string, string> DatabaseViewerDefaults = new() {
			["DB_DeleteSelectedEntries"] = "Delete",
		};

		public static readonly HashSet<string> AllActionIds = new(
			MainWindowDefaults.Keys.Concat(DatabaseViewerDefaults.Keys)
		);
	}
}
