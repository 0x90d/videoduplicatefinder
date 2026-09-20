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
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public class HotKeyBox : Border {
		readonly TextBlock _textBlock;
		bool _isCapturing;

		public static readonly StyledProperty<string> GestureTextProperty =
			AvaloniaProperty.Register<HotKeyBox, string>(nameof(GestureText), defaultValue: string.Empty);

		public string GestureText {
			get => GetValue(GestureTextProperty);
			set => SetValue(GestureTextProperty, value);
		}

		public HotKeyBox() {
			Focusable = true;
			// A Border without a background takes no clicks, so this one is not left to a style.
			Background = Brushes.Transparent;
			BorderThickness = new Thickness(1);
			CornerRadius = new CornerRadius(3);
			Padding = new Thickness(6, 3);
			MinHeight = 28;
			Cursor = new Cursor(StandardCursorType.Hand);
			// The COLORS (border, focus border, text) come from the style next to the box in
			// SettingsView.xaml so they follow the theme; they used to be hard-coded for dark.
			_textBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
			Child = _textBlock;

			GestureTextProperty.Changed.AddClassHandler<HotKeyBox>((box, _) => box.UpdateDisplay());
		}

		void UpdateDisplay() {
			// While listening the box always shows the prompt, so it is obvious that the next
			// key press is going to be taken.
			_textBlock.Text = _isCapturing
				? App.Lang["MainWindow.Settings.KeyboardShortcuts.PressKeys"]
				: string.IsNullOrEmpty(GestureText)
					? App.Lang["MainWindow.Settings.KeyboardShortcuts.ClickToSet"]
					: GestureText;
			_textBlock.Classes.Set("placeholder", string.IsNullOrEmpty(GestureText) && !_isCapturing);
		}

		// The key that started listening, until it is released: holding Enter down must not
		// go on to assign "Enter" through key repeat.
		Key? activationKeyHeld;

		/// <summary>
		/// The box listens only after it was asked to: Enter, Space or a click. It used to
		/// listen from the moment it had focus, so walking through the shortcut list with Tab
		/// and touching any other key (an arrow, a letter, Space) reassigned whichever shortcut
		/// happened to have focus, and Escape wiped it.
		/// </summary>
		void StartListening(Key? activationKey = null) {
			_isCapturing = true;
			activationKeyHeld = activationKey;
			UpdateDisplay();
		}

		void StopListening() {
			_isCapturing = false;
			activationKeyHeld = null;
			UpdateDisplay();
		}

		protected override void OnGotFocus(FocusChangedEventArgs e) {
			base.OnGotFocus(e); // the :focus style draws the accent border, a Border has no focus adorner
		}

		protected override void OnLostFocus(FocusChangedEventArgs e) {
			base.OnLostFocus(e);
			StopListening();
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e) {
			base.OnPointerPressed(e);
			Focus();
			StartListening();
			e.Handled = true;
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			// Tab belongs to keyboard navigation, which skips handled events: marking it
			// handled like every other key made the box a focus trap, there was no way
			// out of the shortcut list without a mouse.
			if (e.Key == Key.Tab) {
				base.OnKeyDown(e);
				return;
			}

			var key = e.Key;
			var modifiers = e.KeyModifiers;

			if (!_isCapturing) {
				if (key is Key.Enter or Key.Space && modifiers == KeyModifiers.None) {
					StartListening(key);
					e.Handled = true;
				}
				else
					base.OnKeyDown(e); // not ours: Escape closes, arrows scroll, shortcuts work
				return;
			}

			e.Handled = true;
			if (key == activationKeyHeld)
				return;

			// Ignore modifier-only presses
			if (key is Key.LeftShift or Key.RightShift or
				Key.LeftCtrl or Key.RightCtrl or
				Key.LeftAlt or Key.RightAlt or
				Key.LWin or Key.RWin)
				return;

			// Escape backs out and keeps the shortcut; the clear button next to the box removes it.
			if (key == Key.Escape && modifiers == KeyModifiers.None) {
				StopListening();
				return;
			}

			if (KeyboardShortcutManager.IsReservedKey(key, modifiers))
				return;

			var gesture = new KeyGesture(key, modifiers);
			var gestureString = gesture.ToString();

			if (DataContext is ShortcutBindingVM binding) {
				binding.CheckConflict(gestureString);
				binding.ApplyGesture(gestureString);
			}
			StopListening();
		}

		protected override void OnKeyUp(KeyEventArgs e) {
			if (e.Key == activationKeyHeld)
				activationKeyHeld = null;
			if (_isCapturing)
				e.Handled = true;
			else
				base.OnKeyUp(e);
		}

		// A Border has no automation peer, which left the whole shortcut editor missing from
		// the tree screen readers are given.
		protected override AutomationPeer OnCreateAutomationPeer() => new HotKeyBoxAutomationPeer(this);
	}

	/// <summary>
	/// Presents a <see cref="HotKeyBox"/> as an edit field whose value is the assigned gesture.
	/// The name comes from AutomationProperties.Name (the action the shortcut belongs to).
	/// </summary>
	public class HotKeyBoxAutomationPeer : ControlAutomationPeer, IValueProvider {
		public HotKeyBoxAutomationPeer(HotKeyBox owner) : base(owner) { }

		new HotKeyBox Owner => (HotKeyBox)base.Owner;

		public bool IsReadOnly => false;
		public string? Value => Owner.GestureText;
		// Same path as a captured key press, so conflicts are resolved and the gesture is
		// stored; text that is not a gesture is ignored.
		public void SetValue(string? value) {
			if (Owner.DataContext is not ShortcutBindingVM binding) return;
			if (string.IsNullOrWhiteSpace(value)) {
				binding.ApplyGesture(string.Empty);
				return;
			}
			try {
				binding.ApplyGesture(KeyGesture.Parse(value).ToString());
			}
			catch (ArgumentException) { }
		}

		protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
		protected override bool IsContentElementCore() => true;
		protected override bool IsControlElementCore() => true;
		protected override string? GetHelpTextCore() =>
			base.GetHelpTextCore() ?? App.Lang["A11y.Shortcuts.BoxHelp"];
	}
}
