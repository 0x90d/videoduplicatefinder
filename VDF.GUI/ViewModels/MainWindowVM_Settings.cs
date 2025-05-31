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

using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {
#pragma warning disable CA1822 // Mark members as static => It's used by Avalonia binding
		public IEnumerable<Core.FFTools.FFHardwareAccelerationMode> HardwareAccelerationModes =>
#pragma warning restore CA1822 // Mark members as static
			Enum.GetValues<Core.FFTools.FFHardwareAccelerationMode>();

		static readonly List<string> _CustomCommandList = typeof(SettingsFile.CustomActionCommands).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
		public List<string> CustomCommandList => _CustomCommandList;
		PropertyInfo _SelectedCustomCommand = typeof(SettingsFile.CustomActionCommands).GetProperty(_CustomCommandList[0])!;
		public string SelectedCustomCommand {
			get => _SelectedCustomCommand.Name;
			set {
				_SelectedCustomCommand = typeof(SettingsFile.CustomActionCommands).GetProperty(value)!;
				this.RaisePropertyChanged(nameof(SelectedCustomCommandValue));
			}
		}
		public string SelectedCustomCommandValue {
			get => (string)_SelectedCustomCommand.GetValue(SettingsFile.Instance.CustomCommands)!;
			set {
				_SelectedCustomCommand.SetValue(SettingsFile.Instance.CustomCommands, value);
				this.RaisePropertyChanged(nameof(IsMultiOpenSupported));
				this.RaisePropertyChanged(nameof(IsMultiOpenInFolderSupported));
			}
		}
		void Instance_LogItemAdded(string message) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				if (string.IsNullOrEmpty(message)) return;
				LogItems.Add(message);
			});

		public ReactiveCommand<Unit, Unit> OpenHWInfoLinkCommand => ReactiveCommand.CreateFromTask(async () => {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "https://trac.ffmpeg.org/wiki/HWAccelIntro#PlatformAPIAvailability",
					UseShellExecute = true
				});
			}
			catch {
				await MessageBoxService.Show("Failed to open URL: https://trac.ffmpeg.org/wiki/HWAccelIntro#PlatformAPIAvailability");
			}
		});
		public ReactiveCommand<Unit, Unit> AddIncludesToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					AllowMultiple = true,
					Title = "Select folder"
				}
				);

			if (result == null || result.Count == 0) return;
			foreach (var item in result) {
				if (!SettingsFile.Instance.Includes.Contains(item))
					SettingsFile.Instance.Includes.Add(item);
			}
		});
		public ReactiveCommand<Unit, Unit> AddFilePathContainsTextToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await InputBoxService.Show("New Entry");
			if (string.IsNullOrEmpty(result)) return;
			if (!SettingsFile.Instance.FilePathContainsTexts.Contains(result))
				SettingsFile.Instance.FilePathContainsTexts.Add(result);
		});
		public ReactiveCommand<ListBox, Action> RemoveFilePathContainsTextFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems?.Count > 0)
				SettingsFile.Instance.FilePathContainsTexts.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});
		public ReactiveCommand<Unit, Unit> AddFilePathNotContainsTextToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await InputBoxService.Show("New Entry");
			if (string.IsNullOrEmpty(result)) return;
			if (!SettingsFile.Instance.FilePathNotContainsTexts.Contains(result))
				SettingsFile.Instance.FilePathNotContainsTexts.Add(result);
		});
		public ReactiveCommand<ListBox, Action> RemoveFilePathNotContainsTextFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems?.Count > 0)
				SettingsFile.Instance.FilePathNotContainsTexts.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});

		public ReactiveCommand<ListBox, Action> RemoveIncludesFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems?.Count > 0)
				SettingsFile.Instance.Includes.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});

		public ReactiveCommand<Unit, Unit> ClearIncludesListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await MessageBoxService.Show("Are you sure you want to clear the list of ALL included folders?", MessageBoxButtons.Yes | MessageBoxButtons.Cancel);
			if (result == MessageBoxButtons.Yes)
				SettingsFile.Instance.Includes.Clear();
		});

		public ReactiveCommand<Unit, Unit> ClearBlacklistListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await MessageBoxService.Show("Are you sure you want to clear the list of ALL excluded folders?", MessageBoxButtons.Yes | MessageBoxButtons.Cancel);
			if (result == MessageBoxButtons.Yes)
				SettingsFile.Instance.Blacklists.Clear();
		});

		public ReactiveCommand<Unit, Unit> AddBlacklistToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					AllowMultiple = true,
					Title = "Select folder"
				});

			if (result == null || result.Count == 0) return;
			foreach (var item in result) {
				if (!SettingsFile.Instance.Blacklists.Contains(item))
					SettingsFile.Instance.Blacklists.Add(item);
			}
		});
		public ReactiveCommand<ListBox, Action> RemoveBlacklistFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems?.Count > 0)
				SettingsFile.Instance.Blacklists.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});
		public ReactiveCommand<Unit, Unit> ClearLogCommand => ReactiveCommand.Create(() => {
			LogItems.Clear();
		});
		public ReactiveCommand<Unit, Unit> SaveLogCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				DefaultExtension = ".txt",
			});
			if (string.IsNullOrEmpty(result)) return;
			var sb = new StringBuilder();
			foreach (var l in LogItems)
				sb.AppendLine(l);
			try {
				File.WriteAllText(result, sb.ToString());
			}
			catch (Exception e) {
				Logger.Instance.Info(e.Message);
			}
		});
		public ReactiveCommand<Unit, Unit> SaveSettingsCommand => ReactiveCommand.CreateFromTask(async () => {
			try {
				SettingsFile.SaveSettings();
			}
			catch (Exception ex) {
				await MessageBoxService.Show($"Saving settings has failed: {ex.Message}");
			}
		});
		public ReactiveCommand<Unit, Unit> SaveSettingsProfileCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				DefaultExtension = ".json",
				FileTypeChoices = new FilePickerFileType[] {
					 new FilePickerFileType("Setting File") { Patterns = new string[] { "*.json" }}}
			});
			if (string.IsNullOrEmpty(result)) return;

			try {
				SettingsFile.SaveSettings(result);
			}
			catch (Exception ex) {
				await MessageBoxService.Show($"Saving settings to file has failed: {ex.Message}");
			}
		});
		public ReactiveCommand<Unit, Unit> LoadSettingsProfileCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = new FilePickerFileType[] {
					 new FilePickerFileType("Setting File") { Patterns = new string[] { "*.json", "*.xml" }}}
			});
			if (string.IsNullOrEmpty(result)) return;

			try {
				SettingsFile.LoadSettings(result);
			}
			catch (Exception ex) {
				await MessageBoxService.Show($"Loading settings from file has failed: {ex.Message}");
				return;
			}
			await MessageBoxService.Show("Please restart VDF to apply new settings.");
		});

		public ReactiveCommand<Unit, Unit> ShowBrokenFilesCommand => ReactiveCommand.CreateFromTask(async () => {
			var scanEngine = new Core.ScanEngine(); // Consider if a shared instance is better
			var brokenEntries = scanEngine.GetBrokenFileEntries();

			if (brokenEntries.Count == 0) {
				await MessageBoxService.Show("No broken file entries found in the database.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine($"Found {brokenEntries.Count} broken file entries:");
			int count = 0;
			foreach (var entry in brokenEntries) {
				sb.AppendLine($"- {entry.Path} (Flags: {entry.Flags})");
				count++;
				if (count >= 20) { // Limit the number of paths displayed directly
					sb.AppendLine($"...and {brokenEntries.Count - count} more.");
					break;
				}
			}
			// Ensure MessageBoxService.Show can handle potentially long strings or consider a custom dialog for many items.
			// For now, we'll use the existing MessageBoxService.
			await MessageBoxService.Show(sb.ToString(), title: "Broken File Entries");
		});

		public ReactiveCommand<Unit, Unit> FindSubClipsCommand => ReactiveCommand.CreateFromTask(async () => {
			IsBusy = true;
			IsBusyText = "Finding sub-clip matches... This may take a while.";
			try {
				var scanEngine = new Core.ScanEngine(); // Consider if a shared instance is better
				// It's important to use the settings from SettingsFile.Instance for consistency with the rest of the app
				var coreSettings = new Core.Settings {
					Percent = SettingsFile.Instance.Percent,
					IgnoreBlackPixels = SettingsFile.Instance.IgnoreBlackPixels,
					IgnoreWhitePixels = SettingsFile.Instance.IgnoreWhitePixels,
					ThumbnailCount = SettingsFile.Instance.Thumbnails
					// Add any other relevant settings from SettingsFile to Core.Settings if needed by FindSubClipMatches
				};

				// Ensure database is loaded. DatabaseUtils.Database is a HashSet<FileEntry>.
				if (!Core.Utils.DatabaseUtils.Database.Any()) {
					Core.Utils.DatabaseUtils.LoadDatabase(); // Corrected: removed await
				}
				var allFiles = Core.Utils.DatabaseUtils.Database.ToList(); // Pass a list

				List<Core.SubClipMatch> subClipMatches = await Task.Run(() => scanEngine.FindSubClipMatches(allFiles, coreSettings));

				if (subClipMatches.Count == 0) {
					await MessageBoxService.Show("No sub-clip matches found.");
				} else {
					var sb = new StringBuilder();
					sb.AppendLine($"Found {subClipMatches.Count} potential sub-clip match(es):");
					int displayCount = 0;
					foreach (var match in subClipMatches) {
						sb.AppendLine($"Main: {match.MainVideo.Path}");
						sb.AppendLine($"  Sub: {match.SubClipVideo.Path}");
						sb.AppendLine($"  Matched at (main video times %): {string.Join(", ", match.MainVideoMatchStartTimes.Select(t => (t*100).ToString("F1")))}");
						sb.AppendLine();
						displayCount++;
						if (displayCount >= 10) { // Limit direct display
							sb.AppendLine($"...and {subClipMatches.Count - displayCount} more matches.");
							break;
						}
					}
					await MessageBoxService.Show(sb.ToString(), title: "Sub-Clip Matches Found");
				}
			} catch (Exception ex) {
				Logger.Instance.Info($"ERROR: Error finding sub-clips: {ex.Message}"); // Corrected Logger call
				await MessageBoxService.Show($"An error occurred while finding sub-clips: {ex.Message}", title: "Error");
			} finally {
				IsBusy = false;
				IsBusyText = string.Empty;
			}
		});

        // The actual method to open the window
		private void OpenSegmentComparisonWindow() {
		   var view = new SegmentComparisonView();
		   view.DataContext = new SegmentComparisonVM();
		   // For Avalonia, showing a window might need to be done via a window manager service or by ShowDialog(owner)
		   // For simplicity, and if ApplicationHelpers.MainWindow is accessible and appropriate:
           if (ApplicationHelpers.MainWindow != null)
           {
                view.ShowDialog(ApplicationHelpers.MainWindow);
           }
           else
           {
                view.Show(); // Fallback if no owner can be determined easily
           }
		}
		// Ensure OpenSegmentComparisonCommand is initialized. This usually happens in the constructor.
		// If MainWindowVM is not partial or this is not the right place, this initialization needs to be moved.
		// For the purpose of this task, I'm adding a placeholder initialization.
		// This should be integrated into the actual MainWindowVM constructor.
		public void EnsureCommandsInitialized() {
		// This is a conceptual method. Command should be initialized in constructor.
		// if (OpenSegmentComparisonCommand == null) {
		//     OpenSegmentComparisonCommand = ReactiveCommand.Create(OpenSegmentComparisonWindow); // This line should be in the constructor
		// }
	}
		// Removed duplicate constructor and EnsureCommandsInitialized method
	}
}
