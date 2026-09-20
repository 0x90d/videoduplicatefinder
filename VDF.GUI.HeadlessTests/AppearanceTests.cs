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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VDF.GUI.Data;
using VDF.GUI.Utils;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// The app's look follows what the user told their operating system, and the settings only
/// override towards what a user asks for.
/// </summary>
public class AppearanceTests {

	[Theory]
	[InlineData(ThemeMode.System, PlatformThemeVariant.Dark, true)]
	[InlineData(ThemeMode.System, PlatformThemeVariant.Light, false)]
	[InlineData(ThemeMode.Dark, PlatformThemeVariant.Light, true)]
	[InlineData(ThemeMode.Light, PlatformThemeVariant.Dark, false)]
	public void Theme_FollowsTheSystem_UnlessTheUserChoseOne(ThemeMode mode, PlatformThemeVariant system, bool dark) =>
		Assert.Equal(dark, Appearance.IsDark(mode, system));

	static void WithThemeMode(Action body) {
		var before = SettingsFile.Instance.ThemeMode;
		try {
			body();
		}
		finally {
			SettingsFile.Instance.ThemeMode = before;
			HeadlessUi.Pump();
		}
	}

	[Fact]
	public Task Theme_TheSetting_SwitchesTheWholeAppAtOnce() => HeadlessUi.Run(() => WithThemeMode(() => {
		var (window, _) = HeadlessUi.Shell();

		SettingsFile.Instance.ThemeMode = ThemeMode.Light;
		HeadlessUi.Pump();
		Assert.Equal(ThemeVariant.Light, Application.Current!.ActualThemeVariant);
		Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);

		SettingsFile.Instance.ThemeMode = ThemeMode.Dark;
		HeadlessUi.Pump();
		Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
		Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
	}));

	[Fact]
	public Task Theme_AnOpenDialog_FollowsToo() => HeadlessUi.Run(() => WithThemeMode(() => {
		HeadlessUi.Shell();
		SettingsFile.Instance.ThemeMode = ThemeMode.Light;
		HeadlessUi.Pump();
		var dialog = new AboutWindow();
		dialog.Show();
		HeadlessUi.Pump();
		try {
			Assert.Equal(ThemeVariant.Light, dialog.ActualThemeVariant);

			// Every dialog used to pin itself to light at construction when dark mode was
			// off, and then stayed light whatever happened: with the system switching to
			// dark in the evening, that is a white window in a dark app.
			SettingsFile.Instance.ThemeMode = ThemeMode.Dark;
			HeadlessUi.Pump();
			Assert.Equal(ThemeVariant.Dark, dialog.ActualThemeVariant);
		}
		finally {
			dialog.Hide();
		}
	}));

	[Fact]
	public Task Theme_IsChosenInSettings_FromAComboOfThree() => HeadlessUi.Run(() => WithThemeMode(() => {
		var vm = new MainWindowVM();
		var window = HeadlessUi.Show(new SettingsView { DataContext = vm });
		try {
			var combo = window.GetLogicalDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.ThemeModeOptions));
			Assert.Equal(["Follow system", "Light", "Dark"], vm.ThemeModeOptions.Select(o => o.Name));

			combo.SelectedItem = vm.ThemeModeOptions.Single(o => o.Value == ThemeMode.Light);
			HeadlessUi.Pump();
			Assert.Equal(ThemeMode.Light, SettingsFile.Instance.ThemeMode);

			combo.SelectedItem = vm.ThemeModeOptions.Single(o => o.Value == ThemeMode.System);
			HeadlessUi.Pump();
			Assert.Equal(ThemeMode.System, SettingsFile.Instance.ThemeMode);
		}
		finally {
			window.Close();
		}
	}));

	static void WithScale(Action body) {
		int before = SettingsFile.Instance.UiScalePercent;
		try {
			body();
		}
		finally {
			SettingsFile.Instance.UiScalePercent = before;
			Appearance.SetSystemTextScale(null);
			HeadlessUi.Pump();
		}
	}

	static double ScaleOf(Control control) => control.TransformToVisual(TopLevel.GetTopLevel(control)!)!.Value.M11;

	[Fact]
	public Task Scale_TheSystemsTextSize_EnlargesEverythingInTheWindow() => HeadlessUi.Run(() => WithScale(() => {
		var (window, _) = HeadlessUi.Shell();
		SettingsFile.Instance.UiScalePercent = 0;
		Appearance.SetSystemTextScale(null);
		HeadlessUi.Pump();
		var scanButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ScanButton");
		double minWidth = window.MinWidth;
		Assert.Equal(1.0, ScaleOf(scanButton), 3);

		// Windows: Settings, Accessibility, Text size at 150 percent.
		Appearance.SetSystemTextScale(1.5);
		HeadlessUi.Pump();
		Assert.Equal(1.5, ScaleOf(scanButton), 3);
		Assert.True(window.MinWidth > minWidth, "the minimum size has to grow along, or the scaled content is cut off");

		// A percentage chosen in the settings wins over the system, in both directions.
		SettingsFile.Instance.UiScalePercent = 125;
		HeadlessUi.Pump();
		Assert.Equal(1.25, ScaleOf(scanButton), 3);

		// Still where a screen reader is sure to hear announcements from.
		Assert.Contains(scanButton.GetVisualAncestors(), a => a is Controls.AnnouncerHost);

		SettingsFile.Instance.UiScalePercent = 0;
		Appearance.SetSystemTextScale(null);
		HeadlessUi.Pump();
		Assert.Equal(1.0, ScaleOf(scanButton), 3);
		Assert.Equal(minWidth, window.MinWidth, 3);
	}));

	[Fact]
	public Task Scale_ADialogOpenedLater_IsScaledAndSizedToMatch() => HeadlessUi.Run(() => WithScale(() => {
		HeadlessUi.Shell();
		SettingsFile.Instance.UiScalePercent = 100;
		HeadlessUi.Pump();
		var normal = new QualityOrderDialog();
		double width = normal.Width, height = normal.Height;

		SettingsFile.Instance.UiScalePercent = 150;
		HeadlessUi.Pump();
		var dialog = new QualityOrderDialog();
		dialog.Show();
		HeadlessUi.Pump();
		try {
			var button = dialog.GetVisualDescendants().OfType<Button>().First();
			Assert.Equal(1.5, ScaleOf(button), 3);
			// Laid out for 100 percent, the window would cut off its own content.
			if (!double.IsNaN(width)) Assert.Equal(width * 1.5, dialog.Width, 1);
			if (!double.IsNaN(height)) Assert.Equal(height * 1.5, dialog.Height, 1);
		}
		finally {
			dialog.Hide();
		}
	}));

	[Fact]
	public Task Scale_MenusFollow_ThoughTheyAreWindowsOfTheirOwn() => HeadlessUi.Run(() => WithScale(() => {
		HeadlessUi.Shell();
		SettingsFile.Instance.UiScalePercent = 150;
		HeadlessUi.Pump();
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = new Window { Width = 1500, Height = 900, Content = new DuplicateResultsView { DataContext = vm } };
		Appearance.Attach(window);
		window.Show();
		HeadlessUi.Pump();
		var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
		var row = (ListBoxItem)list.ContainerFromIndex(1)!;
		var menu = row.GetVisualDescendants().OfType<Border>().First(b => b.ContextMenu != null).ContextMenu!;
		try {
			row.Focus();
			row.RaiseEvent(new ContextRequestedEventArgs());
			HeadlessUi.Pump();
			Assert.True(menu.IsOpen);

			// A popup takes the transform of what it belongs to only when told so; without
			// the Popup style the window is large and every menu in it stays small.
			Assert.Equal(1.5, ScaleOf(menu.ContainerFromIndex(0)!), 3);
		}
		finally {
			menu.Close();
			window.Close();
		}
	}));

	[Fact]
	public Task Scale_IsChosenInSettings() => HeadlessUi.Run(() => WithScale(() => {
		var vm = new MainWindowVM();
		var window = HeadlessUi.Show(new SettingsView { DataContext = vm });
		try {
			var combo = window.GetLogicalDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, vm.UiScaleOptions));
			Assert.Equal(["Follow system", "100 %", "110 %", "125 %", "150 %", "175 %", "200 %"], vm.UiScaleOptions.Select(o => o.Name));

			combo.SelectedItem = vm.UiScaleOptions.Single(o => o.Percent == 150);
			HeadlessUi.Pump();
			Assert.Equal(150, SettingsFile.Instance.UiScalePercent);
		}
		finally {
			window.Close();
		}
	}));

	[Theory]
	[InlineData(true, false, "Dark")]
	[InlineData(false, false, "Light")]
	[InlineData(true, true, "VdfHighContrastDark")]   // a version of dark and of light,
	[InlineData(false, true, "VdfHighContrastLight")] // not a third theme next to them
	public void HighContrast_IsAVersionOfTheThemeInUse(bool dark, bool highContrast, string expected) =>
		Assert.Equal(expected, Appearance.ResolveVariant(dark, highContrast).Key);

	[Theory]
	[InlineData(false, false, false)]
	[InlineData(false, true, true)]  // the system asks for it: followed
	[InlineData(true, false, true)]  // the setting only ever adds
	[InlineData(true, true, true)]
	public void HighContrast_WhenTheSystemOrTheUserAsksForIt(bool always, bool system, bool expected) =>
		Assert.Equal(expected, Appearance.ResolveHighContrast(always, system));

	[Fact]
	public Task HighContrast_FollowsTheSystemAndTheSetting_Live() => HeadlessUi.Run(() => WithThemeMode(() => {
		var (window, _) = HeadlessUi.Shell();
		bool settingBefore = SettingsFile.Instance.AlwaysHighContrast;
		try {
			SettingsFile.Instance.AlwaysHighContrast = false;
			SettingsFile.Instance.ThemeMode = ThemeMode.Dark;
			Appearance.SetSystemHighContrast(false);
			HeadlessUi.Pump();
			Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);

			// Windows: a contrast theme is switched on while VDF runs.
			Appearance.SetSystemHighContrast(true);
			HeadlessUi.Pump();
			Assert.Equal(VdfThemes.HighContrastDark, window.ActualThemeVariant);

			SettingsFile.Instance.ThemeMode = ThemeMode.Light;
			HeadlessUi.Pump();
			Assert.Equal(VdfThemes.HighContrastLight, window.ActualThemeVariant);

			// The system does not ask, the user does.
			Appearance.SetSystemHighContrast(false);
			HeadlessUi.Pump();
			Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
			SettingsFile.Instance.AlwaysHighContrast = true;
			HeadlessUi.Pump();
			Assert.Equal(VdfThemes.HighContrastLight, window.ActualThemeVariant);
		}
		finally {
			SettingsFile.Instance.AlwaysHighContrast = settingBefore;
			Appearance.SetSystemHighContrast(null);
			HeadlessUi.Pump();
		}
	}));
}
