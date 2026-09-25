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

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VDF.GUI.Utils;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// #906: a switch that is on looked like one that is off, apart from the knob's side.
/// On is now filled with the accent, in every theme, and its knob stays visible on the fill.
/// </summary>
public class SwitchStateTests {
	public static TheoryData<string> Themes() => new() { "Light", "Dark", "HighContrastLight", "HighContrastDark" };

	static ThemeVariant Variant(string name) => name switch {
		"Light" => ThemeVariant.Light,
		"Dark" => ThemeVariant.Dark,
		_ => ContrastTests.HighContrastVariant(name),
	};

	static (Color Track, Color Knob) Colors(ToggleSwitch toggle) {
		var track = toggle.GetVisualDescendants().OfType<Border>().First(b => b.Name == "border");
		var knob = toggle.GetVisualDescendants().OfType<ContentPresenter>().First(c => c.Name == "glyph");
		return (((ISolidColorBrush)track.Background!).Color, ((ISolidColorBrush)knob.Foreground!).Color);
	}

	[Theory]
	[MemberData(nameof(Themes))]
	public Task OnIsFilled_AndItsKnobStandsOut(string theme) => HeadlessUi.Run(() => {
		HeadlessUi.Shell();
		var on = new ToggleSwitch { IsChecked = true, Classes = { "plain" } };
		var off = new ToggleSwitch { IsChecked = false, Classes = { "plain" } };
		var window = new Window { Width = 300, Height = 200, RequestedThemeVariant = Variant(theme), Content = new StackPanel { Children = { on, off } } };
		window.Show();
		HeadlessUi.Pump();
		try {
			var (onTrack, onKnob) = Colors(on);
			var (offTrack, _) = Colors(off);

			Assert.NotEqual(offTrack, onTrack);
			// WCAG 1.4.11: the knob is what shows the state, 3:1 against what it sits on.
			double knobContrast = ContrastTests.Ratio(onKnob, onTrack);
			Assert.True(knobContrast >= 3.0, $"knob {onKnob} on track {onTrack}: {knobContrast:0.00}:1 in {theme}");
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task DisabledSwitches_KeepTheThemesLook() => HeadlessUi.Run(() => {
		HeadlessUi.Shell();
		var disabledOn = new ToggleSwitch { IsChecked = true, IsEnabled = false };
		var disabledOff = new ToggleSwitch { IsChecked = false, IsEnabled = false };
		var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { disabledOn, disabledOff } } };
		window.Show();
		HeadlessUi.Pump();
		try {
			Assert.Equal(Colors(disabledOff).Track, Colors(disabledOn).Track);
		}
		finally {
			window.Close();
		}
	});
}
