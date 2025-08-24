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
using Avalonia.Controls.ApplicationLifetimes;
using VDF.GUI.Views;

namespace VDF.GUI {
	static class ApplicationHelpers {
		public static IClassicDesktopStyleApplicationLifetime CurrentApplicationLifetime =>
			(IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
		public static MainWindow MainWindow =>
			(MainWindow)((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!)!.MainWindow!;

		public static ViewModels.MainWindowVM MainWindowDataContext =>
			(ViewModels.MainWindowVM)MainWindow.DataContext!;

	}
}
