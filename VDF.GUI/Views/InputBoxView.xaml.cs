// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {

	public static class InputBoxService {
		public static async Task<String> Show(string message, string defaultInput = "", string waterMark = "",
			MessageBoxButtons buttons = MessageBoxButtons.Ok | MessageBoxButtons.Cancel, string? title = null) {
			var dlg = new InputBoxView(message, defaultInput, waterMark, buttons, title) {
				Icon = ApplicationHelpers.MainWindow.Icon
			};
			return await dlg.ShowDialog<String>(ApplicationHelpers.MainWindow);
		}
	}


	public class InputBoxView : Window {

		//Designer need this
		public InputBoxView() => InitializeComponent();

		public InputBoxView(string message, string defaultInput = "", string waterMark = "",
			MessageBoxButtons buttons = MessageBoxButtons.Ok | MessageBoxButtons.Cancel, string? title = null) {
			DataContext = new InputBoxVM();
			((InputBoxVM)DataContext).host = this;
			((InputBoxVM)DataContext).Message = message;
			((InputBoxVM)DataContext).Input = defaultInput;
			((InputBoxVM)DataContext).WaterMark = waterMark;
			if (!string.IsNullOrEmpty(title))
				((InputBoxVM)DataContext).Title = title;
			((InputBoxVM)DataContext).HasCancelButton = (buttons & MessageBoxButtons.Cancel) != 0;
			((InputBoxVM)DataContext).HasNoButton = (buttons & MessageBoxButtons.No) != 0;
			((InputBoxVM)DataContext).HasOKButton = (buttons & MessageBoxButtons.Ok) != 0;
			((InputBoxVM)DataContext).HasYesButton = (buttons & MessageBoxButtons.Yes) != 0;

			InitializeComponent();
			Owner = ApplicationHelpers.MainWindow;

			if (!VDF.GUI.Data.SettingsFile.Instance.DarkMode)
				RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

			var textBox = this.FindControl<TextBox>("TextBoxInput");
			if (textBox != null) {
				textBox.AttachedToVisualTree += (s, e) => {
					textBox.Focus();
					textBox.SelectAll();
				};
			}
		}
		private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
	}

}
