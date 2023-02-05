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
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public class DatabaseViewer : Window {
		public DatabaseViewer() {
			InitializeComponent();
			DataContext = new DatabaseViewerVM(this.FindControl<DataGrid>("datagridDatabase")!);
			Owner = ApplicationHelpers.MainWindow;
			Closing += DatabaseViewer_Closing;
		}

		private void DatabaseViewer_Closing(object? sender, System.ComponentModel.CancelEventArgs e) 
			=> ((DatabaseViewerVM)DataContext!).Save();

		void InitializeComponent() => AvaloniaXamlLoader.Load(this);		

	}
}
