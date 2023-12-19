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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {
		public ScanEngine Scanner { get; } = new();
		public ObservableCollection<string> LogItems { get; } = new();
		List<HashSet<string>> GroupBlacklist = new();
		public string BackupScanResultsFile =>
			Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder) ?
			Path.Combine(SettingsFile.Instance.CustomDatabaseFolder, "backup.scanresults") :
			Path.Combine(CoreUtils.CurrentFolder, "backup.scanresults");

		public ObservableCollection<DuplicateItemVM> Duplicates { get; } = new();


		bool _IsScanning;
		public bool IsScanning {
			get => _IsScanning;
			set => this.RaiseAndSetIfChanged(ref _IsScanning, value);
		}
		bool _IsReadyToCompare;
		public bool IsReadyToCompare {
			get => _IsReadyToCompare;
			set => this.RaiseAndSetIfChanged(ref _IsReadyToCompare, value);
		}
		bool _IsGathered;
		public bool IsGathered {
			get => _IsGathered;
			set => this.RaiseAndSetIfChanged(ref _IsGathered, value);
		}
		bool _IsPaused;
		public bool IsPaused {
			get => _IsPaused;
			set => this.RaiseAndSetIfChanged(ref _IsPaused, value);
		}

		string _ScanProgressText = string.Empty;
		public string ScanProgressText {
			get => _ScanProgressText;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressText, value);
		}
		TimeSpan _RemainingTime;
		public TimeSpan RemainingTime {
			get => _RemainingTime;
			set => this.RaiseAndSetIfChanged(ref _RemainingTime, value);
		}

		TimeSpan _TimeElapsed;
		public TimeSpan TimeElapsed {
			get => _TimeElapsed;
			set => this.RaiseAndSetIfChanged(ref _TimeElapsed, value);
		}
		int _ScanProgressValue;
		public int ScanProgressValue {
			get => _ScanProgressValue;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressValue, value);
		}
		bool _IsBusy;
		public bool IsBusy {
			get => _IsBusy;
			set => this.RaiseAndSetIfChanged(ref _IsBusy, value);
		}
		string _BusyText = string.Empty;
		public string IsBusyText {
			get => _BusyText;
			set => this.RaiseAndSetIfChanged(ref _BusyText, value);
		}
		int _ScanProgressMaxValue = 100;
		public int ScanProgressMaxValue {
			get => _ScanProgressMaxValue;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressMaxValue, value);
		}
		int _TotalDuplicates;
		public int TotalDuplicates {
			get => _TotalDuplicates;
			set => this.RaiseAndSetIfChanged(ref _TotalDuplicates, value);
		}
		int _TotalDuplicateGroups;
		public int TotalDuplicateGroups {
			get => _TotalDuplicateGroups;
			set => this.RaiseAndSetIfChanged(ref _TotalDuplicateGroups, value);
		}
		string _TotalDuplicatesSize = string.Empty;
		public string TotalDuplicatesSize {
			get => _TotalDuplicatesSize;
			set => this.RaiseAndSetIfChanged(ref _TotalDuplicatesSize, value);
		}
		long _TotalSizeRemovedInternal;
		long TotalSizeRemovedInternal {
			get => _TotalSizeRemovedInternal;
			set {
				_TotalSizeRemovedInternal = value;
				this.RaisePropertyChanged(nameof(TotalSizeRemoved));
			}
		}
		int _DuplicatesSelectedCounter;
		public int DuplicatesSelectedCounter {
			get => _DuplicatesSelectedCounter;
			set => this.RaiseAndSetIfChanged(ref _DuplicatesSelectedCounter, value);
		}
		public bool IsMultiOpenSupported => !string.IsNullOrEmpty(SettingsFile.Instance.CustomCommands.OpenMultiple);
		public bool IsMultiOpenInFolderSupported => !string.IsNullOrEmpty(SettingsFile.Instance.CustomCommands.OpenMultipleInFolder);


		public bool IsWindows => CoreUtils.IsWindows;

		public string TotalSizeRemoved => TotalSizeRemovedInternal.BytesToString();
#if DEBUG
		public static bool IsDebug => true;
#else
		public static bool IsDebug => false;
#endif


		public MainWindowVM() {
			FileInfo groupBlacklistFile = new(FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "BlacklistedGroups.json"));
			if (groupBlacklistFile.Exists && groupBlacklistFile.Length > 0) {
				using var stream = new FileStream(groupBlacklistFile.FullName, FileMode.Open);
				GroupBlacklist = JsonSerializer.Deserialize<List<HashSet<string>>>(stream)!;
			}
			_FileType = TypeFilters[0];
			Scanner.ScanAborted += Scanner_ScanAborted;
			Scanner.ScanDone += Scanner_ScanDone;
			Scanner.Progress += Scanner_Progress;
			Scanner.ThumbnailsRetrieved += Scanner_ThumbnailsRetrieved;
			Scanner.DatabaseCleaned += Scanner_DatabaseCleaned;
			Scanner.FilesEnumerated += Scanner_FilesEnumerated;
			Scanner.NoThumbnailImage = SixLabors.ImageSharp.Image.Load(AssetLoader.Open(new Uri("avares://VDF.GUI/Assets/icon.png")));

			try {
				File.Delete(Path.Combine(CoreUtils.CurrentFolder, "log.txt"));
			}
			catch { }
			Logger.Instance.LogItemAdded += Instance_LogItemAdded;
			//Ensure items added before GUI was ready will be shown
			Instance_LogItemAdded(string.Empty);
			if (File.Exists(BackupScanResultsFile))
				ImportScanResultsIncludingThumbnails(BackupScanResultsFile);

			Duplicates.CollectionChanged += Duplicates_CollectionChanged;

			SortOrders = new KeyValuePair<string, DataGridSortDescription>[] {
				new KeyValuePair<string, DataGridSortDescription>("None", null!),
				new KeyValuePair<string, DataGridSortDescription>("Size Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.SizeLong)}", ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Size Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.SizeLong)}", ListSortDirection.Descending)),
				new KeyValuePair<string, DataGridSortDescription>("Resolution Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.FrameSizeInt)}", ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Resolution Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.FrameSizeInt)}", ListSortDirection.Descending)),
				new KeyValuePair<string, DataGridSortDescription>("Duration Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Duration)}", ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Duration Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Duration)}", ListSortDirection.Descending)),
				new KeyValuePair<string, DataGridSortDescription>("Date Created Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.DateCreated)}", ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Date Created Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.DateCreated)}", ListSortDirection.Descending)),
				new KeyValuePair<string, DataGridSortDescription>("Similarity Ascending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Similarity)}", ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Similarity Descending",
				DataGridSortDescription.FromPath($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.Similarity)}", ListSortDirection.Descending)),
				new KeyValuePair<string, DataGridSortDescription>("Group Has Selected Items Ascending",
				DataGridSortDescription.FromComparer(new CheckedGroupsComparer(this), ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Group Has Selected Items Descending",
				DataGridSortDescription.FromComparer(new CheckedGroupsComparer(this), ListSortDirection.Descending)),
				new KeyValuePair<string, DataGridSortDescription>("Group Size Ascending",
				DataGridSortDescription.FromComparer(new GroupSizeComparer(this), ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Group Size Descending",
				DataGridSortDescription.FromComparer(new GroupSizeComparer(this), ListSortDirection.Descending)),
			};
			_SortOrder = SortOrders[0];
		}

		void Duplicates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
			if (e.OldItems != null) {
				foreach (INotifyPropertyChanged item in e.OldItems) {
					item.PropertyChanged -= DuplicateItemVM_PropertyChanged;
					if (((DuplicateItemVM)item).Checked)
						DuplicatesSelectedCounter--;
				}
			}
			if (e.NewItems != null) {
				foreach (INotifyPropertyChanged item in e.NewItems)
					item.PropertyChanged += DuplicateItemVM_PropertyChanged;
			}
			if (e.Action == NotifyCollectionChangedAction.Reset)
				DuplicatesSelectedCounter = 0;
		}

		public async void Thumbnails_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
			bool isReadyToCompare = IsGathered;
			isReadyToCompare &= Scanner.Settings.ThumbnailCount == e.NewValue;
			if (!isReadyToCompare && ApplicationHelpers.MainWindowDataContext.IsReadyToCompare)
				await MessageBoxService.Show($"Number of thumbnails can't be changed between quick rescans. Full scan will be required.");
			ApplicationHelpers.MainWindowDataContext.IsReadyToCompare = isReadyToCompare;
		}

		void DuplicateItemVM_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			if (e.PropertyName != nameof(DuplicateItemVM.Checked) || sender == null) return;
			if (((DuplicateItemVM)sender).Checked)
				DuplicatesSelectedCounter++;
			else
				DuplicatesSelectedCounter--;
		}

		void Scanner_ThumbnailsRetrieved(object? sender, EventArgs e) {
			//Reset properties
			ScanProgressText = string.Empty;
			RemainingTime = new TimeSpan();
			ScanProgressValue = 0;
			ScanProgressMaxValue = 100;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
			if (SettingsFile.Instance.BackupAfterListChanged)
				ExportScanResultsIncludingThumbnails(BackupScanResultsFile);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
		}

		void Scanner_FilesEnumerated(object? sender, EventArgs e) => IsBusy = false;

		async void Scanner_DatabaseCleaned(object? sender, EventArgs e) {
			IsBusy = false;
			await MessageBoxService.Show("Database cleaned!");
		}

		public async Task<bool> SaveScanResults() {
			if (Duplicates.Count == 0 || !SettingsFile.Instance.AskToSaveResultsOnExit) {
				return true;
			}
			MessageBoxButtons? result = await MessageBoxService.Show("Do you want to save the results and continue next time you start VDF?",
				MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
			if (result == null || result == MessageBoxButtons.Cancel) {
				//Can be NULL if user closed the window by clicking on 'X'
				return false;
			}
			if (result != MessageBoxButtons.Yes) {
				return true;
			}
			await ExportScanResultsIncludingThumbnails(BackupScanResultsFile);
			return true;
		}



		public async void LoadDatabase() {
			IsBusy = true;
			IsBusyText = "Loading database...";
			bool success = await ScanEngine.LoadDatabase();
			IsBusy = false;
			if (!success) {
				await MessageBoxService.Show("Failed to load database of scanned files. Please see log file in VDF directory");
				Environment.Exit(-1);
			}
		}

		void Scanner_Progress(object? sender, ScanProgressChangedEventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				ScanProgressText = e.CurrentFile;
				RemainingTime = e.Remaining;
				ScanProgressValue = e.CurrentPosition;
				TimeElapsed = e.Elapsed;
				ScanProgressMaxValue = e.MaxPosition;
			});



		void Scanner_ScanAborted(object? sender, EventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				IsScanning = false;
				IsBusy = false;
				IsReadyToCompare = false;
				IsGathered = false;
			});
		void Scanner_ScanDone(object? sender, EventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				IsScanning = false;
				IsBusy = false;
				IsReadyToCompare = true;
				IsGathered = true;

				Scanner.Duplicates.RemoveWhere(a => {
					foreach (HashSet<string> blackListedGroup in GroupBlacklist) {
						if (!blackListedGroup.Contains(a.Path)) continue;
						bool isBlacklisted = true;
						foreach (DuplicateItem blackListItem in Scanner.Duplicates.Where(b => b.GroupId == a.GroupId)) {
							if (!blackListedGroup.Contains(blackListItem.Path)) {
								isBlacklisted = false;
								break;
							}
						}
						if (isBlacklisted) return true;
					}
					return false;
				});


				foreach (var item in Scanner.Duplicates) {
					Duplicates.Add(new DuplicateItemVM(item));
				}

				if (SettingsFile.Instance.GeneratePreviewThumbnails)
					Scanner.RetrieveThumbnails();

				BuildDuplicatesView();
			});
		void BuildDuplicatesView() {
			view = new DataGridCollectionView(Duplicates);
			view.GroupDescriptions.Add(new DataGridPathGroupDescription($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.GroupId)}"));
			view.Filter += DuplicatesFilter;
			GetDataGrid.ItemsSource = view;
			TotalDuplicates = Duplicates.Count;
			TotalDuplicatesSize = Duplicates.Sum(x => x.ItemInfo.SizeLong).BytesToString();
			TotalSizeRemovedInternal = 0;
			TotalDuplicateGroups = Duplicates.GroupBy(x => x.ItemInfo.GroupId).Count();
		}


		static DataGrid GetDataGrid => ApplicationHelpers.MainWindow.FindControl<DataGrid>("dataGridGrouping")!;



		public static ReactiveCommand<Unit, Unit> LatestReleaseCommand => ReactiveCommand.CreateFromTask(async () => {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "https://github.com/0x90d/videoduplicatefinder/releases",
					UseShellExecute = true
				});
			}
			catch {
				await MessageBoxService.Show("Failed to open URL: https://github.com/0x90d/videoduplicatefinder/releases");
			}
		});
		public static ReactiveCommand<Unit, Unit> OpenOwnFolderCommand => ReactiveCommand.Create(() => {
			Process.Start(new ProcessStartInfo {
				FileName = CoreUtils.CurrentFolder,
				UseShellExecute = true,
			});
		});
		public ReactiveCommand<Unit, Unit> CleanDatabaseCommand => ReactiveCommand.Create(() => {
			IsBusy = true;
			IsBusyText = "Cleaning database...";
			Scanner.CleanupDatabase();
		});
		public ReactiveCommand<Unit, Unit> ClearDatabaseCommand => ReactiveCommand.CreateFromTask(async () => {
			MessageBoxButtons? dlgResult = await MessageBoxService.Show(
				"WARNING: This will delete all stored data in your database. Do you want to continue?",
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;
			ScanEngine.ClearDatabase();
			await MessageBoxService.Show("Done!");
		});
		public static ReactiveCommand<Unit, Unit> EditDataBaseCommand => ReactiveCommand.CreateFromTask(async () => {
			DatabaseViewer dlg = new();
			bool res = await dlg.ShowDialog<bool>(ApplicationHelpers.MainWindow);
		});
		public static ReactiveCommand<Unit, Unit> ImportDataBaseFromJsonCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = new FilePickerFileType[] {
					 new FilePickerFileType("Json File") { Patterns = new string[] { "*.json" }}}
			});
			if (string.IsNullOrEmpty(result)) return;

			bool success = ScanEngine.ImportDataBaseFromJson(result, new JsonSerializerOptions {
				IncludeFields = true,
			});
			if (!success)
				await MessageBoxService.Show("Importing database has failed, please see log");
			else
				ScanEngine.SaveDatabase();
		});
		public static ReactiveCommand<Unit, Unit> ExportDataBaseToJsonCommand => ReactiveCommand.Create(() => {
			ExportDbToJson(new JsonSerializerOptions {
				IncludeFields = true,
			});
		});
		public static ReactiveCommand<Unit, Unit> ExportDataBaseToJsonPrettyCommand => ReactiveCommand.Create(() => {
			ExportDbToJson(new JsonSerializerOptions {
				IncludeFields = true,
				WriteIndented = true,
			});
		});
		async static void ExportDbToJson(JsonSerializerOptions options) {

			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				DefaultExtension = ".json",
				FileTypeChoices = new FilePickerFileType[] {
					 new FilePickerFileType("Json Files") { Patterns = new string[] { "*.json" }}}
			});
			if (string.IsNullOrEmpty(result)) return;

			if (!ScanEngine.ExportDataBaseToJson(result, options))
				await MessageBoxService.Show("Exporting database has failed, please see log");
		}
		public ReactiveCommand<Unit, Unit> ExportScanResultsCommand => ReactiveCommand.Create(() => {
			ExportScanResultsToJson(new JsonSerializerOptions {
				IncludeFields = true,
			});
		});
		public ReactiveCommand<Unit, Unit> ExportScanResultsPrettyCommand => ReactiveCommand.Create(() => {
			ExportScanResultsToJson(new JsonSerializerOptions {
				IncludeFields = true,
				WriteIndented = true,
			});
		});
		async void ExportScanResultsToJson(JsonSerializerOptions options) {
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				DefaultExtension = ".json",
				FileTypeChoices = new FilePickerFileType[] {
					 new FilePickerFileType("Json Files") { Patterns = new string[] { "*.json" }}}
			});
			if (string.IsNullOrEmpty(result)) return;

			options.Converters.Add(new ImageJsonConverter());
			try {
				List<DuplicateItem> list = Duplicates.Select(x => x.ItemInfo).OrderBy(x => x.GroupId).ToList();
				using var stream = File.OpenWrite(result);
				await JsonSerializer.SerializeAsync(stream, list, options);
				stream.Close();
			}
			catch (Exception ex) {
				await MessageBoxService.Show($"Exporting scan results has failed because of {ex}");
			}
		}
		public ReactiveCommand<Unit, Unit> ExportScanResultsToFileCommand => ReactiveCommand.CreateFromTask(async () => {
			await ExportScanResultsIncludingThumbnails();
		});
		async Task ExportScanResultsIncludingThumbnails(string? path = null) {
			path ??= await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				DefaultExtension = ".json",
				FileTypeChoices = new FilePickerFileType[] {
					 new FilePickerFileType("Scan Results") { Patterns = new string[] { "*.scanresults" }}}
			});

			if (string.IsNullOrEmpty(path)) return;

			try {
				using var stream = File.OpenWrite(path);
				var options = new JsonSerializerOptions {
					IncludeFields = true,
				};
				options.Converters.Add(new BitmapJsonConverter());
				options.Converters.Add(new ImageJsonConverter());
				IsBusy = true;
				IsBusyText = "Saving scan results to disk...";
				await JsonSerializer.SerializeAsync(stream, Duplicates, options);
				IsBusy = false;
				stream.Close();
			}
			catch (Exception ex) {
				IsBusy = false;
				string error = $"Exporting scan results has failed because of {ex}";
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
		}
		public ReactiveCommand<Unit, Unit> ImportScanResultsFromFileCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = new FilePickerFileType[] {
					 new FilePickerFileType("Scan Results") { Patterns = new string[] { "*.scanresults" }}}
			});
			if (string.IsNullOrEmpty(result)) return;
			ImportScanResultsIncludingThumbnails(result);
		});
		async void ImportScanResultsIncludingThumbnails(string? path = null) {
			if (Duplicates.Count > 0) {
				MessageBoxButtons? result = await MessageBoxService.Show($"Importing scan results will clear the current list, continue?", MessageBoxButtons.Yes | MessageBoxButtons.No);
				if (result != MessageBoxButtons.Yes) return;
			}

			if (path == null) {
				path = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
					SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
					FileTypeFilter = new FilePickerFileType[] {
					 new FilePickerFileType("Scan Results") { Patterns = new string[] { "*.scanresults" }}}
				});
			}
			if (string.IsNullOrEmpty(path)) return;

			try {
				using var stream = File.OpenRead(path);
				var options = new JsonSerializerOptions {
					IncludeFields = true,
				};
				options.Converters.Add(new BitmapJsonConverter());
				options.Converters.Add(new ImageJsonConverter());
				IsBusy = true;
				IsBusyText = "Importing scan results from disk...";
				List<DuplicateItemVM>? list = await JsonSerializer.DeserializeAsync<List<DuplicateItemVM>>(stream, options);
				Duplicates.Clear();
				if (list != null)
					foreach (var dupItem in list)
						Duplicates.Add(dupItem);

				BuildDuplicatesView();
				IsBusy = false;
				stream.Close();
			}
			catch (Exception ex) {
				IsBusy = false;
				string error = $"Importing scan results has failed because of {ex}";
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
		}

		public static ReactiveCommand<DuplicateItemVM, Unit> OpenItemCommand => ReactiveCommand.Create<DuplicateItemVM>(currentItem => {
			OpenItems();
		});

		public static ReactiveCommand<Unit, Unit> OpenItemInFolderCommand => ReactiveCommand.Create(() => {
			OpenItemsInFolder();
		});

		public static ReactiveCommand<Unit, Unit> OpenItemsByColIdCommand => ReactiveCommand.Create(() => {
			if (GetDataGrid.CurrentColumn.DisplayIndex == 1)
				OpenItems();
			else if (GetDataGrid.CurrentColumn.DisplayIndex == 2)
				OpenItemsInFolder();
		});

		public ReactiveCommand<string, Unit> OpenGroupCommand => ReactiveCommand.Create<string>(openInFolder => {
			if (GetDataGrid.SelectedItem is DuplicateItemVM currentItem) {
				List<DuplicateItemVM> items = Duplicates.Where(s => s.ItemInfo.GroupId == currentItem.ItemInfo.GroupId).ToList();
				if (openInFolder == "0")
					AlternativeOpen(String.Empty, SettingsFile.Instance.CustomCommands.OpenMultiple, items);
				else
					AlternativeOpen(String.Empty, SettingsFile.Instance.CustomCommands.OpenMultipleInFolder, items);
			}
		});

		public static void OpenItems() {
			if (AlternativeOpen(SettingsFile.Instance.CustomCommands.OpenItem,
								SettingsFile.Instance.CustomCommands.OpenMultiple))
				return;

			if (GetDataGrid.SelectedItem is not DuplicateItemVM currentItem) return;
			if (CoreUtils.IsWindows) {
				Process.Start(new ProcessStartInfo {
					FileName = currentItem.ItemInfo.Path,
					UseShellExecute = true
				});
			}
			else {
				Process.Start(new ProcessStartInfo {
					FileName = currentItem.ItemInfo.Path,
					UseShellExecute = true,
					Verb = "open"
				});
			}
		}

		public static void OpenItemsInFolder() {
			if (AlternativeOpen(SettingsFile.Instance.CustomCommands.OpenItemInFolder,
								SettingsFile.Instance.CustomCommands.OpenMultipleInFolder))
				return;

			if (GetDataGrid.SelectedItem is not DuplicateItemVM currentItem) return;
			if (CoreUtils.IsWindows) {
				Process.Start(new ProcessStartInfo("explorer.exe", $"/select, \"{currentItem.ItemInfo.Path}\"") {
					UseShellExecute = true
				});
			}
			else {
				Process.Start(new ProcessStartInfo {
					FileName = currentItem.ItemInfo.Folder,
					UseShellExecute = true,
					Verb = "open"
				});
			}
		}

		private static bool AlternativeOpen(string cmdSingle, string cmdMulti, List<DuplicateItemVM>? items = null) {
			if (string.IsNullOrEmpty(cmdSingle) && string.IsNullOrEmpty(cmdMulti))
				return false;

			if (items == null) {
				items = new();
				if (!string.IsNullOrEmpty(cmdMulti)) {
					foreach (var selectedItem in GetDataGrid.SelectedItems)
						if (selectedItem is DuplicateItemVM item)
							items.Add(item);
				}
				else {
					if (GetDataGrid.SelectedItem is DuplicateItemVM duplicateItem)
						items.Add(duplicateItem);
				}
			}

			string[]? cmd = null;
			string command = string.Empty;
			if (!string.IsNullOrEmpty(cmdSingle) && (string.IsNullOrEmpty(cmdMulti) || items.Count == 1))
				command = cmdSingle;
			else if (items.Count > 1)
				command = cmdMulti;
			if (!string.IsNullOrEmpty(command)) {
				if (command[0] == '"' || command[0] == '\'') {  // -> when spaces in command part: "c:/my folder/prog.exe"
					cmd = command.Split(command[0] + " ", 2);
					cmd[0] += command[0];
				}
				else
					cmd = command.Split(' ', 2);
			}
			if (string.IsNullOrEmpty(cmd?[0]))
				return false;

			command = cmd[0];
			string args = string.Empty;
			items.ForEach(item => args += $"\"{item.ItemInfo.Path}\" ");
			if (cmd.Length == 2)
				if (cmd[1].Contains("%f"))
					args = cmd[1].Replace("%f", args);  // %f in user command string is the placeholder for the file(s)
				else
					args = cmd[1] + " " + args;         // otherwise simply attach

			try {
				Process.Start(new ProcessStartInfo {
					FileName = command,
					Arguments = args,
					UseShellExecute = false,
					RedirectStandardError = true,
				});
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed to run custom command: {command}\n Arguments: {args}\nException: {e.Message}");
			}

			return true;
		}

		public static ReactiveCommand<Unit, Unit> RenameFileCommand => ReactiveCommand.CreateFromTask(async () => {
			if (GetDataGrid.SelectedItem is not DuplicateItemVM currentItem) return;
			var fi = new FileInfo(currentItem.ItemInfo.Path);
			Debug.Assert(fi.Directory != null, "fi.Directory != null");
			string newName = await InputBoxService.Show("Enter new name", Path.GetFileNameWithoutExtension(fi.FullName), title: "Rename File");
			if (string.IsNullOrEmpty(newName)) return;
			newName = FileUtils.SafePathCombine(fi.DirectoryName!, newName + fi.Extension);
			while (File.Exists(newName)) {
				MessageBoxButtons? result = await MessageBoxService.Show($"A file with the name '{Path.GetFileName(newName)}' already exists. Do you want to overwrite this file? Click on 'No' to enter a new name", MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
				if (result == null || result == MessageBoxButtons.Cancel)
					return;
				if (result == MessageBoxButtons.Yes)
					break;
				newName = await InputBoxService.Show("Enter new name", Path.GetFileNameWithoutExtension(newName), title: "Rename File");
				if (string.IsNullOrEmpty(newName))
					return;
				newName = FileUtils.SafePathCombine(fi.DirectoryName!, newName + fi.Extension);
			}
			try {
				ScanEngine.GetFromDatabase(currentItem.ItemInfo.Path, out var dbEntry);
				fi.MoveTo(newName, true);
				ScanEngine.UpdateFilePathInDatabase(newName, dbEntry);
				currentItem.ItemInfo.Path = newName;
				ScanEngine.SaveDatabase();
			}
			catch (Exception e) {
				await MessageBoxService.Show(e.Message);
			}
		});

		public static ReactiveCommand<Unit, Unit> ToggleCheckboxCommand => ReactiveCommand.Create(() => {
			foreach (var item in GetDataGrid.SelectedItems) {
				if (item is not DuplicateItemVM currentItem) return;
				currentItem.Checked = !currentItem.Checked;
			}
		});



		public ReactiveCommand<string, Unit> StartScanCommand => ReactiveCommand.CreateFromTask(async (string command) => {
			if (!string.IsNullOrEmpty(SettingsFile.Instance.CustomDatabaseFolder) && !Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder)) {
				await MessageBoxService.Show("The custom database folder does not exist!");
				return;
			}

			if (!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists) {
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					await MessageBoxService.Show("Cannot find FFmpeg executable. The easiest solution is to download ffmpeg and place it in VDF 'bin' folder. Otherwise please follow instructions on Github and restart VDF");
				}
				else {
					await MessageBoxService.Show("Cannot find FFmpeg. Please follow instructions on Github and restart VDF");
				}
				return;
			}
			if (SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) {
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					if (await MessageBoxService.Show("Cannot find shared FFmpeg libraries. Would you like to open the download page? Otherwise either uncheck 'Use native ffmpeg binding' in settings or please follow instructions on Github and restart VDF.", MessageBoxButtons.Yes | MessageBoxButtons.No) == MessageBoxButtons.Yes) {
						try {
							int ffmpegMajorVersion = FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avcodec"] / 10;
							Process.Start(new ProcessStartInfo($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{ffmpegMajorVersion}.0-latest-win64-gpl-shared-{ffmpegMajorVersion}.0.zip") {
								UseShellExecute = true
							});
							await MessageBoxService.Show("After downloading, please extract and copy the 'bin' (!!) folder into VDF folder. Then restart VDF");
						}
						catch {
							await MessageBoxService.Show("Failed to open download page. Please visit: https://github.com/BtbN/FFmpeg-Builds/releases");
						}
					}
				}
				else {
					await MessageBoxService.Show("Cannot find shared FFmpeg libraries. Either uncheck 'Use native ffmpeg binding' in settings or please follow instructions on Github and restart VDF");
				}
				return;
			}
			if (!ScanEngine.FFprobeExists) {
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					await MessageBoxService.Show("Cannot find FFprobe executable. The easiest solution is to download ffmpeg/ffprobe and place it in VDF 'bin' folder. Otherwise please follow instructions on Github and restart VDF");
				}
				else {
					await MessageBoxService.Show("Cannot find FFprobe. Please follow instructions on Github and restart VDF");
				}
				return;
			}
			if (SettingsFile.Instance.UseNativeFfmpegBinding && SettingsFile.Instance.HardwareAccelerationMode == Core.FFTools.FFHardwareAccelerationMode.auto) {
				await MessageBoxService.Show("You cannot use hardware acceleration mode 'auto' with native ffmpeg bindings. Please explicit set a mode or set it to 'none'.");
				return;
			}
			if (SettingsFile.Instance.Includes.Count == 0) {
				await MessageBoxService.Show("There are no folders to scan. Please go to the settings and add at least one folder to 'Search Directories'.");
				return;
			}
			if (SettingsFile.Instance.MaxDegreeOfParallelism == 0) {
				await MessageBoxService.Show("MaxDegreeOfParallelism cannot be 0. Please go to the settings and change it.");
				return;
			}
			if (SettingsFile.Instance.FilterByFileSize && SettingsFile.Instance.MaximumFileSize <= SettingsFile.Instance.MinimumFileSize) {
				await MessageBoxService.Show("Filtering maximum file size cannot be greater or equal minimum file size.");
				return;
			}
			bool isFreshScan = true;
			switch (command) {
			case "FullScan":
				isFreshScan = true;
				break;
			case "CompareOnly":
				isFreshScan = false;
				if (await MessageBoxService.Show("Are you sure to perform a rescan?", MessageBoxButtons.Yes | MessageBoxButtons.No) != MessageBoxButtons.Yes)
					return;
				break;
			default:
				await MessageBoxService.Show("Requested command is NOT implemented yet!");
				break;
			}


			Duplicates.Clear();
			IsScanning = true;
			IsReadyToCompare = false;
			IsGathered = false;
			SettingsFile.SaveSettings();
			//Set scan settings
			Scanner.Settings.IncludeSubDirectories = SettingsFile.Instance.IncludeSubDirectories;
			Scanner.Settings.IncludeImages = SettingsFile.Instance.IncludeImages;
			Scanner.Settings.GeneratePreviewThumbnails = SettingsFile.Instance.GeneratePreviewThumbnails;
			Scanner.Settings.IgnoreReadOnlyFolders = SettingsFile.Instance.IgnoreReadOnlyFolders;
			Scanner.Settings.IgnoreReparsePoints = SettingsFile.Instance.IgnoreReparsePoints;
			Scanner.Settings.ExcludeHardLinks = SettingsFile.Instance.ExcludeHardLinks;
			Scanner.Settings.HardwareAccelerationMode = SettingsFile.Instance.HardwareAccelerationMode;
			Scanner.Settings.Percent = SettingsFile.Instance.Percent;
			Scanner.Settings.PercentDurationDifference = SettingsFile.Instance.PercentDurationDifference;
			Scanner.Settings.MaxDegreeOfParallelism = SettingsFile.Instance.MaxDegreeOfParallelism;
			Scanner.Settings.ThumbnailCount = SettingsFile.Instance.Thumbnails;
			Scanner.Settings.ExtendedFFToolsLogging = SettingsFile.Instance.ExtendedFFToolsLogging;
			Scanner.Settings.AlwaysRetryFailedSampling = SettingsFile.Instance.AlwaysRetryFailedSampling;
			Scanner.Settings.CustomFFArguments = SettingsFile.Instance.CustomFFArguments;
			Scanner.Settings.UseNativeFfmpegBinding = SettingsFile.Instance.UseNativeFfmpegBinding;
			Scanner.Settings.IgnoreBlackPixels = SettingsFile.Instance.IgnoreBlackPixels;
			Scanner.Settings.IgnoreWhitePixels = SettingsFile.Instance.IgnoreWhitePixels;
			Scanner.Settings.CompareHorizontallyFlipped = SettingsFile.Instance.CompareHorizontallyFlipped;
			Scanner.Settings.CustomDatabaseFolder = SettingsFile.Instance.CustomDatabaseFolder;
			Scanner.Settings.IncludeNonExistingFiles = SettingsFile.Instance.IncludeNonExistingFiles;
			Scanner.Settings.FilterByFilePathContains = SettingsFile.Instance.FilterByFilePathContains;
			Scanner.Settings.FilePathContainsTexts = SettingsFile.Instance.FilePathContainsTexts.ToList();
			Scanner.Settings.FilterByFilePathNotContains = SettingsFile.Instance.FilterByFilePathNotContains;
			Scanner.Settings.ScanAgainstEntireDatabase = SettingsFile.Instance.ScanAgainstEntireDatabase;
			Scanner.Settings.FilePathNotContainsTexts = SettingsFile.Instance.FilePathNotContainsTexts.ToList();
			Scanner.Settings.FilterByFileSize = SettingsFile.Instance.FilterByFileSize;
			Scanner.Settings.MaximumFileSize = SettingsFile.Instance.MaximumFileSize;
			Scanner.Settings.MinimumFileSize = SettingsFile.Instance.MinimumFileSize;
			Scanner.Settings.IncludeList.Clear();
			foreach (var s in SettingsFile.Instance.Includes)
				Scanner.Settings.IncludeList.Add(s);
			Scanner.Settings.BlackList.Clear();
			foreach (var s in SettingsFile.Instance.Blacklists)
				Scanner.Settings.BlackList.Add(s);

			//Start scan
			if (isFreshScan) {
				IsBusy = true;
				IsBusyText = "Enumerating files...";
				Scanner.StartSearch();
			}
			else {
				Scanner.StartCompare();
			}
		});
		public ReactiveCommand<Unit, Unit> PauseScanCommand => ReactiveCommand.Create(() => {
			Scanner.Pause();
			IsPaused = true;
		}, this.WhenAnyValue(x => x.IsScanning, x => x.IsPaused, (a, b) => a && !b));
		public ReactiveCommand<Unit, Unit> ResumeScanCommand => ReactiveCommand.Create(() => {
			IsPaused = false;
			Scanner.Resume();
		}, this.WhenAnyValue(x => x.IsScanning, x => x.IsPaused, (a, b) => a && b));
		public ReactiveCommand<Unit, Unit> StopScanCommand => ReactiveCommand.Create(() => {
			IsPaused = false;
			IsBusy = true;
			IsBusyText = "Stopping all scan threads...";
			Scanner.Stop();
		}, this.WhenAnyValue(x => x.IsScanning));

		public ReactiveCommand<Unit, Unit> MarkGroupAsNotAMatchCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(async () => {
				if (GetDataGrid.SelectedItem is not DuplicateItemVM data) return;
				HashSet<string> blacklist = new HashSet<string>();
				foreach (DuplicateItemVM duplicateItem in Duplicates.Where(a => a.ItemInfo.GroupId == data.ItemInfo.GroupId))
					blacklist.Add(duplicateItem.ItemInfo.Path);
				GroupBlacklist.Add(blacklist);
				try {
					using var stream = new FileStream(FileUtils.SafePathCombine(CoreUtils.CurrentFolder,
					"BlacklistedGroups.json"), FileMode.Create);
					await JsonSerializer.SerializeAsync(stream, GroupBlacklist);
				}
				catch (Exception e) {
					GroupBlacklist.Remove(blacklist);
					await MessageBoxService.Show(e.Message);
				}
				for (var i = Duplicates.Count - 1; i >= 0; i--) {
					if (!blacklist.Contains(Duplicates[i].ItemInfo.Path)) continue;
					Duplicates.RemoveAt(i);
				}
			});
		});
		public ReactiveCommand<Unit, Unit> ShowGroupInThumbnailComparerCommand => ReactiveCommand.Create(() => {

			if (GetDataGrid.SelectedItem is not DuplicateItemVM data) return;
			List<LargeThumbnailDuplicateItem> items = new();

			if (GetDataGrid.SelectedItems.Count == 1) {
				foreach (DuplicateItemVM duplicateItem in Duplicates.Where(a => a.ItemInfo.GroupId == data.ItemInfo.GroupId))
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
			}
			else {
				foreach (DuplicateItemVM duplicateItem in GetDataGrid.SelectedItems)
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
			}

			ThumbnailComparer thumbnailComparer = new(items);
			thumbnailComparer.Show();
		});



		async void DeleteInternal(bool fromDisk,
								  bool blackList = false,
								  bool createSymbolLinksInstead = false,
								  bool permanently = false) {
			if (Duplicates.Count == 0) return;

			MessageBoxButtons? dlgResult = await MessageBoxService.Show(
				fromDisk
					? $"Are you sure you want to{(CoreUtils.IsWindows && !permanently ? " move" : " permanently delete")} the selected files{(CoreUtils.IsWindows && !permanently ? " to recycle bin (only if supported, i.e. network files will be deleted instead)" : " from disk")}?"
					: $"Are you sure to delete selected from list (keep files){(blackList ? " and blacklist them" : string.Empty)}?",
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;

			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				DuplicateItemVM dub = Duplicates[i];
				if (dub.Checked == false || !dub.IsVisibleInFilter) continue;
				if (fromDisk)
					try {
						FileEntry dubFileEntry = new FileEntry(dub.ItemInfo.Path);
						if (createSymbolLinksInstead) {
							DuplicateItemVM? fileToKeep = Duplicates.FirstOrDefault(s =>
							s.ItemInfo.GroupId == dub.ItemInfo.GroupId &&
							s.Checked == false);
							if (fileToKeep == default(DuplicateItemVM)) {
								throw new Exception($"Cannot create a symbol link for '{dub.ItemInfo.Path}' because all items in this group are selected/checked");
							}
							File.CreateSymbolicLink(dub.ItemInfo.Path, fileToKeep.ItemInfo.Path);
							TotalSizeRemovedInternal += dub.ItemInfo.SizeLong;
						}
						else if (CoreUtils.IsWindows && !permanently) {
							//Try moving files to recycle bin
							var fs = new FileUtils.SHFILEOPSTRUCT {
								wFunc = FileUtils.FileOperationType.FO_DELETE,
								pFrom = dub.ItemInfo.Path + '\0' + '\0',
								fFlags = FileUtils.FileOperationFlags.FOF_ALLOWUNDO |
								FileUtils.FileOperationFlags.FOF_NOCONFIRMATION |
								FileUtils.FileOperationFlags.FOF_NOERRORUI |
								FileUtils.FileOperationFlags.FOF_SILENT
							};
							int result = FileUtils.SHFileOperation(ref fs);
							if (result != 0)
								throw new Exception($"SHFileOperation returned: {result:X}");

							TotalSizeRemovedInternal += dub.ItemInfo.SizeLong;
						}
						else {
							File.Delete(dub.ItemInfo.Path);
							TotalSizeRemovedInternal += dub.ItemInfo.SizeLong;
						}
						ScanEngine.RemoveFromDatabase(dubFileEntry);
					}
					catch (Exception ex) {
						Logger.Instance.Info(
							$"Failed to delete file '{dub.ItemInfo.Path}', reason: {ex.Message}, Stacktrace {ex.StackTrace}");
						continue;
					}
				if (blackList)
					ScanEngine.BlackListFileEntry(dub.ItemInfo.Path);
				Duplicates.RemoveAt(i);
			}

			//Remove groups with just one item left
			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				var first = Duplicates[i];
				if (Duplicates.Any(s => s.ItemInfo.GroupId == first.ItemInfo.GroupId && s.ItemInfo.Path != first.ItemInfo.Path)) continue;
				Duplicates.RemoveAt(i);
			}

			ScanEngine.SaveDatabase();

			if (SettingsFile.Instance.BackupAfterListChanged)
				await ExportScanResultsIncludingThumbnails(BackupScanResultsFile);
		}

		public static ReactiveCommand<Unit, Unit> ExpandAllGroupsCommand => ReactiveCommand.Create(() => {
			Utils.TreeHelper.ToggleExpander(GetDataGrid, true);
		});
		public static ReactiveCommand<Unit, Unit> CollapseAllGroupsCommand => ReactiveCommand.Create(() => {
			Utils.TreeHelper.ToggleExpander(GetDataGrid, false);
		});
		public static ReactiveCommand<Unit, Unit> CopyPathsToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetDataGrid.SelectedItems) {
				if (item is not DuplicateItemVM currentItem) return;
				sb.AppendLine(currentItem.ItemInfo.Path);
			}
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
			await (ApplicationHelpers.MainWindow.Clipboard.SetTextAsync(sb.ToString().TrimEnd(new char[2] { '\r', '\n' })));
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
		});
		public static ReactiveCommand<Unit, Unit> CopyFilenamesToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetDataGrid.SelectedItems) {
				if (item is not DuplicateItemVM currentItem) return;
				sb.AppendLine(Path.GetFileName(currentItem.ItemInfo.Path));
			}
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
			await (ApplicationHelpers.MainWindow.Clipboard.SetTextAsync(sb.ToString().TrimEnd(new char[2] { '\r', '\n' })));
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
		});

	}
}
