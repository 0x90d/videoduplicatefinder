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

using System.Linq;
using System.Windows.Input;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Mvvm;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public class MainWindow : Window {
		bool keepBackupFile;
		bool hasExited;

		public readonly Core.FFTools.FFHardwareAccelerationMode InitialHwMode;
		public MainWindow() {
			//Settings must be load before XAML is parsed
			SettingsFile.LoadSettings();
			App.Lang.CurrentLanguage = SettingsFile.Instance.LanguageCode;

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

			// Switch theme at runtime when the user toggles the DarkMode setting
			SettingsFile.Instance.PropertyChanged += (_, e) => {
				if (e.PropertyName == nameof(SettingsFile.DarkMode))
					RequestedThemeVariant = SettingsFile.Instance.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
			};

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				this.FindControl<TextBlock>("TextBlockWindowTitle")!.IsVisible = false;
			}
			ShowAlgoView();
		}

		async void ShowAlgoView() {

			if (File.Exists(FileUtils.SafePathCombine(
					CoreUtils.ResolveDatabaseFolder(SettingsFile.Instance.CustomDatabaseFolder),
					"ScannedFiles.db")))
					return;

			while (!this.IsVisible) {
				await Task.Delay(200);
			}
			var dlg = new Views.ChooseAlgoView();
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

			ApplyKeyboardShortcuts();
			KeyboardShortcutManager.Instance.ShortcutsChanged += ApplyKeyboardShortcuts;
		}

		void ApplyKeyboardShortcuts() {
			var vm = ApplicationHelpers.MainWindowDataContext;
			var commandMap = new Dictionary<string, ICommand> {
				["RenameFile"] = vm.RenameFileCommand,
				["ToggleCheckbox"] = vm.ToggleCheckboxCommand,
				["OpenItemsByColId"] = vm.OpenItemsByColIdCommand,
				["OpenItemInFolder"] = vm.OpenItemInFolderCommand,
				["DeleteCheckedItemsWithPrompt"] = vm.DeleteCheckedItemsWithPromptCommand,
				["DeleteCheckedItems"] = vm.DeleteCheckedItemsCommand,
				["DeleteHighlighted"] = vm.DeleteHighlightedCommand,
				["ShowGroupInThumbnailComparer"] = vm.ShowGroupInThumbnailComparerCommand,
				["MarkGroupAsNotAMatch"] = vm.MarkGroupAsNotAMatchCommand,
				["CopyCheckedItems"] = vm.CopyCheckedItemsCommand,
				["MoveCheckedItems"] = vm.MoveCheckedItemsCommand,
				["CheckLowestQuality"] = vm.CheckLowestQualityCommand,
				["ClearCheckedItems"] = vm.ClearCheckedItemsCommand,
				["InvertCheckedItems"] = vm.InvertCheckedItemsCommand,
				["ExpandAllGroups"] = vm.ExpandAllGroupsCommand,
				["CollapseAllGroups"] = vm.CollapseAllGroupsCommand,
				["RemoveCheckedItemsFromList"] = vm.RemoveCheckedItemsFromListCommand,
				["NavigateNextGroup"] = vm.NavigateNextGroupCommand,
				["NavigatePreviousGroup"] = vm.NavigatePreviousGroupCommand,
			};
			var dataGrid = this.FindControl<DataGrid>("dataGridGrouping")!;
			KeyboardShortcutManager.Instance.ApplyBindings(dataGrid, commandMap);
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
			if (!e.DataTransfer.Contains(DataFormat.File))
				e.DragEffects = DragDropEffects.None;
		}

		private void DropInclude(object? sender, DragEventArgs e) {
			if (!e.DataTransfer.Contains(DataFormat.File)) return;

			foreach (var path in e.DataTransfer.GetItems(DataFormat.File) ?? Array.Empty<IDataTransferItem>()) {
				IStorageItem? fold = path.TryGetFile();
				if (fold == null)
					continue;
				string? localPath = fold.TryGetLocalPath();
				if (!string.IsNullOrEmpty(localPath) && !SettingsFile.Instance.Includes.Contains(localPath))
					SettingsFile.Instance.Includes.Add(localPath);
			}
		}
		private void DropBlacklist(object? sender, DragEventArgs e) {
			if (!e.DataTransfer.Contains(DataFormat.File)) return;

			foreach (var path in e.DataTransfer.GetItems(DataFormat.File) ?? Array.Empty<IDataTransferItem>()) {
				IStorageItem? fold = path.TryGetFile();
				if (fold == null)
					continue;
				string? localPath = fold.TryGetLocalPath();
				if (!string.IsNullOrEmpty(localPath) && !SettingsFile.Instance.Includes.Contains(localPath))
					SettingsFile.Instance.Includes.Add(localPath);
			}
		}

		void Thumbnails_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
			if (ApplicationHelpers.MainWindow != null && ApplicationHelpers.MainWindowDataContext != null)
				ApplicationHelpers.MainWindowDataContext.Thumbnails_ValueChanged(sender, e);
		}

		void MainWindow_Startup(object? sender, ControlledApplicationLifetimeStartupEventArgs e) {
			var vm = ApplicationHelpers.MainWindowDataContext;
			vm.LoadDatabase();
			vm.RestoreBackupScanResults();
		}

		void OnLoadingRowGroup(object? sender, DataGridRowGroupHeaderEventArgs e) {
			var header = e.RowGroupHeader;
			// Avoid adding buttons twice (recycled headers)
			if (header.Tag is true) return;
			header.Tag = true;

			var vm = ApplicationHelpers.MainWindowDataContext;

			Guid GetGroupId() {
				if (header.DataContext is Avalonia.Collections.DataGridCollectionViewGroup g) {
					var first = g.Items.OfType<DuplicateItemVM>().FirstOrDefault();
					if (first != null) return first.ItemInfo.GroupId;
				}
				return Guid.Empty;
			}

			var compareBtn = new Button { Content = "Compare", Classes = { "group-action" } };
			var keepBestBtn = new Button { Content = "Keep Best", Classes = { "group-action" } };

			compareBtn.Click += (_, _) => {
				var id = GetGroupId();
				if (id != Guid.Empty) vm.CompareGroup(id);
			};
			keepBestBtn.Click += (_, _) => {
				var id = GetGroupId();
				if (id != Guid.Empty) vm.KeepBestInGroup(id);
			};

			var panel = new StackPanel {
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				Margin = new Thickness(8, 0, 4, 0),
				VerticalAlignment = VerticalAlignment.Center,
				Children = { compareBtn, keepBestBtn }
			};

			// Inject buttons into the header's visual tree once it's loaded
			header.Loaded += (_, _) => {
				// Walk visual tree to find the root Grid and append our button panel
				var grid = header.GetVisualDescendants().OfType<Grid>().FirstOrDefault();
				if (grid != null && !grid.Children.Contains(panel)) {
					grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
					Grid.SetColumn(panel, grid.ColumnDefinitions.Count - 1);
					grid.Children.Add(panel);
				}
			};
		}

		void OnMetricPointerEntered(object? sender, PointerEventArgs e) {
			if (sender is Control ctrl && ctrl.Tag is string metric && ctrl.DataContext is DuplicateItemVM item)
				ApplicationHelpers.MainWindowDataContext.SetHoveredMetric(item, metric);
		}

		void OnMetricPointerExited(object? sender, PointerEventArgs e) {
			if (sender is Control ctrl && ctrl.DataContext is DuplicateItemVM item)
				ApplicationHelpers.MainWindowDataContext.ClearHoveredMetric(item);
		}

		void InitializeComponent() => AvaloniaXamlLoader.Load(this);
	}
}
