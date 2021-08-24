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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VDF.GUI.Mvvm;
using VDF.Core.Utils;

namespace VDF.GUI.Views {
	public class MainWindow : FluentWindow {
		public MainWindow() {
			InitializeComponent();
			Closing += MainWindow_Closing;
			//Don't use this Window.OnClosing event,
			//datacontext might not be the same due to Avalonia internal handling data differently

			ApplicationHelpers.CurrentApplicationLifetime.Startup += MainWindow_Startup;
			ApplicationHelpers.CurrentApplicationLifetime.Exit += MainWindow_Exit;
			ApplicationHelpers.CurrentApplicationLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		}

		void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
			e.Cancel = true;
			ConfirmClose();
		}

		static async void ConfirmClose() {
			var result = await MessageBoxService.Show("Are you sure you want to close?",
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (result == MessageBoxButtons.Yes)
				ApplicationHelpers.CurrentApplicationLifetime.Shutdown();
		}

		void MainWindow_Exit(object sender, ControlledApplicationLifetimeExitEventArgs e) {
			ApplicationHelpers.MainWindowDataContext.SaveSettings();
			DatabaseUtils.UnloadDatabase(); // Needed because of delayed actions
		}

		void MainWindow_Startup(object sender, ControlledApplicationLifetimeStartupEventArgs e) {
			ApplicationHelpers.MainWindowDataContext.LoadSettings();
			ApplicationHelpers.MainWindowDataContext.LoadDatabase();
		}


		void InitializeComponent() => AvaloniaXamlLoader.Load(this);
	}
}
