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

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Mvvm;

namespace VDF.GUI.Views {
	public class MainWindow : Window {
		bool keepBackupFile;
		bool hasExited;

		public readonly Core.FFTools.FFHardwareAccelerationMode InitialHwMode;
		public MainWindow() {
			//Settings must be load before XAML is parsed
			SettingsFile.LoadSettings();

			InitializeComponent();
			Closing += MainWindow_Closing;
			Opened += MainWindow_Opened;
			//Don't use this Window.OnClosing event,
			//datacontext might not be the same due to Avalonia internal handling data differently



			this.FindControl<ListBox>("ListboxIncludelist")!.AddHandler(DragDrop.DropEvent, DropInclude);
			this.FindControl<ListBox>("ListboxIncludelist")!.AddHandler(DragDrop.DragOverEvent, DragOver);
			this.FindControl<ListBox>("ListboxBlacklist")!.AddHandler(DragDrop.DropEvent, DropBlacklist);
			this.FindControl<ListBox>("ListboxBlacklist")!.AddHandler(DragDrop.DragOverEvent, DragOver);

			ApplicationHelpers.CurrentApplicationLifetime.Startup += MainWindow_Startup;
			ApplicationHelpers.CurrentApplicationLifetime.Exit += MainWindow_Exit;
			ApplicationHelpers.CurrentApplicationLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;

			if (SettingsFile.Instance.UseMica &&
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
				Environment.OSVersion.Version.Build >= 22000) {
				Background = null;
				TransparencyLevelHint = new List<WindowTransparencyLevel> { WindowTransparencyLevel.Mica };
				ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
				if (SettingsFile.Instance.DarkMode)
					this.FindControl<ExperimentalAcrylicBorder>("ExperimentalAcrylicBorderBackgroundBlack")!.IsVisible = true;
				else
					this.FindControl<ExperimentalAcrylicBorder>("ExperimentalAcrylicBorderBackgroundWhite")!.IsVisible = true;
			}

			if (!SettingsFile.Instance.DarkMode)
				RequestedThemeVariant = ThemeVariant.Light;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				this.FindControl<TextBlock>("TextBlockWindowTitle")!.IsVisible = false;
			}
			ShowAlgoView();
		}
		async void ShowAlgoView() {

			if (File.Exists(Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder)
					? FileUtils.SafePathCombine(SettingsFile.Instance.CustomDatabaseFolder,
					"ScannedFiles.db")
					: FileUtils.SafePathCombine(CoreUtils.CurrentFolder,
					"ScannedFiles.db")))
					return;

			var dlg = new Views.ChooseAlgoView();
			while (!this.IsVisible) {
				await Task.Delay(200);
			}
			await dlg.ShowDialog(this);
			if (dlg.FindControl<RadioButton>("Cb16x16")?.IsChecked == true)
				VDF.Core.Utils.DatabaseUtils.Create16x16Database();
		}

		private void MainWindow_Opened(object? sender, EventArgs e) {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				/*
				 * Due to Avalonia bug, window is bigger than screen size. 
				 * Status bar is hidden by MacOS launch bar,
				 * see https://github.com/0x90d/videoduplicatefinder/issues/391
				 */
				Height = 750d;
			}
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
				Closing -= MainWindow_Closing;
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

			// Only allow if the dragged data contains filenames.
			if (!e.Data.Contains(DataFormats.Files))
				e.DragEffects = DragDropEffects.None;
		}

		private void DropInclude(object? sender, DragEventArgs e) {
			if (!e.Data.Contains(DataFormats.Files)) return;

			foreach (var path in e.Data.GetFiles() ?? Array.Empty<IStorageFolder>()) {
				if (path is not IStorageFolder) continue;
				string? localPath = path.TryGetLocalPath();
				if (!string.IsNullOrEmpty(localPath) && !SettingsFile.Instance.Includes.Contains(localPath))
					SettingsFile.Instance.Includes.Add(localPath);
			}
		}
		private void DropBlacklist(object? sender, DragEventArgs e) {
			if (!e.Data.Contains(DataFormats.Files)) return;

			foreach (var path in e.Data.GetFiles() ?? Array.Empty<IStorageFolder>()) {
				if (path is not IStorageFolder) continue;
				string? localPath = path.TryGetLocalPath();
				if (!string.IsNullOrEmpty(localPath) && !SettingsFile.Instance.Includes.Contains(localPath))
					SettingsFile.Instance.Includes.Add(localPath);
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
