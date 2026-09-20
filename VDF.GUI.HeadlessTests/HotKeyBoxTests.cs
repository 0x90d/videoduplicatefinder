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

using System.Collections.ObjectModel;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>The shortcut capture box of Settings > Keyboard shortcuts, used by keyboard alone.</summary>
public class HotKeyBoxTests {

	static (Window Window, HotKeyBox Box, Button After, ShortcutBindingVM Binding) ShowBoxBetweenButtons() {
		var siblings = new ObservableCollection<ShortcutBindingVM>();
		var binding = new ShortcutBindingVM("RenameFile", siblings);
		siblings.Add(binding);
		var box = new HotKeyBox { DataContext = binding, GestureText = binding.CurrentGesture };
		AutomationProperties.SetName(box, binding.DisplayName);
		var after = new Button { Content = "after" };
		var window = HeadlessUi.Show(new StackPanel { Children = { new Button { Content = "before" }, box, after } });
		return (window, box, after, binding);
	}

	[Fact]
	public Task Tab_LeavesTheBox() => HeadlessUi.Run(() => {
		var (window, box, after, binding) = ShowBoxBetweenButtons();
		string gestureBefore = binding.CurrentGesture;
		box.Focus(NavigationMethod.Tab);
		HeadlessUi.Pump();
		Assert.Same(box, window.FocusManager!.GetFocusedElement());

		window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
		window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
		HeadlessUi.Pump();

		// The box used to mark every key handled, Tab included, and Avalonia's Tab
		// navigation skips handled events: focus could never leave by keyboard.
		Assert.Same(after, window.FocusManager!.GetFocusedElement());
		Assert.Equal(gestureBefore, binding.CurrentGesture);
		window.Close();
	});

	[Fact]
	public Task ShiftTab_LeavesTheBoxBackwards() => HeadlessUi.Run(() => {
		var (window, box, _, _) = ShowBoxBetweenButtons();
		box.Focus(NavigationMethod.Tab);
		HeadlessUi.Pump();

		window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
		window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
		HeadlessUi.Pump();

		Assert.True(window.FocusManager!.GetFocusedElement() is Button { Content: "before" });
		window.Close();
	});

	[Fact]
	public Task ScreenReader_SeesAnEditFieldNamedAfterItsActionHoldingTheGesture() => HeadlessUi.Run(() => {
		var (window, box, _, binding) = ShowBoxBetweenButtons();

		var peer = ControlAutomationPeer.CreatePeerForElement(box);

		Assert.True(peer.IsControlElement());
		Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
		Assert.Equal(binding.DisplayName, peer.GetName());
		Assert.False(string.IsNullOrWhiteSpace(peer.GetHelpText()));
		var value = Assert.IsAssignableFrom<IValueProvider>(peer);
		Assert.Equal(binding.CurrentGesture, value.Value);
		window.Close();
	});
}
