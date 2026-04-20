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
using System.IO.Compression;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFmpeg.AutoGen;
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
			Path.Combine(CoreUtils.ResolveDatabaseFolder(SettingsFile.Instance.CustomDatabaseFolder), "backup.scanresults");

		public AvaloniaList<DuplicateItemVM> Duplicates { get; } = new();

		private TempDir? TempDirectory;

		static readonly string[] BusyMessages =
		{
			App.Lang["BusyMessages1"],
			App.Lang["BusyMessages2"],
			App.Lang["BusyMessages3"],
			App.Lang["BusyMessages4"],
			App.Lang["BusyMessages5"],
			App.Lang["BusyMessages6"],
			App.Lang["BusyMessages7"],
			App.Lang["BusyMessages8"],
			App.Lang["BusyMessages9"],
			App.Lang["BusyMessages10"],
			App.Lang["BusyMessages11"],
			App.Lang["BusyMessages12"],
			App.Lang["BusyMessages13"],
			App.Lang["BusyMessages14"],
			App.Lang["BusyMessages15"],
			App.Lang["BusyMessages16"],
			App.Lang["BusyMessages17"],
			App.Lang["BusyMessages18"],
			App.Lang["BusyMessages19"],
			App.Lang["BusyMessages20"]
		};
		private void ChangeIsBusyMessage() => IsBusyOverlayText = BusyMessages[Random.Shared.Next(BusyMessages.Length)];

		readonly DispatcherTimer scheduledScanTimer = new();
		DateTime lastScheduledScanDate = DateTime.MinValue;
		bool scheduledScanInProgress;
		bool scheduleTimeInvalidNotified;

		bool _IsScanning;
		public bool IsScanning {
			get => _IsScanning;
			set => this.RaiseAndSetIfChanged(ref _IsScanning, value);
		}
		string _IsBusyOverlayText = string.Empty;
		public string IsBusyOverlayText {
			get => _IsBusyOverlayText;
			set => this.RaiseAndSetIfChanged(ref _IsBusyOverlayText, value);
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
		bool _ShowThumbnailRetrievalProgressBar;
		public bool ShowThumbnailRetrievalProgressBar {
			get => _ShowThumbnailRetrievalProgressBar;
			set => this.RaiseAndSetIfChanged(ref _ShowThumbnailRetrievalProgressBar, value);
		}
		public bool ShowThumbnailRetrievalProgress => !string.IsNullOrEmpty(ThumbnailRetrievalProgressText);
		string _ThumbnailRetrievalProgressText = string.Empty;
		public string ThumbnailRetrievalProgressText {
			get => _ThumbnailRetrievalProgressText;
			set {
				this.RaiseAndSetIfChanged(ref _ThumbnailRetrievalProgressText, value);
				this.RaisePropertyChanged(nameof(ShowThumbnailRetrievalProgress));
			}
		}
		string _ScanProgressText = string.Empty;
		public string ScanProgressText {
			get => _ScanProgressText;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressText, value);
		}
		string _RemainingTime = string.Empty;
		public string RemainingTime {
			get => _RemainingTime;
			set => this.RaiseAndSetIfChanged(ref _RemainingTime, value);
		}

		string _TimeElapsed = string.Empty;
		public string TimeElapsed {
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
		int _DuplicatesCheckedCounter;
		public int DuplicatesCheckedCounter {
			get => _DuplicatesCheckedCounter;
			set => this.RaiseAndSetIfChanged(ref _DuplicatesCheckedCounter, value);
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
			Scanner.ThumbnailProgress += Scanner_ThumbnailProgress;
			Scanner.ThumbnailsRetrieved += Scanner_ThumbnailsRetrieved;
			Scanner.DatabaseCleaned += Scanner_DatabaseCleaned;
			Scanner.FilesEnumerated += Scanner_FilesEnumerated;
			Scanner.NoThumbnailImage = SixLabors.ImageSharp.Image.Load(AssetLoader.Open(new Uri("avares://VDF.GUI/Assets/icon.png")));

			try {
				TempDirectory = TempExtractionManager.Register(new("VDF-"));
				// Invalidate thumbnail cache if the configured width has changed
				Utils.ThumbCacheHelpers.InvalidateIfWidthChanged(TempDirectory.Path, SettingsFile.Instance.ThumbnailMaxWidth);
				Utils.ThumbCacheHelpers.Provider = Utils.ThumbPack.Open(TempDirectory.Path);
			}
			catch { Utils.ThumbCacheHelpers.Provider = null; }

			try {
				File.Delete(Path.Combine(CoreUtils.CurrentFolder, "log.txt"));
			}
			catch { }
			Logger.Instance.LogItemAdded += Instance_LogItemAdded;
			//Ensure items added before GUI was ready will be shown
			Instance_LogItemAdded(string.Empty);

			Duplicates.CollectionChanged += Duplicates_CollectionChanged;

			scheduledScanTimer.Interval = TimeSpan.FromMinutes(1);
			scheduledScanTimer.Tick += (_, __) => CheckScheduledScan();
			scheduledScanTimer.Start();
			CheckScheduledScan();

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
				new KeyValuePair<string, DataGridSortDescription>("Group Total Size Ascending",
				DataGridSortDescription.FromComparer(new GroupTotalSizeComparer(this), ListSortDirection.Ascending)),
				new KeyValuePair<string, DataGridSortDescription>("Group Total Size Descending",
				DataGridSortDescription.FromComparer(new GroupTotalSizeComparer(this), ListSortDirection.Descending)),
			};
			_SortOrder = SortOrders[0];

			this.WhenAnyValue(vm => vm.FilterByPath)
					.Throttle(TimeSpan.FromMilliseconds(500), RxApp.MainThreadScheduler)
						.Subscribe(_ => { RebuildSearchPathIndex(); view?.Refresh(); });
		}

		void Duplicates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
			if (e.OldItems != null) {
				foreach (INotifyPropertyChanged item in e.OldItems) {
					item.PropertyChanged -= DuplicateItemVM_PropertyChanged;
					if (((DuplicateItemVM)item).Checked)
						DuplicatesCheckedCounter--;
				}
			}
			if (e.NewItems != null) {
				foreach (INotifyPropertyChanged item in e.NewItems)
					item.PropertyChanged += DuplicateItemVM_PropertyChanged;
			}
			if (e.Action == NotifyCollectionChangedAction.Reset)
				DuplicatesCheckedCounter = 0;
		}

		void DuplicateItemVM_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			if (e.PropertyName != nameof(DuplicateItemVM.Checked) || sender == null) return;
			if (((DuplicateItemVM)sender).Checked)
				DuplicatesCheckedCounter++;
			else
				DuplicatesCheckedCounter--;
		}

		public async void Thumbnails_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
			bool isReadyToCompare = IsGathered;
			isReadyToCompare &= Scanner.Settings.ThumbnailCount == e.NewValue;
			if (!isReadyToCompare && ApplicationHelpers.MainWindowDataContext.IsReadyToCompare)
				await MessageBoxService.Show($"Number of thumbnails can't be changed between quick rescans. Full scan will be required.");
			ApplicationHelpers.MainWindowDataContext.IsReadyToCompare = isReadyToCompare;
		}

		private void Scanner_ThumbnailProgress(int arg1, int arg2) => Dispatcher.UIThread.Post(() => {
			ThumbnailRetrievalProgressText = $"Retrieving thumbnails for preview: {arg1}/{arg2}";
		});

		void Scanner_ThumbnailsRetrieved(object? sender, EventArgs e) {
			//Reset properties
			ScanProgressText = string.Empty;
			RemainingTime = new TimeSpan().Format();
			ScanProgressValue = 0;
			ScanProgressMaxValue = 100;
			ThumbnailRetrievalProgressText = string.Empty;
			ShowThumbnailRetrievalProgressBar = false;
#pragma warning disable CS4014
			if (SettingsFile.Instance.BackupAfterListChanged)
				ExportScanResults(BackupScanResultsFile);
#pragma warning restore CS4014
		}

		void Scanner_FilesEnumerated(object? sender, EventArgs e) => ChangeIsBusyMessage();

		async void Scanner_DatabaseCleaned(object? sender, EventArgs e) {
			IsBusy = false;
			await MessageBoxService.Show(App.Lang["Message.DatabaseCleaned"]);
		}

		public async Task<bool> SaveScanResults() {
			if (Duplicates.Count == 0 || !SettingsFile.Instance.AskToSaveResultsOnExit) {
				try { Utils.ThumbCacheHelpers.Provider?.FlushIndex(); } catch { }
				return true;
			}
			MessageBoxButtons? result = await MessageBoxService.Show(App.Lang["Message.SaveResultsPrompt"],
				MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
			if (result == null || result == MessageBoxButtons.Cancel) {
				return false;
			}
			if (result != MessageBoxButtons.Yes) {
				return true;
			}
			await ExportScanResults(BackupScanResultsFile);
			return true;
		}

		public void RestoreBackupScanResults() {
			if (File.Exists(BackupScanResultsFile))
				ImportScanResultsIncludingThumbnails(BackupScanResultsFile);
		}

		public async void LoadDatabase() {
			IsBusy = true;
			IsBusyOverlayText = "Loading database...";
			bool success = await ScanEngine.LoadDatabase();
			IsBusy = false;
			if (!success) {
				await MessageBoxService.Show(App.Lang["Message.LoadDatabaseFailed"]);
				Environment.Exit(-1);
			}
		}

		void CheckScheduledScan() {
			if (!SettingsFile.Instance.EnableScheduledScan || IsScanning || scheduledScanInProgress)
				return;
			if (!TryParseScheduledTime(SettingsFile.Instance.ScheduledScanTime, out var scheduledTime)) {
				if (!scheduleTimeInvalidNotified) {
					Logger.Instance.Info(App.Lang["Log.InvalidScheduledScanTime"]);
					scheduleTimeInvalidNotified = true;
				}
				return;
			}
			scheduleTimeInvalidNotified = false;
			var now = DateTime.Now;
			if (lastScheduledScanDate.Date == now.Date)
				return;
			if (now.TimeOfDay < scheduledTime)
				return;
			TryStartScheduledScan();
		}

		static bool TryParseScheduledTime(string value, out TimeSpan time) {
			time = default;
			if (string.IsNullOrWhiteSpace(value)) return false;
			if (TimeSpan.TryParse(value, out time))
				return true;
			return TimeSpan.TryParseExact(value, "hh\\:mm", null, out time);
		}

		void TryStartScheduledScan() {
			if (IsScanning) return;
			if (!string.IsNullOrEmpty(SettingsFile.Instance.CustomDatabaseFolder) && !Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder)) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedMissingDatabaseFolder"]);
				return;
			}
			if (Duplicates.Count > 0) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedWithResults"]);
				return;
			}
			if ((SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) ||
				(!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists)) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedMissingFfmpeg"]);
				return;
			}
			if (!ScanEngine.FFprobeExists) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedMissingFfprobe"]);
				return;
			}
			if (SettingsFile.Instance.Includes.Count == 0) {
				Logger.Instance.Info(App.Lang["Log.ScheduledScanSkippedNoFolders"]);
				return;
			}
			scheduledScanInProgress = true;
			lastScheduledScanDate = DateTime.Now.Date;
			Dispatcher.UIThread.Post(() => StartScanCommand.Execute("FullScan").Subscribe());
		}

		void Scanner_Progress(object? sender, ScanProgressChangedEventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				ScanProgressText = e.CurrentFile;
				RemainingTime = e.Remaining.Format();
				ScanProgressValue = e.CurrentPosition;
				TimeElapsed = e.Elapsed.Format();
				ScanProgressMaxValue = e.MaxPosition;
			});

		void Scanner_ScanAborted(object? sender, EventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				IsScanning = false;
				IsBusy = false;
				IsReadyToCompare = false;
				IsGathered = false;
				scheduledScanInProgress = false;
			});

		void Scanner_ScanDone(object? sender, EventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				IsScanning = false;
				IsBusy = false;
				IsReadyToCompare = true;
				IsGathered = true;
				ScanProgressText = string.Empty;
				RemainingTime = TimeSpan.Zero.Format();
				ScanProgressValue = 0;
				var completedScheduledScan = scheduledScanInProgress;
				scheduledScanInProgress = false;

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

				foreach (var item in Scanner.Duplicates)
					Duplicates.Add(new DuplicateItemVM(item));

				if (SettingsFile.Instance.GeneratePreviewThumbnails) {
					ShowThumbnailRetrievalProgressBar = true;
					ThumbnailRetrievalProgressText = "Starting to retrieve thumbnails for preview";
					Scanner.RetrieveThumbnails();
				}

				BuildDuplicatesView();
				RebuildSearchPathIndex();
				RefreshGroupStats();

				if (completedScheduledScan && SettingsFile.Instance.NotifyOnScheduledScanComplete) {
					_ = MessageBoxService.Show(App.Lang["Message.ScheduledScanCompleted"]);
				}

				if (SettingsFile.Instance.NotifyOnScanComplete) {
					VDF.GUI.Utils.DesktopNotificationHelper.Notify(
						App.Lang["Notification.ScanComplete.Title"],
						string.Format(App.Lang["Notification.ScanComplete.Message"], TotalDuplicateGroups));
				}
			});

		void BuildDuplicatesView() {
			view = new DataGridCollectionView(Duplicates);
			view.GroupDescriptions.Add(new DataGridPathGroupDescription($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.GroupId)}"));
			view.Filter += DuplicatesFilter;
			GetDataGrid.ItemsSource = view;
			TotalSizeRemovedInternal = 0;
		}

		static DataGrid GetDataGrid => ApplicationHelpers.MainWindow.FindControl<DataGrid>("dataGridGrouping")!;

		void RefreshGroupStats() {
			TotalDuplicates = Duplicates.Count;
			TotalDuplicatesSize = Duplicates.Sum(x => x.ItemInfo.SizeLong).BytesToString();
			TotalDuplicateGroups = Duplicates.GroupBy(x => x.ItemInfo.GroupId).Count();
		}

		private DuplicateItemVM? GetSelectedDuplicateItem() {
			return GetDataGrid.SelectedItem as DuplicateItemVM;
		}

		private List<DuplicateItemVM> GetSelectedDuplicates() {
			return GetDataGrid.SelectedItems?.Cast<DuplicateItemVM>().ToList() ?? new();
		}

		public static ReactiveCommand<Unit, Unit> LatestReleaseCommand => ReactiveCommand.CreateFromTask(async () => {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "https://github.com/0x90d/videoduplicatefinder/releases",
					UseShellExecute = true
				});
			}
			catch {
				await MessageBoxService.Show(App.Lang["Message.OpenReleaseFailed"]);
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
			IsBusyOverlayText = App.Lang["Busy.CleaningDatabase"];
			Scanner.CleanupDatabase();
		});

		public ReactiveCommand<Unit, Unit> ClearDatabaseCommand => ReactiveCommand.CreateFromTask(async () => {
			MessageBoxButtons? dlgResult = await MessageBoxService.Show(
				App.Lang["Message.ClearDatabaseWarning"],
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;
			ScanEngine.ClearDatabase();
			await MessageBoxService.Show(App.Lang["Message.Done"]);
		});

		public static ReactiveCommand<Unit, Unit> EditDataBaseCommand => ReactiveCommand.CreateFromTask(async () => {
			DatabaseViewer dlg = new();
			bool res = await dlg.ShowDialog<bool>(ApplicationHelpers.MainWindow);
		});
		public static ReactiveCommand<Unit, Unit> ImportDataBaseFromJsonCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = [new FilePickerFileType("Json File") { Patterns = ["*.json"] }]
			});
			if (string.IsNullOrEmpty(result)) return;

			bool success = ScanEngine.ImportDataBaseFromJson(result, new JsonSerializerOptions {
				IncludeFields = true,
			});
			if (!success)
				await MessageBoxService.Show(App.Lang["Message.ImportDatabaseFailed"]);
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
				FileTypeChoices = [new FilePickerFileType("Json Files") { Patterns = ["*.json"] }]
			});
			if (string.IsNullOrEmpty(result)) return;

			if (!ScanEngine.ExportDataBaseToJson(result, options))
				await MessageBoxService.Show(App.Lang["Message.ExportDatabaseFailed"]);
		}

		public ReactiveCommand<Unit, Unit> ExportScanResultsCommand => ReactiveCommand.CreateFromTask(async () => {
			await ExportScanResults(serializerOptions: new JsonSerializerOptions {
				IncludeFields = true,
			});
		});

		public ReactiveCommand<Unit, Unit> ExportScanResultsPrettyCommand => ReactiveCommand.CreateFromTask(async () => {
			await ExportScanResults(serializerOptions: new JsonSerializerOptions {
				IncludeFields = true,
				WriteIndented = true,
			});
		});

		public ReactiveCommand<Unit, Unit> ExportScanResultsToFileCommand => ReactiveCommand.CreateFromTask(async () => {
			await ExportScanResults();
		});

		private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
			IncludeFields = true,
		};

		async Task ExportScanResults(string? path = null, bool includeThumbnails = true, int thumbMaxEdge = 160, JsonSerializerOptions? serializerOptions = null) {
			path ??= await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				DefaultExtension = includeThumbnails ? ".zip" : ".json",
				FileTypeChoices = [new FilePickerFileType("Scan Results") { Patterns = [includeThumbnails ? "*.zip" : ".scanresults"] }]
			});

			if (string.IsNullOrEmpty(path)) return;

			IsBusy = true;
			IsBusyOverlayText = App.Lang["Busy.SavingScanResults"];
			var dir = Path.GetDirectoryName(path)!;
			var tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp");

			try {
				var snapshot = Duplicates.ToList();

				if (!includeThumbnails) {
					await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
					await JsonSerializer.SerializeAsync(fs, snapshot, serializerOptions ?? JsonOptions);
				}
				else {
					await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
					using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
					var jsonEntry = zip.CreateEntry("scan.json", CompressionLevel.NoCompression);

					await using (var es = jsonEntry.Open()) {
						await JsonSerializer.SerializeAsync(es, snapshot, serializerOptions ?? JsonOptions);
						await es.FlushAsync();
					}

					Utils.ThumbCacheHelpers.Provider?.FlushIndex();

					if (TempDirectory != null) {
						var packPath = Path.Combine(TempDirectory.Path, "thumbs.pack");
						var idxPath = Path.Combine(TempDirectory.Path, "thumbs.idx");

						if (File.Exists(packPath) && Utils.ThumbCacheHelpers.Provider != null) {
							var packEntry = zip.CreateEntry("thumbs.pack", CompressionLevel.NoCompression);
							using var es = packEntry.Open();
							Utils.ThumbCacheHelpers.Provider.CopyTo(es);
						}

						if (File.Exists(idxPath)) {
							var idxEntry = zip.CreateEntry("thumbs.idx", CompressionLevel.NoCompression);
							using var es = idxEntry.Open();
							using var fs2 = File.OpenRead(idxPath);
							fs2.CopyTo(es);
						}
					}
				}

				File.Move(tmp, path, overwrite: true);
			}
			catch (Exception ex) {
				IsBusy = false;
				string error = string.Format(App.Lang["Message.ExportScanResultsFailed"], ex);
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
			finally {
				try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
				IsBusy = false;
			}
		}

		public ReactiveCommand<Unit, Unit> ImportScanResultsFromFileCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				FileTypeFilter = [new FilePickerFileType("Scan Results") { Patterns = ["*.zip"] }]
			});
			if (string.IsNullOrEmpty(result)) return;
			ImportScanResultsIncludingThumbnails(result);
		});

		async void ImportScanResultsIncludingThumbnails(string? path = null) {
			if (Duplicates.Count > 0) {
				MessageBoxButtons? result = await MessageBoxService.Show(App.Lang["Message.ImportScanResultsClearConfirm"], MessageBoxButtons.Yes | MessageBoxButtons.No);
				if (result != MessageBoxButtons.Yes) return;
			}

			if (path == null) {
				path = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
					SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
					FileTypeFilter = [new FilePickerFileType("Scan Results") { Patterns = ["*.zip"] }]
				});
			}
			if (string.IsNullOrEmpty(path)) return;

			try {
				using var stream = File.OpenRead(path);
				IsBusy = true;
				IsBusyOverlayText = App.Lang["Busy.ImportScanResults"];

				using var zip = ZipFile.OpenRead(path);
				var json = zip.GetEntry("scan.json") ?? throw new Exception("scan.json missing");
				await using var js = json.Open();
				var items = await JsonSerializer.DeserializeAsync<List<DuplicateItemVM>>(js, JsonOptions) ?? new();

				TempDirectory = TempExtractionManager.Register(new("VDF-"));

				// Validate ZIP entry paths to prevent path traversal (Zip Slip)
				foreach (var entryName in new[] { "thumbs.pack", "thumbs.idx" }) {
					var entry = zip.GetEntry(entryName);
					if (entry == null) continue;
					string dest = Path.GetFullPath(Path.Combine(TempDirectory.Path, entryName));
					if (!dest.StartsWith(Path.GetFullPath(TempDirectory.Path) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
						throw new InvalidOperationException($"ZIP entry '{entryName}' would extract outside target directory");
					entry.ExtractToFile(dest, true);
				}

				Utils.ThumbCacheHelpers.SetActiveProvider(Utils.ThumbPack.Open(TempDirectory.Path));

				Duplicates.Clear();
				foreach (var item in items)
					Duplicates.Add(item);

				BuildDuplicatesView();
				RefreshGroupStats();
				IsBusy = false;
				stream.Close();
			}
			catch (JsonException) {
				IsBusy = false;
				string error = App.Lang["Message.ImportScanResultsCorrupt"];
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
			catch (Exception ex) {
				IsBusy = false;
				string error = string.Format(App.Lang["Message.ImportScanResultsFailed"], ex);
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
		}

		public ReactiveCommand<DuplicateItemVM, Unit> OpenItemCommand => ReactiveCommand.Create<DuplicateItemVM>(currentItem => {
			OpenItems();
		});

		public ReactiveCommand<Unit, Unit> OpenItemsByColIdCommand => ReactiveCommand.Create(() => {
			var tag = GetDataGrid.CurrentColumn?.Tag as string;
			if (tag == "Thumbnail")
				OpenItems();
			else if (tag == "Path")
				OpenItemsInFolder();
		});

		public ReactiveCommand<Unit, Unit> ThumbnailDoubleClickCommand => ReactiveCommand.Create(() => {
			if (SettingsFile.Instance.ThumbnailDoubleClickAction == Data.ThumbnailDoubleClickAction.OpenThumbnailComparer)
				ShowGroupInThumbnailComparerCommand.Execute().Subscribe();
			else
				OpenItems();
		});

		public KeyValuePair<string, Data.ThumbnailDoubleClickAction>[] ThumbnailDoubleClickOptions { get; } = {
			new(App.Lang["MainWindow.Settings.ThumbnailDoubleClick.OpenFile"], Data.ThumbnailDoubleClickAction.OpenFile),
			new(App.Lang["MainWindow.Settings.ThumbnailDoubleClick.OpenThumbnailComparer"], Data.ThumbnailDoubleClickAction.OpenThumbnailComparer),
		};

		public ReactiveCommand<Unit, Unit> OpenItemInFolderCommand => ReactiveCommand.Create(OpenItemsInFolder);

		public ReactiveCommand<string, Unit> OpenGroupCommand => ReactiveCommand.Create<string>(openInFolder => {
			if (GetSelectedDuplicateItem() is DuplicateItemVM currentItem) {
				List<DuplicateItemVM> items = Duplicates.Where(d => d.ItemInfo.GroupId == currentItem.ItemInfo.GroupId).ToList();
				if (openInFolder == "0")
					AlternativeOpen(String.Empty, SettingsFile.Instance.CustomCommands.OpenMultiple, items);
				else
					AlternativeOpen(String.Empty, SettingsFile.Instance.CustomCommands.OpenMultipleInFolder, items);
			}
		});

		public async void OpenItems() {
			if (AlternativeOpen(SettingsFile.Instance.CustomCommands.OpenItem,
								SettingsFile.Instance.CustomCommands.OpenMultiple))
				return;

			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			try {
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
			catch (Exception ex) {
				await MessageBoxService.Show(string.Format(App.Lang["Message.OpenFilesFailed"], ex.Message));
				return;
			}
		}

		public async void OpenItemsInFolder() {
			if (AlternativeOpen(SettingsFile.Instance.CustomCommands.OpenItemInFolder,
								SettingsFile.Instance.CustomCommands.OpenMultipleInFolder))
				return;

			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			try {
				if (OperatingSystem.IsWindows()) {
					try {
						Utils.ShellUtils.ShowInExplorer(currentItem.ItemInfo.Path);
					}
					catch {
						// Fallback to explorer.exe if shell API fails (Notepad++/Electron pattern)
						var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
						psi.ArgumentList.Add($"/select,{currentItem.ItemInfo.Path}");
						Process.Start(psi);
					}
				}
				else if (OperatingSystem.IsMacOS()) {
					var psi = new ProcessStartInfo("open") { UseShellExecute = false };
					psi.ArgumentList.Add("-R");
					psi.ArgumentList.Add(currentItem.ItemInfo.Path);
					Process.Start(psi);
				}
				else {
					Process.Start(new ProcessStartInfo {
						FileName = currentItem.ItemInfo.Folder,
						UseShellExecute = true,
						Verb = "open"
					});
				}
			}
			catch (Exception ex) {
				await MessageBoxService.Show(string.Format(App.Lang["Message.OpenFilesFailed"], ex.Message));
				return;
			}
		}

		private bool AlternativeOpen(string cmdSingle, string cmdMulti, List<DuplicateItemVM>? items = null) {
			if (string.IsNullOrEmpty(cmdSingle) && string.IsNullOrEmpty(cmdMulti))
				return false;

			if (items == null) {
				items = new();
				if (!string.IsNullOrEmpty(cmdMulti)) {
					foreach (var selectedItem in GetSelectedDuplicates())
						if (selectedItem is DuplicateItemVM item)
							items.Add(item);
				}
				else {
					if (GetSelectedDuplicateItem() is DuplicateItemVM duplicateItem)
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
				if (command[0] == '"' || command[0] == '\'') {
					cmd = command.Split(command[0] + " ", 2);
					cmd[0] += command[0];
				}
				else
					cmd = command.Split(' ', 2);
			}
			if (string.IsNullOrEmpty(cmd?[0]))
				return false;

			command = cmd[0];
			var psi = new ProcessStartInfo {
				FileName = command,
				UseShellExecute = false,
				RedirectStandardError = true,
			};

			if (cmd.Length == 2 && cmd[1].Contains("%f")) {
				// When the user has a %f placeholder, substitute file paths into the argument template.
				// Each file gets its own copy of the template as a separate argument to avoid injection.
				foreach (var item in items) {
					psi.ArgumentList.Add(cmd[1].Replace("%f", item.ItemInfo.Path));
				}
			}
			else {
				if (cmd.Length == 2)
					psi.ArgumentList.Add(cmd[1]);
				foreach (var item in items)
					psi.ArgumentList.Add(item.ItemInfo.Path);
			}

			try {
				Process.Start(psi);
			}
			catch (Exception e) {
				Logger.Instance.Info(string.Format(App.Lang["Log.CustomCommandFailed"], command,
					string.Join(" ", psi.ArgumentList), e.Message));
			}

			return true;
		}

		public ReactiveCommand<Unit, Unit> RenameFileCommand => ReactiveCommand.CreateFromTask(async () => {
			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			if (!File.Exists(currentItem.ItemInfo.Path)) {
				await MessageBoxService.Show(App.Lang["Message.FileNoLongerExists"]);
				return;
			}
			var fi = new FileInfo(currentItem.ItemInfo.Path);
			Debug.Assert(fi.Directory != null, "fi.Directory != null");
			string newName = await InputBoxService.Show("Enter new name", Path.GetFileNameWithoutExtension(fi.FullName), title: "Rename File", readOnlyInfo: fi.FullName);
			if (string.IsNullOrEmpty(newName)) return;
			newName = FileUtils.SafePathCombine(fi.DirectoryName!, newName + fi.Extension);
			while (File.Exists(newName) && !string.Equals(fi.FullName, newName, StringComparison.OrdinalIgnoreCase)) {
				MessageBoxButtons? result = await MessageBoxService.Show($"A file with the name '{Path.GetFileName(newName)}' already exists. Do you want to overwrite this file? Click on 'No' to enter a new name", MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel);
				if (result == null || result == MessageBoxButtons.Cancel)
					return;
				if (result == MessageBoxButtons.Yes)
					break;
				newName = await InputBoxService.Show("Enter new name", Path.GetFileNameWithoutExtension(newName), title: "Rename File", readOnlyInfo: fi.FullName);
				if (string.IsNullOrEmpty(newName))
					return;
				newName = FileUtils.SafePathCombine(fi.DirectoryName!, newName + fi.Extension);
			}
			try {
				ScanEngine.GetFromDatabase(currentItem.ItemInfo.Path, out var dbEntry);
				fi.MoveTo(newName, true);
				ScanEngine.UpdateFilePathInDatabase(newName, dbEntry!);
				currentItem.ItemInfo.Path = newName;
				ScanEngine.SaveDatabase();
			}
			catch (Exception e) {
				await MessageBoxService.Show(e.Message);
			}
		});

		public ReactiveCommand<Unit, Unit> ToggleCheckboxCommand => ReactiveCommand.Create(() => {
			foreach (var item in GetSelectedDuplicates()) {
				if (item is not DuplicateItemVM currentItem) return;
				currentItem.Checked = !currentItem.Checked;
			}
		});

		static int MapToFfmpegMajor(int avcodecMajor, int avformatMajor, int avutilMajor) {
			int[] majors = { avcodecMajor, avformatMajor, avutilMajor };
			int want = 0;
			foreach (var m in majors) {
				int v = m switch {
					62 => 8,
					61 => 7,
					60 => 6,
					59 => 5,
					_ => 0
				};
				if (v > want) want = v;
			}
			return want;
		}

		static string ArchString(Architecture a) => a switch {
			Architecture.X64 => "x64 (64-bit)",
			Architecture.X86 => "x86 (32-bit)",
			Architecture.Arm64 => "arm64 (64-bit ARM)",
			Architecture.Arm => "arm (32-bit ARM)",
			_ => a.ToString()
		};

		public static string GetRequiredFfmpegPackage(string? vdfFolderPath = null) {
			int avcodec = ffmpeg.LIBAVCODEC_VERSION_MAJOR;
			int avformat = ffmpeg.LIBAVFORMAT_VERSION_MAJOR;
			int avutil = ffmpeg.LIBAVUTIL_VERSION_MAJOR;

			int ffMajor = MapToFfmpegMajor(avcodec, avformat, avutil);

			string platform =
				OperatingSystem.IsWindows() ? "Windows" :
				OperatingSystem.IsMacOS() ? "macOS" :
				OperatingSystem.IsLinux() ? "Linux" : "Unknown";

			var osArch = RuntimeInformation.OSArchitecture;
			var procArch = RuntimeInformation.ProcessArchitecture;
			string versionPart = ffMajor == 0 ? "unknown (old headers?)" : $"{ffMajor}.x";
			string archPart = ArchString(procArch);

			var msg =
$@"FFmpeg was not found.

Which FFmpeg you need:
  • Version: FFmpeg {versionPart}
  • Architecture: {archPart}

Platform/OS:
  • {platform} · OS arch: {ArchString(osArch)} · Process arch: {ArchString(procArch)}

Notes:
  • On ARM64 systems (e.g., Windows/macOS ARM): if your app runs as x64 (emulation/Rosetta), use x64 FFmpeg.
    If it runs natively as ARM64, use ARM64 FFmpeg.";

			if (OperatingSystem.IsWindows()) {
				string target = string.IsNullOrWhiteSpace(vdfFolderPath)
					? @"<YourApp>\VDF\bin"
					: System.IO.Path.Combine(vdfFolderPath, "bin");

				msg +=
	$@"

Windows setup:
  1) Create a folder named 'bin' inside your VDF folder:
       {target}
  2) Download the FFmpeg {versionPart} shared build for {archPart}.
  3) Extract these DLLs into that 'bin' folder:
       avcodec-*.dll, avformat-*.dll, avutil-*.dll, swresample-*.dll, swscale-*.dll";
			}
			else {
				msg +=
	@"

Non-Windows setup:
  • Recommended: Check Github instructions
  • Use the shared libraries matching the required version/architecture.
  • Make sure the dynamic loader can find them (e.g., alongside your app binary,
    via rpath, or environment variables like LD_LIBRARY_PATH/DYLD_LIBRARY_PATH).
  • Typical library names:
      - Linux: libavcodec.so.*, libavformat.so.*, libavutil.so.*, libswresample.so.*, libswscale.so.*
      - macOS: libavcodec.*.dylib, libavformat.*.dylib, libavutil.*.dylib, libswresample.*.dylib, libswscale.*.dylib";
			}

			return msg;
		}

		public ReactiveCommand<string, Unit> StartScanCommand => ReactiveCommand.CreateFromTask(async (string command) => {
			if (!string.IsNullOrEmpty(SettingsFile.Instance.CustomDatabaseFolder) && !Directory.Exists(SettingsFile.Instance.CustomDatabaseFolder)) {
				await MessageBoxService.Show(App.Lang["Message.CustomDatabaseFolderMissing"]);
				return;
			}
			if (Duplicates.Count > 0) {
				if (await MessageBoxService.Show(App.Lang["Message.DiscardResultsPrompt"], MessageBoxButtons.Yes | MessageBoxButtons.No) != MessageBoxButtons.Yes) {
					return;
				}
			}

			if ((SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) ||
				(!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists) ||
				!ScanEngine.FFprobeExists) {
				await DownloadSharedFfmpegAsync();
			}
			if ((SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) ||
				(!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists)) {
				await MessageBoxService.Show(GetRequiredFfmpegPackage(CoreUtils.CurrentFolder));
				return;
			}
			if (!ScanEngine.FFprobeExists) {
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					await MessageBoxService.Show(App.Lang["Message.FFprobeMissingWithHint"]);
				}
				else {
					await MessageBoxService.Show(App.Lang["Message.FFprobeMissing"]);
				}
				return;
			}
			if (SettingsFile.Instance.UseNativeFfmpegBinding && SettingsFile.Instance.HardwareAccelerationMode == Core.FFTools.FFHardwareAccelerationMode.auto) {
				await MessageBoxService.Show(App.Lang["Message.NativeFfmpegAutoNotSupported"]);
				return;
			}
			if (SettingsFile.Instance.Includes.Count == 0) {
				await MessageBoxService.Show(App.Lang["Message.NoScanFolders"]);
				return;
			}
			if (SettingsFile.Instance.MaxDegreeOfParallelism == 0) {
				await MessageBoxService.Show(App.Lang["Message.MaxDegreeOfParallelismInvalid"]);
				return;
			}
			if (SettingsFile.Instance.FilterByFileSize && SettingsFile.Instance.MaximumFileSize <= SettingsFile.Instance.MinimumFileSize) {
				await MessageBoxService.Show(App.Lang["Message.FileSizeFilterInvalid"]);
				return;
			}
			bool isFreshScan = true;
			switch (command) {
			case "FullScan":
				isFreshScan = true;
				break;
			case "CompareOnly":
				isFreshScan = false;
				if (await MessageBoxService.Show(App.Lang["Message.RescanConfirm"], MessageBoxButtons.Yes | MessageBoxButtons.No) != MessageBoxButtons.Yes)
					return;
				break;
			default:
				await MessageBoxService.Show(App.Lang["Message.CommandNotImplemented"]);
				break;
			}

			Duplicates.Clear();

			TempDirectory = TempExtractionManager.Register(new("VDF-"));
			Utils.ThumbCacheHelpers.SetActiveProvider(Utils.ThumbPack.Open(TempDirectory.Path));

			IsScanning = true;
			IsReadyToCompare = false;
			IsGathered = false;
			TotalDuplicateGroups = 0;
			TotalDuplicates = 0;
			TotalDuplicatesSize = string.Empty;

			SettingsFile.SaveSettings();
			Scanner.Settings.IncludeSubDirectories = SettingsFile.Instance.IncludeSubDirectories;
			Scanner.Settings.IncludeImages = SettingsFile.Instance.IncludeImages;
			Scanner.Settings.GeneratePreviewThumbnails = SettingsFile.Instance.GeneratePreviewThumbnails;
			Scanner.Settings.IgnoreReadOnlyFolders = SettingsFile.Instance.IgnoreReadOnlyFolders;
			Scanner.Settings.IgnoreReparsePoints = SettingsFile.Instance.IgnoreReparsePoints;
			Scanner.Settings.ExcludeHardLinks = SettingsFile.Instance.ExcludeHardLinks;
			Scanner.Settings.HardwareAccelerationMode = SettingsFile.Instance.HardwareAccelerationMode;
			Scanner.Settings.Percent = SettingsFile.Instance.Percent;
			Scanner.Settings.PercentDurationDifference = SettingsFile.Instance.PercentDurationDifference;
			Scanner.Settings.DurationDifferenceMinSeconds = SettingsFile.Instance.DurationDifferenceMinSeconds;
			Scanner.Settings.DurationDifferenceMaxSeconds = SettingsFile.Instance.DurationDifferenceMaxSeconds;
			Scanner.Settings.MaxSamplingDurationSeconds = SettingsFile.Instance.MaxSamplingDurationSeconds;
			Scanner.Settings.MaxDegreeOfParallelism = SettingsFile.Instance.MaxDegreeOfParallelism;
			Scanner.Settings.ThumbnailCount = SettingsFile.Instance.Thumbnails;
			Scanner.Settings.ThumbnailMaxWidth = SettingsFile.Instance.ThumbnailMaxWidth;
			Scanner.Settings.ExtendedFFToolsLogging = SettingsFile.Instance.ExtendedFFToolsLogging;
			Scanner.Settings.LogExcludedFiles = SettingsFile.Instance.LogExcludedFiles;
			Scanner.Settings.AlwaysRetryFailedSampling = SettingsFile.Instance.AlwaysRetryFailedSampling;
			Scanner.Settings.CustomFFArguments = SettingsFile.Instance.CustomFFArguments;
			Scanner.Settings.UseNativeFfmpegBinding = SettingsFile.Instance.UseNativeFfmpegBinding;
			Scanner.Settings.IgnoreBlackPixels = SettingsFile.Instance.IgnoreBlackPixels;
			Scanner.Settings.IgnoreWhitePixels = SettingsFile.Instance.IgnoreWhitePixels;
			Scanner.Settings.CompareHorizontallyFlipped = SettingsFile.Instance.CompareHorizontallyFlipped;
			Scanner.Settings.CustomDatabaseFolder = SettingsFile.Instance.CustomDatabaseFolder;
			Scanner.Settings.DatabaseCheckpointIntervalMinutes = SettingsFile.Instance.DatabaseCheckpointIntervalMinutes;
			SettingsFile.Instance.LanguageCode = App.Lang.CurrentLanguage;
			Scanner.Settings.LanguageCode = SettingsFile.Instance.LanguageCode;
			Scanner.Settings.IncludeNonExistingFiles = SettingsFile.Instance.IncludeNonExistingFiles;
			Scanner.Settings.FilterByFilePathContains = SettingsFile.Instance.FilterByFilePathContains;
			Scanner.Settings.FilePathContainsTexts = SettingsFile.Instance.FilePathContainsTexts.ToList();
			Scanner.Settings.FilterByFilePathNotContains = SettingsFile.Instance.FilterByFilePathNotContains;
			Scanner.Settings.ScanAgainstEntireDatabase = SettingsFile.Instance.ScanAgainstEntireDatabase;
			Scanner.Settings.FolderMatchMode = SettingsFile.Instance.FolderMatchMode;
			Scanner.Settings.SameFolderDepth = SettingsFile.Instance.SameFolderDepth;
			Scanner.Settings.UsePHashing = SettingsFile.Instance.UsePHash;
			Scanner.Settings.UseExifCreationDate = SettingsFile.Instance.UseExifCreationDate;
			Scanner.Settings.FilePathNotContainsTexts = SettingsFile.Instance.FilePathNotContainsTexts.ToList();
			Scanner.Settings.FilterByFileSize = SettingsFile.Instance.FilterByFileSize;
			Scanner.Settings.MaximumFileSize = SettingsFile.Instance.MaximumFileSize;
			Scanner.Settings.MinimumFileSize = SettingsFile.Instance.MinimumFileSize;
			Scanner.Settings.EnablePartialClipDetection = SettingsFile.Instance.EnablePartialClipDetection;
			Scanner.Settings.PartialClipMinRatio = SettingsFile.Instance.PartialClipMinRatioPercent / 100.0;
			Scanner.Settings.PartialClipSimilarityThreshold = SettingsFile.Instance.PartialClipSimilarityThresholdPercent / 100.0;
			Scanner.Settings.IncludeList.Clear();
			foreach (var s in SettingsFile.Instance.Includes)
				Scanner.Settings.IncludeList.Add(s);
			Scanner.Settings.BlackList.Clear();
			foreach (var s in SettingsFile.Instance.Blacklists)
				Scanner.Settings.BlackList.Add(s);

			ChangeIsBusyMessage();
			IsBusy = true;

			if (isFreshScan) {
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
			IsBusyOverlayText = "Stopping all scan threads...";
			Scanner.Stop();
		}, this.WhenAnyValue(x => x.IsScanning));

		public ReactiveCommand<Unit, Unit> MarkGroupAsNotAMatchCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(async () => {
				if (GetSelectedDuplicateItem() is not DuplicateItemVM data) return;

				var gid = data.ItemInfo.GroupId;

				HashSet<string> blacklist = new HashSet<string>();
				foreach (DuplicateItemVM duplicateItem in Duplicates.Where(d => d.ItemInfo.GroupId == gid))
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

				// Remove all items in this group from the list
				foreach (var path in blacklist.ToList())
					for (int i = Duplicates.Count - 1; i >= 0; i--)
						if (Duplicates[i].ItemInfo.GroupId == gid && Duplicates[i].ItemInfo.Path == path)
							Duplicates.RemoveAt(i);

				// Drop singleton groups
				DropSingletonGroups();
				RefreshGroupStats();
				view?.Refresh();
			});
		});

		public ReactiveCommand<Unit, Unit> ShowGroupInThumbnailComparerCommand => ReactiveCommand.Create(() => {
			if (GetSelectedDuplicateItem() is not DuplicateItemVM data) return;
			List<LargeThumbnailDuplicateItem> items = new();

			if (GetSelectedDuplicates().Count == 1) {
				foreach (DuplicateItemVM duplicateItem in Duplicates.Where(d => d.ItemInfo.GroupId == data.ItemInfo.GroupId))
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
			}
			else {
				foreach (DuplicateItemVM duplicateItem in GetSelectedDuplicates())
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
			}

			ThumbnailComparer thumbnailComparer = new(items);
			thumbnailComparer.Show();
		});

		public void CompareGroup(Guid groupId) {
			var items = Duplicates
				.Where(d => d.ItemInfo.GroupId == groupId)
				.Select(d => new LargeThumbnailDuplicateItem(d))
				.ToList();
			if (items.Count == 0) return;
			new ThumbnailComparer(items).Show();
		}

		public void KeepBestInGroup(Guid groupId) {
			var groupItems = Duplicates.Where(d => d.ItemInfo.GroupId == groupId).ToList();
			if (groupItems.Count < 2) return;

			var keep = groupItems[0];
			bool anyApplied = false;
			string? lastCriterion = null;

			foreach (var criterion in QualityCriteriaOrder) {
				if (criterion is ("Duration" or "FPS" or "Bitrate" or "Audio Bitrate") && keep.ItemInfo.IsImage)
					continue;
				bool tieOnLast = anyApplied && HasTieOn(lastCriterion!, groupItems, keep);
				if (!anyApplied || tieOnLast) {
					keep = ApplyCriterion(criterion, groupItems);
					anyApplied = true;
					lastCriterion = criterion;
				}
			}

			keep.Checked = false;
			foreach (var item in groupItems)
				if (item.ItemInfo.Path != keep.ItemInfo.Path)
					item.Checked = true;
		}

	public ReactiveCommand<Unit, Unit> LoadThumbnailsForCheckedItemsCommand => ReactiveCommand.CreateFromTask(async () => {
		var items = Duplicates.Where(d => d.Checked).Select(vm => vm.ItemInfo).ToList();
		if (items.Count == 0) return;
		await Scanner.RetrieveThumbnailsForItems(items);
	});

	public ReactiveCommand<Unit, Unit> LoadThumbnailsForGroupCommand => ReactiveCommand.CreateFromTask(async () => {
		if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
		var items = Duplicates.Where(d => d.ItemInfo.GroupId == currentItem.ItemInfo.GroupId).Select(d => d.ItemInfo).ToList();
		await Scanner.RetrieveThumbnailsForItems(items);
	});

		List<DuplicateItemVM> CheckedItemsToDelete => Duplicates.Where(d => d.Checked && d.IsVisibleInFilter).ToList();

		async void DeleteInternal(bool fromDisk,
									List<DuplicateItemVM>? toDelete = null,
									bool blackList = false,
									bool createSymbolLinksInstead = false,
									bool permanently = false) {
			if (Duplicates.Count == 0) return;
			toDelete ??= CheckedItemsToDelete;
			if (toDelete.Count == 0) return;

			MessageBoxButtons? dlgResult = await MessageBoxService.Show(
				fromDisk
					? (!permanently ? App.Lang["Message.DeleteToTrashConfirm"] : App.Lang["Message.DeletePermanentlyConfirm"])
					: (blackList ? App.Lang["Message.DeleteFromListBlacklistConfirm"] : App.Lang["Message.DeleteFromListConfirm"]),
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;

			var keepByGroup = Duplicates
				   .GroupBy(d => d.ItemInfo.GroupId)
				   .ToDictionary(
					   g => g.Key,
					   g => g.FirstOrDefault(x => !x.Checked)
				   );

			var actuallyDeleted = new HashSet<DuplicateItemVM>(toDelete.Count, ReferenceEqualityComparer<DuplicateItemVM>.Instance);
			long freedBytes = 0;
			foreach (var dub in toDelete) {
				try {
					var fe = new FileEntry(dub.ItemInfo.Path);
					if (fromDisk) {
						if (createSymbolLinksInstead) {
							var keeper = keepByGroup.TryGetValue(dub.ItemInfo.GroupId, out var k) ? k : null;
							if (keeper == null)
								throw new Exception($"Cannot create symlink for '{dub.ItemInfo.Path}' because all items in this group are selected");
							File.CreateSymbolicLink(dub.ItemInfo.Path, keeper.ItemInfo.Path);
							freedBytes += dub.ItemInfo.SizeLong;
						}
						else if (!permanently && CoreUtils.IsWindows) {
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
							freedBytes += dub.ItemInfo.SizeLong;
						}
						else if (!permanently) {
							// Linux/macOS: attempt to move to system trash, fall back to permanent delete
							if (!FileUtils.MoveToTrash(dub.ItemInfo.Path))
								File.Delete(dub.ItemInfo.Path);
							freedBytes += dub.ItemInfo.SizeLong;
						}
						else {
							File.Delete(dub.ItemInfo.Path);
							freedBytes += dub.ItemInfo.SizeLong;
						}
					}

					if (blackList)
						ScanEngine.BlackListFileEntry(dub.ItemInfo.Path);
					else
						ScanEngine.RemoveFromDatabase(fe);

					actuallyDeleted.Add(dub);
				}
				catch (Exception ex) {
					Logger.Instance.Info($"Failed to delete '{dub.ItemInfo.Path}': {ex.Message}\n{ex.StackTrace}");
				}
			}

			if (freedBytes > 0)
				TotalSizeRemovedInternal += freedBytes;

			if (actuallyDeleted.Count == 0)
				return;

			// Remove deleted items from flat list
			foreach (var item in actuallyDeleted) {
				for (int i = Duplicates.Count - 1; i >= 0; i--)
					if (ReferenceEquals(Duplicates[i], item)) { Duplicates.RemoveAt(i); break; }
			}

			// When ExcludeHardLinks is enabled, remove items within each group
			// that are hardlinks of another remaining item in the same group.
			if (SettingsFile.Instance.ExcludeHardLinks)
				DropHardLinkDuplicates();

			// Drop groups that have only one item left (no longer duplicates)
			DropSingletonGroups();

			RefreshGroupStats();
			view?.Refresh();

			ScanEngine.SaveDatabase();

			if (SettingsFile.Instance.BackupAfterListChanged)
				await ExportScanResults(BackupScanResultsFile);
		}

		private void DropSingletonGroups() {
			var singletonGroups = Duplicates
				.GroupBy(d => d.ItemInfo.GroupId)
				.Where(g => g.Count() <= 1)
				.Select(g => g.Key)
				.ToHashSet();

			if (singletonGroups.Count == 0) return;

			for (int i = Duplicates.Count - 1; i >= 0; i--)
				if (singletonGroups.Contains(Duplicates[i].ItemInfo.GroupId))
					Duplicates.RemoveAt(i);
		}

		/// <summary>
		/// After deletion, re-check remaining groups for items that are hardlinks
		/// of each other. Within each group, keep only one representative per set
		/// of hardlinked files.
		/// </summary>
		private void DropHardLinkDuplicates() {
			var toRemove = new List<DuplicateItemVM>();
			foreach (var group in Duplicates.GroupBy(d => d.ItemInfo.GroupId)) {
				var items = group.ToList();
				if (items.Count <= 1) continue;
				var kept = new List<DuplicateItemVM>();
				foreach (var item in items) {
					bool isHardLinkOfKept = false;
					foreach (var k in kept) {
						if (item.ItemInfo.SizeLong == k.ItemInfo.SizeLong &&
							HardLinkUtils.AreSameFile(item.ItemInfo.Path, k.ItemInfo.Path)) {
							isHardLinkOfKept = true;
							break;
						}
					}
					if (isHardLinkOfKept)
						toRemove.Add(item);
					else
						kept.Add(item);
				}
			}

			foreach (var item in toRemove)
				for (int i = Duplicates.Count - 1; i >= 0; i--)
					if (ReferenceEquals(Duplicates[i], item)) { Duplicates.RemoveAt(i); break; }
		}

		public ReactiveCommand<Unit, Unit> ExpandAllGroupsCommand => ReactiveCommand.Create(() => {
			if (view == null) return;
			foreach (var group in view.Groups ?? Enumerable.Empty<object>())
				if (group is DataGridCollectionViewGroup g)
					GetDataGrid.ExpandRowGroup(g, true);
		});

		public ReactiveCommand<Unit, Unit> CollapseAllGroupsCommand => ReactiveCommand.Create(() => {
			if (view == null) return;
			foreach (var group in view.Groups ?? Enumerable.Empty<object>())
				if (group is DataGridCollectionViewGroup g)
					GetDataGrid.CollapseRowGroup(g, true);
		});

		public ReactiveCommand<Unit, Unit> NavigateNextGroupCommand => ReactiveCommand.Create(() => {
			NavigateGroup(forward: true);
		});

		public ReactiveCommand<Unit, Unit> NavigatePreviousGroupCommand => ReactiveCommand.Create(() => {
			NavigateGroup(forward: false);
		});

		void NavigateGroup(bool forward) {
			if (view?.Groups == null) return;
			var groups = view.Groups.OfType<DataGridCollectionViewGroup>().ToList();
			if (groups.Count == 0) return;

			var dataGrid = GetDataGrid;
			var currentItem = dataGrid.SelectedItem as DuplicateItemVM;
			int currentGroupIndex = -1;

			if (currentItem != null) {
				for (int i = 0; i < groups.Count; i++) {
					if (groups[i].Items.OfType<DuplicateItemVM>()
						.Any(item => item.ItemInfo.GroupId == currentItem.ItemInfo.GroupId)) {
						currentGroupIndex = i;
						break;
					}
				}
			}

			int targetIndex = forward
				? (currentGroupIndex + 1 < groups.Count ? currentGroupIndex + 1 : 0)
				: (currentGroupIndex - 1 >= 0 ? currentGroupIndex - 1 : groups.Count - 1);

			var targetGroup = groups[targetIndex];
			dataGrid.ExpandRowGroup(targetGroup, true);
			var firstItem = targetGroup.Items.OfType<DuplicateItemVM>().FirstOrDefault();
			if (firstItem != null) {
				dataGrid.SelectedItem = firstItem;
				dataGrid.ScrollIntoView(firstItem, null);
			}
		}

		public ReactiveCommand<Unit, Unit> CopyPathsToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetSelectedDuplicates()) {
				if (item is not DuplicateItemVM currentItem) return;
				sb.AppendLine($"\"{currentItem.ItemInfo.Path}\"");
			}
#pragma warning disable CS8600
#pragma warning disable CS8602
			await (ApplicationHelpers.MainWindow.Clipboard.SetTextAsync(sb.ToString().TrimEnd(new char[2] { '\r', '\n' })));
#pragma warning restore CS8602
#pragma warning restore CS8600
		});

		public ReactiveCommand<Unit, Unit> CopyFilenamesToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetSelectedDuplicates()) {
				if (item is not DuplicateItemVM currentItem) return;
				sb.AppendLine(Path.GetFileName(currentItem.ItemInfo.Path));
			}
#pragma warning disable CS8600
#pragma warning disable CS8602
			await (ApplicationHelpers.MainWindow.Clipboard.SetTextAsync(sb.ToString().TrimEnd(new char[2] { '\r', '\n' })));
#pragma warning restore CS8602
#pragma warning restore CS8600
		});

		public ReactiveCommand<Unit, Unit> RelocateDatabaseFilesCommand => ReactiveCommand.Create(() => {
			var dlg = new RelocateFilesDialog();
			dlg.ShowDialog(ApplicationHelpers.MainWindow);
		});
	}
}
