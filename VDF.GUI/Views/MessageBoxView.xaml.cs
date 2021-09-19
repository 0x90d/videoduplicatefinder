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

	public static class MessageBoxService {
		public static async Task<MessageBoxButtons> Show(string message, MessageBoxButtons buttons = MessageBoxButtons.Ok,
			string title = null) {
			while (ApplicationHelpers.MainWindow == null) {
				await Task.Delay(500);
			}
			var dlg = new MessageBoxView(message, buttons, title) {
				Icon = ApplicationHelpers.MainWindow.Icon
			};
			return await dlg.ShowDialog<MessageBoxButtons>(ApplicationHelpers.MainWindow);
		}

	}


	public class MessageBoxView : Window {

		//Designer need this
		public MessageBoxView() => InitializeComponent();

		public MessageBoxView(string message, MessageBoxButtons buttons = MessageBoxButtons.Ok, string title = null) {

			DataContext = new MessageBoxVM();
			((MessageBoxVM)DataContext).host = this;
			((MessageBoxVM)DataContext).Message = message;
			if (!string.IsNullOrEmpty(title))
				((MessageBoxVM)DataContext).Title = title;
			((MessageBoxVM)DataContext).HasCancelButton = (buttons & MessageBoxButtons.Cancel) != 0;
			((MessageBoxVM)DataContext).HasNoButton = (buttons & MessageBoxButtons.No) != 0;
			((MessageBoxVM)DataContext).HasOKButton = (buttons & MessageBoxButtons.Ok) != 0;
			((MessageBoxVM)DataContext).HasYesButton = (buttons & MessageBoxButtons.Yes) != 0;

			InitializeComponent();

		}
		private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
	}


}
