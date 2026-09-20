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
}
