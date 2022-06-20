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
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using VDF.GUI.Data;
using VDF.GUI.Mvvm;

namespace VDF.GUI.Views {
	public class MainWindow : FluentWindow {
		bool keepBackupFile;
		bool hasExited;
		public readonly Core.FFTools.FFHardwareAccelerationMode InitialHwMode;
		public MainWindow() {
			//Settings must be load before XAML is parsed
			SettingsFile.LoadSettings();
			// See comment in App.xaml.cs
			InitialHwMode = SettingsFile.Instance.HardwareAccelerationMode;

			InitializeComponent();
			Closing += MainWindow_Closing;
			//Don't use this Window.OnClosing event,
			//datacontext might not be the same due to Avalonia internal handling data differently


			if (SettingsFile.Instance.HardwareAccelerationMode == Core.FFTools.FFHardwareAccelerationMode.none) {
				this.FindControl<ComboBox>("ComboboxHardwareAccelerationMode").SelectedIndex = 1;
			}


			this.FindControl<ListBox>("ListboxIncludelist").AddHandler(DragDrop.DropEvent, DropInclude);
			this.FindControl<ListBox>("ListboxIncludelist").AddHandler(DragDrop.DragOverEvent, DragOver);
			this.FindControl<ListBox>("ListboxBlacklist").AddHandler(DragDrop.DropEvent, DropBlacklist);
			this.FindControl<ListBox>("ListboxBlacklist").AddHandler(DragDrop.DragOverEvent, DragOver);

			ApplicationHelpers.CurrentApplicationLifetime.Startup += MainWindow_Startup;
			ApplicationHelpers.CurrentApplicationLifetime.Exit += MainWindow_Exit;
			ApplicationHelpers.CurrentApplicationLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		}

		void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
			e.Cancel = true;
			ConfirmClose();
		}

		async void ConfirmClose() {
			try {
				if (!keepBackupFile)
					File.Delete(ApplicationHelpers.MainWindowDataContext.BackupScanResultsFile);
			}
			catch { }
			if (keepBackupFile = await ApplicationHelpers.MainWindowDataContext.SaveScanResults()) {
				ApplicationHelpers.CurrentApplicationLifetime.Shutdown();
			}
		}

		void MainWindow_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e) {
			if (hasExited) return;
			hasExited = true;
			SettingsFile.SaveSettings();
		}

		private void DragOver(object? sender, DragEventArgs e) {
			// Only allow Copy or Link as Drop Operations.
			e.DragEffects &= (DragDropEffects.Copy | DragDropEffects.Link);

			// Only allow if the dragged data contains text or filenames.
			if (!e.Data.Contains(DataFormats.FileNames))
				e.DragEffects = DragDropEffects.None;
		}

		private void DropInclude(object? sender, DragEventArgs e) {
			if (!e.Data.Contains(DataFormats.FileNames)) return;
			
			foreach(string file in e.Data.GetFileNames()!) {
				if (!Directory.Exists(file)) continue;
				if (!SettingsFile.Instance.Includes.Contains(file))
					SettingsFile.Instance.Includes.Add(file);
			}
		}
		private void DropBlacklist(object? sender, DragEventArgs e) {
			if (!e.Data.Contains(DataFormats.FileNames)) return;

			foreach (string file in e.Data.GetFileNames()!) {
				if (!Directory.Exists(file)) continue;
				if (!SettingsFile.Instance.Blacklists.Contains(file))
					SettingsFile.Instance.Blacklists.Add(file);
			}
		}

		void Thumbnails_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
			if (ApplicationHelpers.MainWindow != null && ApplicationHelpers.MainWindowDataContext != null)
				ApplicationHelpers.MainWindowDataContext.Thumbnails_ValueChanged(sender, e);
		}

		void MainWindow_Startup(object? sender, ControlledApplicationLifetimeStartupEventArgs e) => ApplicationHelpers.MainWindowDataContext.LoadDatabase();

		void InitializeComponent() => AvaloniaXamlLoader.Load(this);
	}
}
