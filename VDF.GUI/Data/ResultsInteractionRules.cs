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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

namespace VDF.GUI.Data {
	/// <summary>Pointer-interaction rules of the results list, extracted for tests.</summary>
	internal static class ResultsInteractionRules {
		/// <summary>
		/// Click on the path line copies the full path (locked design decision 5) — but
		/// only when the row was ALREADY selected when the click landed. The path line is
		/// the row's largest click target, so the first, merely row-selecting click must
		/// never touch the clipboard: users lost clipboard content to plain row selection
		/// and pasted stale paths into rename dialogs without noticing (#849).
		/// </summary>
		internal static bool ShouldCopyPathOnPointerPress(bool isLeftButton, bool rowWasAlreadySelected) =>
			isLeftButton && rowWasAlreadySelected;
	}
}
