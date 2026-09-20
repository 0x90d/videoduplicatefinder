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
using Avalonia.Interactivity;
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

	[Theory]
	[InlineData(1)] // a file row
	[InlineData(0)] // a group header
	public Task Results_ContextMenuKeyOnAFocusedRow_OpensThatRowsMenu(int rowIndex) => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });
		var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
		var row = (ListBoxItem)list.ContainerFromIndex(rowIndex)!;
		var menu = row.GetVisualDescendants().OfType<Border>().First(b => b.ContextMenu != null).ContextMenu!;
		row.Focus(NavigationMethod.Directional);
		HeadlessUi.Pump();

		// What Shift+F10 / the Menu key boil down to: Avalonia raises ContextRequested on
		// the FOCUSED element and it bubbles up. The menus hang on a Border inside the row,
		// below the focus, so the keyboard could never open them.
		row.RaiseEvent(new ContextRequestedEventArgs());
		HeadlessUi.Pump();

		try {
			Assert.True(menu.IsOpen);
		}
		finally {
			menu.Close();
			window.Close();
		}
	});

	static Button FocusScanButton(Window window) {
		var setup = window.GetVisualDescendants().OfType<SetupView>().First();
		var button = setup.GetVisualDescendants().OfType<Button>().Last(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled);
		button.Focus(NavigationMethod.Tab);
		HeadlessUi.Pump();
		Assert.Same(button, window.FocusManager!.GetFocusedElement());
		return button;
	}

	[Fact]
	public Task Shell_BusyCurtain_KeepsTheKeyboardOutOfTheViewBelow() => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		FocusScanButton(window);
		try {
			vm.IsBusy = true;
			HeadlessUi.Pump();

			// The curtain only ever stopped the mouse: focus stayed where it was, Tab walked
			// on through the view below, and Space / Delete there acted in the middle of a
			// running operation.
			var under = new List<string>();
			if ((window.FocusManager!.GetFocusedElement() as Control)?.FindAncestorOfType<SetupView>() != null)
				under.Add("focus stayed on " + window.FocusManager!.GetFocusedElement()!.GetType().Name);
			for (int i = 0; i < 40; i++) {
				Press(window, PhysicalKey.Tab);
				if (window.FocusManager!.GetFocusedElement() is Control c && c.FindAncestorOfType<SetupView>() != null)
					under.Add(c.GetType().Name);
			}
			Assert.True(under.Count == 0, "keyboard reached controls under the busy curtain: " + string.Join(", ", under.Distinct()));
		}
		finally {
			vm.IsBusy = false;
			HeadlessUi.Pump();
		}
	});

	[Fact]
	public Task Shell_BusyCurtain_GivesFocusBackWhenItLifts() => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		var scanButton = FocusScanButton(window);
		try {
			vm.IsBusy = true;
			HeadlessUi.Pump();
			Assert.NotSame(scanButton, window.FocusManager!.GetFocusedElement());
		}
		finally {
			vm.IsBusy = false;
			HeadlessUi.Pump();
		}

		// Otherwise every delete would throw a keyboard user out of the results list.
		Assert.Same(scanButton, window.FocusManager!.GetFocusedElement());
	});

	[Fact]
	public Task Shell_CancelableBusyCurtain_PutsFocusOnCancel() => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		FocusScanButton(window);
		try {
			vm.IsBusyCancelable = true;
			vm.IsBusy = true;
			HeadlessUi.Pump();

			Assert.True(window.FocusManager!.GetFocusedElement() is Button { Name: "BusyCancelButton" });
		}
		finally {
			vm.IsBusy = false;
			vm.IsBusyCancelable = false;
			HeadlessUi.Pump();
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
	public Task Settings_ResultColumns_CanBeSwitchedWithoutAMouse() => HeadlessUi.Run(() => {
		var view = new SettingsView { DataContext = new MainWindowVM() };
		var window = HeadlessUi.Show(view);
		view.FindControl<ListBox>("NavList")!.SelectedIndex = 5; // Results & database
		HeadlessUi.Pump();
		bool before = Data.SettingsFile.Instance.ShowBitrateColumn;
		try {
			// Column visibility used to live only in the column header's right-click menu,
			// and a header strip cannot take keyboard focus.
			var bitrate = view.GetVisualDescendants().OfType<CheckBox>()
				.Single(c => c.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(c) == "Bitrate");
			bitrate.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();
			Press(window, PhysicalKey.Space);

			Assert.Equal(!before, Data.SettingsFile.Instance.ShowBitrateColumn);
		}
		finally {
			Data.SettingsFile.Instance.ShowBitrateColumn = before;
			window.Close();
		}
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
