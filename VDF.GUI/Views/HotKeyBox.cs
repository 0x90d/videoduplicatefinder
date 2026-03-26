// /*
//     Copyright (C) 2025 0x90d
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
			Background = new SolidColorBrush(Colors.Transparent);
			BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
			BorderThickness = new Thickness(1);
			CornerRadius = new CornerRadius(3);
			Padding = new Thickness(6, 3);
			MinHeight = 28;
			Cursor = new Cursor(StandardCursorType.Hand);

			_textBlock = new TextBlock {
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
			};
			Child = _textBlock;

			GestureTextProperty.Changed.AddClassHandler<HotKeyBox>((box, _) => box.UpdateDisplay());
		}

		void UpdateDisplay() {
			if (_isCapturing) {
				_textBlock.Text = string.IsNullOrEmpty(GestureText)
					? App.Lang["MainWindow.Settings.KeyboardShortcuts.PressKeys"]
					: GestureText;
			}
			else {
				_textBlock.Text = string.IsNullOrEmpty(GestureText)
					? App.Lang["MainWindow.Settings.KeyboardShortcuts.ClickToSet"]
					: GestureText;
			}

			if (string.IsNullOrEmpty(GestureText) && !_isCapturing)
				_textBlock.Opacity = 0.5;
			else
				_textBlock.Opacity = 1.0;
		}

		protected override void OnGotFocus(GotFocusEventArgs e) {
			base.OnGotFocus(e);
			_isCapturing = true;
			BorderBrush = new SolidColorBrush(Color.FromRgb(100, 150, 255));
			UpdateDisplay();
		}

		protected override void OnLostFocus(RoutedEventArgs e) {
			base.OnLostFocus(e);
			_isCapturing = false;
			BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
			UpdateDisplay();
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			e.Handled = true;

			if (!_isCapturing)
				return;

			var key = e.Key;
			var modifiers = e.KeyModifiers;

			// Ignore modifier-only presses
			if (key is Key.LeftShift or Key.RightShift or
				Key.LeftCtrl or Key.RightCtrl or
				Key.LeftAlt or Key.RightAlt or
				Key.LWin or Key.RWin)
				return;

			// Escape clears the shortcut
			if (key == Key.Escape && modifiers == KeyModifiers.None) {
				if (DataContext is ShortcutBindingVM vm)
					vm.ApplyGesture(string.Empty);
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
		}

		protected override void OnKeyUp(KeyEventArgs e) {
			if (_isCapturing)
				e.Handled = true;
			else
				base.OnKeyUp(e);
		}
	}
}
