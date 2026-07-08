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

using System.Collections.Generic;
using System.Linq;

namespace VDF.GUI.Data {

	/// <summary>
	/// Maps a stored setting value to the option object a ComboBox needs for its selection.
	///
	/// A ComboBox setting MUST bind to <c>SelectedItem</c> (via a view-model property backed
	/// by these helpers), never to <c>SelectedValue</c>: when the selection changes Avalonia
	/// writes <c>SelectedValue</c> with <c>SetCurrentValue</c>, which updates the effective
	/// value "without changing its value source" — so a <c>SelectedValue</c> TwoWay binding
	/// never writes back to the bound setting and the choice silently fails to persist
	/// (issue #829). <c>SelectedItem</c> is raised normally and does write back.
	/// </summary>
	public static class SettingsCombo {

		/// <summary>The option whose Value equals the stored action, or null if none match.</summary>
		public static ThumbnailDoubleClickOption? OptionFor(
				IEnumerable<ThumbnailDoubleClickOption> options, ThumbnailDoubleClickAction stored) =>
			options.FirstOrDefault(o => o.Value == stored);

		/// <summary>The preset carrying the stored name, or null (shows the placeholder) if none match.</summary>
		public static CustomSelectionPreset? PresetFor(
				IEnumerable<CustomSelectionPreset> presets, string storedName) =>
			string.IsNullOrEmpty(storedName)
				? null
				: presets.FirstOrDefault(p => string.Equals(p.Name, storedName, System.StringComparison.Ordinal));
	}
}
