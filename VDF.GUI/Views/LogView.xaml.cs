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
using Avalonia.Markup.Xaml;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public partial class LogView : UserControl {
		public LogView() => AvaloniaXamlLoader.Load(this);

		// Only offer "Open In Folder" when the right-clicked log line actually
		// resolves to a file/folder that exists; otherwise suppress the menu.
		private void LogContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e) {
			if (DataContext is MainWindowVM vm &&
				MainWindowVM.TryExtractExistingPath((vm.SelectedLogItem as LogMessageRow)?.Message) == null)
				e.Cancel = true;
		}
	}
}
