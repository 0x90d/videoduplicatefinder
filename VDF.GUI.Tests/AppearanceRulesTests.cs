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
using VDF.GUI.Utils;

namespace VDF.GUI.Tests;

/// <summary>The rules by which the app's look follows the operating system.</summary>
public class AppearanceRulesTests {

	[Theory]
	[InlineData(0, 1.5, 1.5)]    // nothing chosen: the system's text size
	[InlineData(0, null, 1.0)]   // and where the system has none, 100 percent
	[InlineData(125, 1.5, 1.25)] // a chosen percentage wins in both directions
	[InlineData(200, 1.0, 2.0)]
	[InlineData(100, 2.25, 1.0)]
	[InlineData(0, 9.0, 3.0)]    // nonsense from the system does not blow the window up
	[InlineData(0, 0.1, 0.5)]
	public void Scale_FollowsTheSystem_UnlessAPercentageWasChosen(int setting, double? system, double expected) =>
		Assert.Equal(expected, Appearance.ResolveScale(setting, system));

	[Theory]
	[InlineData(100, 1.0)]
	[InlineData(225, 2.25)] // the top of Windows' text size slider
	[InlineData(0, null)]
	[InlineData(-5, null)]
	[InlineData(100000, null)]
	public void WindowsTextSize_IsAPercentage(int registryValue, double? expected) =>
		Assert.Equal(expected, SystemPreferences.FromPercent(registryValue));

	[Theory]
	[InlineData("1.25\n", 1.25)]
	[InlineData("1.0", 1.0)]
	[InlineData("2", 2.0)]
	[InlineData("1,25", null)]      // gsettings never prints a decimal comma: not an answer
	[InlineData("uint32 3", null)]
	[InlineData("", null)]
	[InlineData(null, null)]
	[InlineData("40", null)]
	public void GnomeTextScaling_IsABareNumber(string? output, double? expected) =>
		Assert.Equal(expected, SystemPreferences.ParseFactor(output));

	[Theory]
	[InlineData("true\n", true)]
	[InlineData("false", false)]
	[InlineData("'true'", null)]
	[InlineData("", null)]
	[InlineData(null, null)]
	public void GnomeSwitches_AreTrueOrFalse(string? output, bool? expected) =>
		Assert.Equal(expected, SystemPreferences.ParseBool(output));

	[Theory]
	[InlineData(0, 0)]
	[InlineData(-20, 0)]   // anything not a percentage means "follow the system"
	[InlineData(125, 125)]
	[InlineData(10, 50)]
	[InlineData(5000, 300)]
	public void ScaleSetting_StaysWithinWhatAWindowCanTake(int stored, int expected) =>
		Assert.Equal(expected, new SettingsFile { UiScalePercent = stored }.UiScalePercent);
}
