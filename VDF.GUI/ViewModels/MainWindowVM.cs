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
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DynamicExpresso;
using DynamicExpresso.Exceptions;
using JetBrains.Annotations;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public class MainWindowVM : ReactiveObject {
		public ScanEngine Scanner { get; } = new();
		public ObservableCollection<string> LogItems { get; } = new();
		List<HashSet<string>> GroupBlacklist = new();
		public string BackupScanResultsFile =>
			Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder) ?
			Path.Combine(SettingsFile.Instance.CustomDatabaseFolder, "backup.scanresults") :
			Path.Combine(CoreUtils.CurrentFolder, "backup.scanresults");

		[CanBeNull] DataGridCollectionView? view;
		public ObservableCollection<DuplicateItemVM> Duplicates { get; } = new();
		public KeyValuePair<string, DataGridSortDescription>[] SortOrders { get; private set; }
		public sealed class CheckedGroupsComparer : System.Collections.IComparer {
			readonly MainWindowVM mainVM;
			public CheckedGroupsComparer(MainWindowVM vm) => mainVM = vm;
			public int Compare(object? x, object? y) {
				if (x == null || y == null)
					return -1;
				var dupX = (DuplicateItemVM)x;
				var dupY = (DuplicateItemVM)y;
				bool xHasChecked = mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupX.ItemInfo.GroupId).Where(a => a.Checked).Any();
				bool yHasChecked = dupY.ItemInfo.GroupId == dupX.ItemInfo.GroupId ?
					xHasChecked :
					mainVM.Duplicates.Where(a => a.ItemInfo.GroupId == dupY.ItemInfo.GroupId).Where(a => a.Checked).Any();
				return xHasChecked.CompareTo(yHasChecked);
			}
		}
		public KeyValuePair<string, FileTypeFilter>[] TypeFilters { get; } = {
			new KeyValuePair<string, FileTypeFilter>("All",  FileTypeFilter.All),
			new KeyValuePair<string, FileTypeFilter>("Videos",  FileTypeFilter.Videos),
			new KeyValuePair<string, FileTypeFilter>("Images",  FileTypeFilter.Images),
		};

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
#pragma warning disable CA1822 // Mark members as static => It's used by Avalonia binding
		public IEnumerable<Core.FFTools.FFHardwareAccelerationMode> HardwareAccelerationModes =>
#pragma warning restore CA1822 // Mark members as static
			Enum.GetValues<Core.FFTools.FFHardwareAccelerationMode>();

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
		public bool MultiOpenSupported => !string.IsNullOrEmpty(SettingsFile.Instance.CustomCommands.OpenMultiple);
		public bool MultiOpenInFolderSupported => !string.IsNullOrEmpty(SettingsFile.Instance.CustomCommands.OpenMultipleInFolder);
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
				this.RaisePropertyChanged(nameof(MultiOpenSupported));
				this.RaisePropertyChanged(nameof(MultiOpenInFolderSupported));
			}
		}

		public string TotalSizeRemoved => TotalSizeRemovedInternal.BytesToString();
#if DEBUG
		public static bool IsDebug => true;
#else
		public static bool IsDebug => false;
#endif

		KeyValuePair<string, FileTypeFilter> _FileType;

		public KeyValuePair<string, FileTypeFilter> FileType {
			get => _FileType;
			set {
				if (value.Key == _FileType.Key) return;
				_FileType = value;
				this.RaisePropertyChanged(nameof(FileType));
				view?.Refresh();
			}
		}
		KeyValuePair<string, DataGridSortDescription> _SortOrder;

		public KeyValuePair<string, DataGridSortDescription> SortOrder {
			get => _SortOrder;
			set {
				if (value.Key == _SortOrder.Key) return;
				_SortOrder = value;
				this.RaisePropertyChanged(nameof(SortOrder));
				view?.SortDescriptions.Clear();
				if (_SortOrder.Value != null)
					view?.SortDescriptions.Add(_SortOrder.Value);
				view?.Refresh();
			}
		}
		string _FilterByPath = string.Empty;

		public string FilterByPath {
			get => _FilterByPath;
			set {
				if (value == FilterByPath) return;
				_FilterByPath = value;
				this.RaisePropertyChanged(nameof(FilterByPath));
				view?.Refresh();
			}
		}
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
			var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
			Scanner.NoThumbnailImage = SixLabors.ImageSharp.Image.Load(assets!.Open(new Uri("avares://VDF.GUI/Assets/icon.png")));

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
				//Otherwise an exception is thrown when calling ApplicationHelpers.CurrentApplicationLifetime.Shutdown();
				await Task.Delay(100);
				return true;
			}
			MessageBoxButtons? result = await MessageBoxService.Show("Do you want to save the results and continue next time you start VDF?",
				MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
			if (result == null || result == MessageBoxButtons.Cancel) {
				//Can be NULL if user closed the window by clicking on 'X'

				//Otherwise an exception is thrown when calling ApplicationHelpers.CurrentApplicationLifetime.Shutdown();
				await Task.Delay(100);
				return false;
			}
			if (result != MessageBoxButtons.Yes) {
				//Otherwise an exception is thrown when calling ApplicationHelpers.CurrentApplicationLifetime.Shutdown();
				await Task.Delay(100);
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

		void Instance_LogItemAdded(string message) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				if (string.IsNullOrEmpty(message)) return;
				LogItems.Add(message);
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
			view.Filter += TextFilter;
			GetDataGrid.Items = view;

			TotalDuplicates = Duplicates.Count;
			TotalDuplicatesSize = Duplicates.Sum(x => x.ItemInfo.SizeLong).BytesToString();
			TotalSizeRemovedInternal = 0;
			TotalDuplicateGroups = Duplicates.GroupBy(x => x.ItemInfo.GroupId).Count();
		}
		bool TextFilter(object obj) {
			if (obj is not DuplicateItemVM data) return false;
			var success = true;
			if (!string.IsNullOrEmpty(FilterByPath)) {
				success = data.ItemInfo.Path.Contains(FilterByPath, StringComparison.OrdinalIgnoreCase);
				//see if a group member matches, then this should be considered as match too
				if (!success)
					success = Duplicates.Any(s =>
						s.ItemInfo.GroupId == data.ItemInfo.GroupId &&
						s.ItemInfo.Path.Contains(FilterByPath, StringComparison.OrdinalIgnoreCase));
			}
			if (success && FileType.Value != FileTypeFilter.All)
				success = FileType.Value == FileTypeFilter.Images ? data.ItemInfo.IsImage : !data.ItemInfo.IsImage;
			return success;
		}

		static DataGrid GetDataGrid => ApplicationHelpers.MainWindow.FindControl<DataGrid>("dataGridGrouping");

		public ReactiveCommand<Unit, Unit> AddIncludesToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = "Select folder"
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			if (!SettingsFile.Instance.Includes.Contains(result))
				SettingsFile.Instance.Includes.Add(result);
		});
		public static ReactiveCommand<Unit, Unit> LatestReleaseCommand => ReactiveCommand.Create(() => {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "https://github.com/0x90d/videoduplicatefinder/releases",
					UseShellExecute = true
				});
			}
			catch { }
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
			List<FileDialogFilter> filterList = new(1);
			filterList.Add(new FileDialogFilter {
				Name = "Json Files",
				Extensions = new List<string>() { "json" }
			});
			string[]? result = await new OpenFileDialog {
				Filters = filterList,
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (result == null || result.Length != 1 || string.IsNullOrEmpty(result[0])) return;

			bool success = ScanEngine.ImportDataBaseFromJson(result[0], new JsonSerializerOptions {
				IncludeFields = true,
			});
			if (!success)
				await MessageBoxService.Show("Exporting database has failed, please see log");
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

			List<FileDialogFilter> filterList = new(1);
			filterList.Add(new FileDialogFilter {
				Name = "Json Files",
				Extensions = new List<string>() { "json" }
			});

			string? result = await new SaveFileDialog {
				DefaultExtension = ".json",
				Filters = filterList
			}.ShowAsync(ApplicationHelpers.MainWindow);
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
			string? path = await new SaveFileDialog {
				DefaultExtension = ".json",
				Filters = new List<FileDialogFilter> { new FileDialogFilter {
					Name = "Json Files",
					Extensions = new List<string>() { "json" }
					}
				}
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(path)) return;


			try {
				List<DuplicateItem> list = Duplicates.Select(x => x.ItemInfo).OrderBy(x => x.GroupId).ToList();
				using var stream = File.OpenWrite(path);
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
			path ??= await new SaveFileDialog {
				DefaultExtension = ".scanresults",
				Filters = new List<FileDialogFilter> { new FileDialogFilter {
					Name = "Scan Results",
					Extensions = new List<string>() { "scanresults" }
					}
				}
			}.ShowAsync(ApplicationHelpers.MainWindow);
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
		async void ImportScanResultsIncludingThumbnails(string? path = null) {
			if (Duplicates.Count > 0) {
				MessageBoxButtons? result = await MessageBoxService.Show($"Importing scan results will clear the current list, continue?", MessageBoxButtons.Yes | MessageBoxButtons.No);
				if (result != MessageBoxButtons.Yes) return;
			}

			if (path == null) {
				var paths = await new OpenFileDialog {
					Filters = new List<FileDialogFilter> { new FileDialogFilter {
					Name = "Scan Results",
					Extensions = new List<string>() { "scanresults" }
						}
					}
				}.ShowAsync(ApplicationHelpers.MainWindow);
				if (paths == null || paths.Length == 0) return;
				path = paths[0];
			}
			if (string.IsNullOrEmpty(path)) return;

			try {
				using var stream = File.OpenRead(path);
				var options = new JsonSerializerOptions {
					IncludeFields = true,
				};
				options.Converters.Add(new BitmapJsonConverter());
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

		public ReactiveCommand<ListBox, Action> RemoveIncludesFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems.Count > 0)
				SettingsFile.Instance.Includes.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});
		public ReactiveCommand<Unit, Unit> AddBlacklistToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = "Select folder"
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			if (!SettingsFile.Instance.Blacklists.Contains(result))
				SettingsFile.Instance.Blacklists.Add(result);
		});
		public ReactiveCommand<ListBox, Action> RemoveBlacklistFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems.Count > 0)
				SettingsFile.Instance.Blacklists.Remove((string)lbox.SelectedItems[0]!);
			return null!;
		});
		public ReactiveCommand<Unit, Unit> ClearLogCommand => ReactiveCommand.Create(() => {
			LogItems.Clear();
		});
		public ReactiveCommand<Unit, Unit> SaveLogCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new SaveFileDialog {
				DefaultExtension = ".txt",
			}.ShowAsync(ApplicationHelpers.MainWindow);
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
		public ReactiveCommand<Unit, Unit> SaveSettingsProfileCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new SaveFileDialog {
				Directory = CoreUtils.CurrentFolder,
				DefaultExtension = ".json",
				Filters = new List<FileDialogFilter> { new FileDialogFilter {
					Extensions= new List<string> { "json" },
					Name = "Setting File"
					}
				}
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			SettingsFile.SaveSettings(result);
		});
		public ReactiveCommand<Unit, Unit> LoadSettingsProfileCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFileDialog {
				Directory = CoreUtils.CurrentFolder,
				Filters = new List<FileDialogFilter> { new FileDialogFilter {
					Extensions= new List<string> { "xml", "json" },
					Name = "Setting File"
					}
				}
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (result == null || result.Length == 0 || string.IsNullOrEmpty(result[0])) return;
			SettingsFile.LoadSettings(result[0]);
			await MessageBoxService.Show("Please restart VDF to apply new settings.");
		});

		public ReactiveCommand<string, Unit> StartScanCommand => ReactiveCommand.CreateFromTask(async (string command) => {
			if (!string.IsNullOrEmpty(SettingsFile.Instance.CustomDatabaseFolder) && !Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder)) {
				await MessageBoxService.Show("The custom database folder does not exist!");
				return;
			}
			if (!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists) {
				await MessageBoxService.Show("Cannot find FFmpeg. Please follow instructions on Github and restart VDF");
				return;
			}
			if (!ScanEngine.FFprobeExists) {
				await MessageBoxService.Show("Cannot find FFprobe. Please follow instructions on Github and restart VDF");
				return;
			}
			if (SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) {
				await MessageBoxService.Show("Cannot find shared FFmpeg libraries. Either uncheck 'Use native ffmpeg binding' in settings or please follow instructions on Github and restart VDF");
				return;
			}
			if (SettingsFile.Instance.UseNativeFfmpegBinding && SettingsFile.Instance.HardwareAccelerationMode == Core.FFTools.FFHardwareAccelerationMode.auto) {
				await MessageBoxService.Show("You cannot use hardware acceleration mode 'auto' with native ffmpeg bindings. Please explicit set a mode or set it to 'none'.");
				return;
			}
			if (SettingsFile.Instance.Includes.Count == 0) {
				await MessageBoxService.Show("There are no folders to scan. Please go to the settings and add at least one folder.");
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
			Scanner.Settings.FilePathContainsText = SettingsFile.Instance.FilePathContainsText;
			Scanner.Settings.FilterByFilePathNotContains = SettingsFile.Instance.FilterByFilePathNotContains;
			Scanner.Settings.IncludeNonExistingFiles = SettingsFile.Instance.IncludeNonExistingFiles;
			Scanner.Settings.FilePathNotContainsText = SettingsFile.Instance.FilePathNotContainsText;
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
			switch (command) {
			case "FullScan":
				IsBusy = true;
				IsBusyText = "Enumerating files...";
				Scanner.StartSearch();
				break;
			case "CompareOnly":
				Scanner.StartCompare();
				break;
			default:
				await MessageBoxService.Show("Requested command is NOT implemented yet!");
				break;
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
		public ReactiveCommand<Unit, Unit> CheckCustomCommand => ReactiveCommand.CreateFromTask(async () => {
			ExpressionBuilder dlg = new();
			((ExpressionBuilderVM)dlg.DataContext!).ExpressionText = SettingsFile.Instance.LastCustomSelectExpression;
			bool res = await dlg.ShowDialog<bool>(ApplicationHelpers.MainWindow);
			if (!res) return;

			SettingsFile.Instance.LastCustomSelectExpression =
							((ExpressionBuilderVM)dlg.DataContext).ExpressionText;

			HashSet<Guid> blackListGroupID = new();
			bool skipIfAllMatches = false;
			bool userAsked = false;

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already

				IEnumerable<DuplicateItemVM> l = Duplicates.Where(a => a.ItemInfo.GroupId == first.ItemInfo.GroupId);
				IEnumerable<DuplicateItemVM> matches;
				try {
					var interpreter = new Interpreter().
						ParseAsDelegate<Func<DuplicateItemVM, bool>>(SettingsFile.Instance.LastCustomSelectExpression);
					matches = l.Where(interpreter);
				}
				catch (ParseException e) {
					await MessageBoxService.Show($"Failed to parse '{SettingsFile.Instance.LastCustomSelectExpression}': {e}");
					return;
				}

				if (!matches.Any()) continue;
				if (matches.Count() == l.Count()) {
					if (!userAsked) {
						MessageBoxButtons? result = await MessageBoxService.Show($"There are groups where all items matches your expression, for example '{first.ItemInfo.Path}'.{Environment.NewLine}{Environment.NewLine}Do you want to have all items checked (Yes)? Or do you want to have NO items in these groups checked (No)?", MessageBoxButtons.Yes | MessageBoxButtons.No);
						if (result == MessageBoxButtons.No)
							skipIfAllMatches = true;
						userAsked = true;
					}
					if (skipIfAllMatches)
						continue;
				}
				foreach (var dup in matches)
					dup.Checked = true;
				blackListGroupID.Add(first.ItemInfo.GroupId);
			}
		});
		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalCommand => ReactiveCommand.Create(() => {
			var blackListGroupID = new HashSet<Guid>();
			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already

				var l = Duplicates.Where(d => d.EqualsFull(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));

				var dupMods = l as DuplicateItemVM[] ?? l.ToArray();
				if (!dupMods.Any()) continue;
				foreach (var dup in dupMods)
					dup.Checked = true;
				first.Checked = false;
				blackListGroupID.Add(first.ItemInfo.GroupId);

			}
		});
		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalButSizeCommand => ReactiveCommand.Create(() => {
			var blackListGroupID = new HashSet<Guid>();

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				var l = Duplicates.Where(d => d.EqualsButSize(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
				var dupMods = l as List<DuplicateItemVM> ?? l.ToList();
				if (!dupMods.Any()) continue;
				dupMods.Add(first);
				dupMods = dupMods.OrderBy(s => s.ItemInfo.SizeLong).ToList();
				dupMods[0].Checked = false;
				for (int i = 1; i < dupMods.Count; i++) {
					dupMods[i].Checked = true;
				}

				blackListGroupID.Add(first.ItemInfo.GroupId);
			}

		});
		public ReactiveCommand<Unit, Unit> CheckLowestQualityCommand => ReactiveCommand.Create(() => {
			HashSet<Guid> blackListGroupID = new();

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				IEnumerable<DuplicateItemVM> l = Duplicates.Where(d => d.EqualsButQuality(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
				var dupMods = l as List<DuplicateItemVM> ?? l.ToList();
				if (!dupMods.Any()) continue;
				dupMods.Insert(0, first);

				DuplicateItemVM keep = dupMods[0];

				//Duration first
				if (!keep.ItemInfo.IsImage)
					keep = dupMods.OrderByDescending(d => d.ItemInfo.Duration).First();

				//resolution next, but only when keep is unchanged, or when there was >=1 item with same quality
				if (keep.ItemInfo.Path.Equals(dupMods[0].ItemInfo.Path) || dupMods.Count(d => d.ItemInfo.Duration == keep.ItemInfo.Duration) > 1)
					keep = dupMods.OrderByDescending(d => d.ItemInfo.FrameSizeInt).First();

				//fps next, but only when keep is unchanged, or when there was >=1 item with same quality
				if (!keep.ItemInfo.IsImage && (keep.ItemInfo.Path.Equals(dupMods[0].ItemInfo.Path) || dupMods.Count(d => d.ItemInfo.FrameSizeInt == keep.ItemInfo.FrameSizeInt) > 1))
					keep = dupMods.OrderByDescending(d => d.ItemInfo.Fps).First();

				//Bitrate next, but only when keep is unchanged, or when there was >=1 item with same quality
				if (!keep.ItemInfo.IsImage && (keep.ItemInfo.Path.Equals(dupMods[0].ItemInfo.Path) || dupMods.Count(d => d.ItemInfo.Fps == keep.ItemInfo.Fps) > 1))
					keep = dupMods.OrderByDescending(d => d.ItemInfo.BitRateKbs).First();

				//Audio Bitrate next, but only when keep is unchanged, or when there was >=1 item with same quality
				if (!keep.ItemInfo.IsImage && (keep.ItemInfo.Path.Equals(dupMods[0].ItemInfo.Path) || dupMods.Count(d => d.ItemInfo.BitRateKbs == keep.ItemInfo.BitRateKbs) > 1))
					keep = dupMods.OrderByDescending(d => d.ItemInfo.AudioSampleRate).First();

				keep.Checked = false;
				for (int i = 0; i < dupMods.Count; i++) {
					if (!keep.ItemInfo.Path.Equals(dupMods[i].ItemInfo.Path))
						dupMods[i].Checked = true;
				}

				blackListGroupID.Add(first.ItemInfo.GroupId);
			}

		});
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
		public ReactiveCommand<Unit, Unit> ClearSelectionCommand => ReactiveCommand.Create(() => {
			for (var i = 0; i < Duplicates.Count; i++)
				Duplicates[i].Checked = false;
		});
		public ReactiveCommand<Unit, Unit> DeleteHighlitedCommand => ReactiveCommand.Create(() => {
			if (GetDataGrid.SelectedItem == null) return;
			Duplicates.Remove((DuplicateItemVM)GetDataGrid.SelectedItem);
		});
		public ReactiveCommand<Unit, Unit> DeleteSelectionWithPromptCommand => ReactiveCommand.CreateFromTask(async () => {
			MessageBoxButtons? dlgResult = await MessageBoxService.Show("Delete files also from DISK?",
				MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
			if (dlgResult == MessageBoxButtons.Yes)
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
				Dispatcher.UIThread.InvokeAsync(() => {
					DeleteInternal(true);
				});
			else if (dlgResult == MessageBoxButtons.No)
				Dispatcher.UIThread.InvokeAsync(() => {
					DeleteInternal(false);
				});
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
		});
		public ReactiveCommand<Unit, Unit> DeleteSelectionCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(true);
			});
		});
		public ReactiveCommand<Unit, Unit> RemoveSelectionFromListCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(false);
			});
		});
		public ReactiveCommand<Unit, Unit> RemoveSelectionFromListAndBlacklistCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(false, blackList: true);
			});
		});
		public ReactiveCommand<Unit, Unit> CreateSymbolLinksForSelectedItemsCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(false, blackList: false, createSymbolLinksInstead: true);
			});
		});
		public ReactiveCommand<Unit, Unit> CreateSymbolLinksForSelectedItemsAndBlacklistCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(false, blackList: true, createSymbolLinksInstead: true);
			});
		});

		async void DeleteInternal(bool fromDisk, bool blackList = false, bool createSymbolLinksInstead = false) {
			if (Duplicates.Count == 0) return;

			MessageBoxButtons? dlgResult = await MessageBoxService.Show(
				fromDisk
					? $"Are you sure you want to{(CoreUtils.IsWindows ? " move" : " permanently delete")} the selected files{(CoreUtils.IsWindows ? " to recycle bin (only if supported, i.e. network files will be deleted instead)" : " from disk")}?"
					: $"Are you sure to delete selected from list (keep files){(blackList ? " and blacklist them" : string.Empty)}?",
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;

			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				DuplicateItemVM dub = Duplicates[i];
				if (dub.Checked == false) continue;
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
						else if (CoreUtils.IsWindows) {
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

			//Hide groups with just one item left
			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				var first = Duplicates[i];
				if (Duplicates.Any(s => s.ItemInfo.GroupId == first.ItemInfo.GroupId && s.ItemInfo.Path != first.ItemInfo.Path)) continue;
				Duplicates.RemoveAt(i);
			}

			ScanEngine.SaveDatabase();

			if (SettingsFile.Instance.BackupAfterListChanged)
				await ExportScanResultsIncludingThumbnails(BackupScanResultsFile);
		}
		public ReactiveCommand<Unit, Unit> CopySelectionCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = "Select folder"
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			Utils.FileUtils.CopyFile(Duplicates.Where(s => s.Checked), result, true, false, out var errorCounter);
			if (errorCounter > 0)
				await MessageBoxService.Show("Failed to copy/move some files. Please check log!");
		});
		public ReactiveCommand<Unit, Unit> MoveSelectionCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = "Select folder"
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			var selectedItems = Duplicates.Where(s => s.Checked).ToList();
			List<Tuple<DuplicateItemVM, FileEntry>> itemsToUpdate = new();
			foreach (var item in selectedItems) {
				ScanEngine.GetFromDatabase(item.ItemInfo.Path, out var dbEntry);
				itemsToUpdate.Add(Tuple.Create(item, dbEntry));
			}
			Utils.FileUtils.CopyFile(selectedItems, result, true, true, out var errorCounter);
			foreach (var pair in itemsToUpdate) {
				ScanEngine.UpdateFilePathInDatabase(pair.Item1.ItemInfo.Path, pair.Item2);
			}
			ScanEngine.SaveDatabase();
			if (errorCounter > 0)
				await MessageBoxService.Show("Failed to copy/move some files. Please check log!");
		});
		public static ReactiveCommand<Unit, Unit> ExpandAllGroupsCommand => ReactiveCommand.Create(() => {
			Utils.TreeHelper.ToggleExpander(GetDataGrid, true);
		});
		public static ReactiveCommand<Unit, Unit> CollapseAllGroupsCommand => ReactiveCommand.Create(() => {
			Utils.TreeHelper.ToggleExpander(GetDataGrid, false);
		});

	}
}
