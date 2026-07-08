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

using VDF.GUI.Data;

namespace VDF.GUI.Tests;

// Settings ComboBoxes bind SelectedItem to a view-model property backed by these mappers
// (SelectedValue silently fails to persist — issue #829). The mapper must return the very
// instance held in the ItemsSource, or the ComboBox can't show it as selected.
public class SettingsComboTests {

	static ThumbnailDoubleClickOption[] DoubleClickOptions() => new[] {
		new ThumbnailDoubleClickOption("Open file", ThumbnailDoubleClickAction.OpenFile),
		new ThumbnailDoubleClickOption("Open comparer", ThumbnailDoubleClickAction.OpenThumbnailComparer),
	};

	[Fact]
	public void OptionFor_ReturnsTheStoredActionsOwnOptionInstance() {
		var options = DoubleClickOptions();
		Assert.Same(options[0], SettingsCombo.OptionFor(options, ThumbnailDoubleClickAction.OpenFile));
		Assert.Same(options[1], SettingsCombo.OptionFor(options, ThumbnailDoubleClickAction.OpenThumbnailComparer));
	}

	[Fact]
	public void PresetFor_FindsByName() {
		var presets = new[] {
			new CustomSelectionPreset { Name = "Keep newest" },
			new CustomSelectionPreset { Name = "Keep largest" },
		};
		Assert.Same(presets[1], SettingsCombo.PresetFor(presets, "Keep largest"));
	}

	[Theory]
	[InlineData("")]        // nothing configured -> show the placeholder, not a stale pick
	[InlineData("Deleted")] // a preset that was since removed -> placeholder
	public void PresetFor_ReturnsNullForEmptyOrMissingName(string storedName) {
		var presets = new[] { new CustomSelectionPreset { Name = "Keep newest" } };
		Assert.Null(SettingsCombo.PresetFor(presets, storedName));
	}
}
