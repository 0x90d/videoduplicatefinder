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

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {

	public static class MessageBoxService {
		public static async Task<MessageBoxButtons?> Show(string message, MessageBoxButtons buttons = MessageBoxButtons.Ok,
			string? title = null) {
			while (ApplicationHelpers.MainWindow == null) {
				await Task.Delay(500);
			}
			var dlg = new MessageBoxView(message, buttons, title) {
				Icon = ApplicationHelpers.MainWindow.Icon
			};
			return await dlg.ShowDialog<MessageBoxButtons?>(ApplicationHelpers.MainWindow);
		}

	}


	public class MessageBoxView : Window {

		//Designer need this
		public MessageBoxView() => InitializeComponent();

		public MessageBoxView(string message, MessageBoxButtons buttons = MessageBoxButtons.Ok, string? title = null) {

			MessageBoxVM vm = new() {
				host = this,
				Message = message,
				HasCancelButton = (buttons & MessageBoxButtons.Cancel) != 0,
				HasNoButton = (buttons & MessageBoxButtons.No) != 0,
				HasOKButton = (buttons & MessageBoxButtons.Ok) != 0,
				HasYesButton = (buttons & MessageBoxButtons.Yes) != 0
			};
			if (!string.IsNullOrEmpty(title))
				vm.Title = title;

			DataContext = vm;
			InitializeComponent();
			Owner = ApplicationHelpers.MainWindow;
			if (!VDF.GUI.Data.SettingsFile.Instance.DarkMode)
				RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
			//Opened += MessageBoxView_Opened;
		}

		//private void MessageBoxView_Opened(object? sender, EventArgs e) {
			////	Setting width on Grid is a workaround for a bug under Windows
			////	For more information see:
			////	https://github.com/AvaloniaUI/Avalonia/issues/4838#issuecomment-710759375
			//var grid = this.FindControl<Grid>("GridMessageboxContent");
			//if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			//	grid.Width = Bounds.Width;
			//}
			////	Workaround for an Avalonia bug with Scrollviewer height not working with Window.SizeToContent
			//var scrollviewer = this.FindControl<ScrollViewer>("ScrollViewerMessageboxContent");
			//scrollviewer.MaxHeight = Bounds.Height - grid.RowDefinitions[1].ActualHeight;
		//}

		private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
	}


}
