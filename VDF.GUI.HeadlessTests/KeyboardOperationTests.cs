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
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>Everything a mouse can do on the main screens has to work from the keyboard alone.</summary>
public class KeyboardOperationTests {

	static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None) {
		window.KeyPressQwerty(key, modifiers);
		window.KeyReleaseQwerty(key, modifiers);
		HeadlessUi.Pump();
	}

	[Fact]
	public Task Setup_SpaceOnAScanProfile_SelectsItInTheViewModelToo() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		var window = HeadlessUi.Show(new SetupView { DataContext = vm });
		var original = vm.ScanProfileOptions.Single(p => p.IsActive);
		var radios = window.GetVisualDescendants().OfType<RadioButton>().ToList();
		var target = radios.First(r => r.IsChecked != true);
		var targetProfile = (ScanProfileOptionVM)target.DataContext!;
		try {
			target.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();
			Press(window, PhysicalKey.Space);

			// The radio used to be decoration next to a mouse-only card: Space checked it on
			// screen while the scan kept the previous profile.
			Assert.Same(targetProfile, vm.ScanProfileOptions.Single(p => p.IsActive));
			Assert.Same(target, radios.Single(r => r.IsChecked == true));
		}
		finally {
			vm.SelectScanProfileCommand.Execute(original).Subscribe(); // profiles write the shared settings
			window.Close();
		}
	});

	static readonly string[] SettingsSections = [
		"Scanning", "Matching", "PartialClips", "Directories", "Files", "Database",
		"Processing", "Schedule", "Appearance", "Shortcuts", "Test"
	];

	static string VisibleSection(SettingsView view) =>
		string.Join(",", view.FindControl<StackPanel>("SectionsHost")!.Children.OfType<StackPanel>()
			.Where(p => p.IsVisible && p.Tag is string).Select(p => (string)p.Tag!));

	[Fact]
	public Task Settings_EverySection_IsReachableWithTabAndArrowKeys() => HeadlessUi.Run(() => {
		var view = new SettingsView { DataContext = new MainWindowVM() };
		var window = HeadlessUi.Show(view);

		// The section list is the first thing on the page, so the first Tab lands in it.
		Press(window, PhysicalKey.Tab);
		var focused = window.FocusManager!.GetFocusedElement() as Control;
		Assert.True(focused?.FindAncestorOfType<ListBox>(includeSelf: true)?.Name == "NavList",
			$"first Tab stop is {focused?.GetType().Name ?? "nothing"}, not the settings section list");

		var visited = new List<string> { VisibleSection(view) };
		for (int i = 1; i < SettingsSections.Length; i++) {
			Press(window, PhysicalKey.ArrowDown);
			visited.Add(VisibleSection(view));
		}

		// The nav used to be eleven Borders with a PointerPressed handler: no focus, no
		// keys, ten of the eleven sections unreachable without a mouse.
		Assert.Equal(SettingsSections, visited);
		window.Close();
	});

	[Fact]
	public Task Settings_SectionList_TellsAScreenReaderWhatIsSelected() => HeadlessUi.Run(() => {
		var view = new SettingsView { DataContext = new MainWindowVM() };
		var window = HeadlessUi.Show(view);

		var nav = PeerTree.Walk(window).Where(n => n.Type == Avalonia.Automation.Peers.AutomationControlType.ListItem
			&& n.Owner?.FindAncestorOfType<ListBox>()?.Name == "NavList").ToList();

		Assert.Equal(SettingsSections.Length, nav.Count);
		Assert.Equal("Scanning", nav[0].Name);
		Assert.All(nav, n => Assert.Null(PeerTree.NameProblem(n.Name)));
		Assert.True(((ListBoxItem)nav[0].Owner!).IsSelected);
		window.Close();
	});

	[Fact]
	public Task Settings_Searching_DeselectsTheSection_AndPickingOneEndsTheSearch() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		var view = new SettingsView { DataContext = vm };
		var window = HeadlessUi.Show(view);
		var nav = view.FindControl<ListBox>("NavList")!;

		vm.SettingsSearchQuery = "dark";
		HeadlessUi.Pump();
		Assert.Null(nav.SelectedItem);

		nav.SelectedIndex = 8; // Appearance
		HeadlessUi.Pump();
		Assert.True(string.IsNullOrEmpty(vm.SettingsSearchQuery));
		Assert.Equal("Appearance", VisibleSection(view));
		window.Close();
	});

	[Fact]
	public Task Setup_EveryScanProfile_IsATabStop() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		var window = HeadlessUi.Show(new SetupView { DataContext = vm });

		var reached = new List<Control>();
		for (int i = 0; i < 60; i++) {
			Press(window, PhysicalKey.Tab);
			if (window.FocusManager!.GetFocusedElement() is not Control focused || reached.Contains(focused)) break;
			reached.Add(focused);
		}

		var profiles = reached.OfType<RadioButton>().Select(r => ((ScanProfileOptionVM)r.DataContext!).Name).ToList();
		Assert.Equal(vm.ScanProfileOptions.Select(p => p.Name), profiles);
		window.Close();
	});
}
