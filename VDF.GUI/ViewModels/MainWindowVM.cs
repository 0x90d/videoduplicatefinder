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
using Avalonia.Threading;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Text;
using System.Xml.Linq;
using Avalonia.Collections;
using DynamicExpresso;
using DynamicExpresso.Exceptions;
using JetBrains.Annotations;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Views;
using System.ComponentModel;
using System.Text.Json;

namespace VDF.GUI.ViewModels {
	public class MainWindowVM : ReactiveObject {
		public ScanEngine Scanner { get; } = new ScanEngine();
		public ObservableCollection<string> LogItems { get; } = new ObservableCollection<string>();
		public ObservableCollection<string> Includes { get; } = new ObservableCollection<string>();
		public ObservableCollection<string> Blacklists { get; } = new ObservableCollection<string>();
		List<HashSet<string>> GroupBlacklist = new List<HashSet<string>>();


		[CanBeNull] DataGridCollectionView view;
		ObservableCollection<DuplicateItemVM> Duplicates { get; } = new ObservableCollection<DuplicateItemVM>();
		public KeyValuePair<string, DataGridSortDescription>[] SortOrders { get; } = {
			new KeyValuePair<string, DataGridSortDescription>("None", null),
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
		};
		public KeyValuePair<string, FileTypeFilter>[] TypeFilters { get; } = {
			new KeyValuePair<string, FileTypeFilter>("All",  FileTypeFilter.All),
			new KeyValuePair<string, FileTypeFilter>("Videos",  FileTypeFilter.Videos),
			new KeyValuePair<string, FileTypeFilter>("Images",  FileTypeFilter.Images),
		};

		string _SearchText;
		public string SearchText {
			get => _SearchText;
			set => this.RaiseAndSetIfChanged(ref _SearchText, value);
		}
		string _CustomFFArguments = string.Empty;
		public string CustomFFArguments {
			get => _CustomFFArguments;
			set => this.RaiseAndSetIfChanged(ref _CustomFFArguments, value);
		}
		bool _IsScanning;
		public bool IsScanning {
			get => _IsScanning;
			set => this.RaiseAndSetIfChanged(ref _IsScanning, value);
		}
		bool _IsPaused;
		public bool IsPaused {
			get => _IsPaused;
			set => this.RaiseAndSetIfChanged(ref _IsPaused, value);
		}

		bool _IgnoreReadOnlyFolders;
		public bool IgnoreReadOnlyFolders {
			get => _IgnoreReadOnlyFolders;
			set => this.RaiseAndSetIfChanged(ref _IgnoreReadOnlyFolders, value);
		}
		bool _IgnoreHardlinks;
		public bool IgnoreHardlinks {
			get => _IgnoreHardlinks;
			set => this.RaiseAndSetIfChanged(ref _IgnoreHardlinks, value);
		}
		int _MaxDegreeOfParallelism = 1;
		public int MaxDegreeOfParallelism {
			get => _MaxDegreeOfParallelism;
			set => this.RaiseAndSetIfChanged(ref _MaxDegreeOfParallelism, value);
		}
		public IEnumerable<Core.FFTools.FFHardwareAccelerationMode> HardwareAccelerationModes =>
			Enum.GetValues<Core.FFTools.FFHardwareAccelerationMode>();
		Core.FFTools.FFHardwareAccelerationMode _HardwareAccelerationMode = Core.FFTools.FFHardwareAccelerationMode.auto;
		public Core.FFTools.FFHardwareAccelerationMode HardwareAccelerationMode {
			get => _HardwareAccelerationMode;
			set => this.RaiseAndSetIfChanged(ref _HardwareAccelerationMode, value);
		}
		bool _IncludeSubDirectories = true;
		public bool IncludeSubDirectories {
			get => _IncludeSubDirectories;
			set => this.RaiseAndSetIfChanged(ref _IncludeSubDirectories, value);
		}
		bool _IncludeImages = true;
		public bool IncludeImages {
			get => _IncludeImages;
			set => this.RaiseAndSetIfChanged(ref _IncludeImages, value);
		}
		bool _GeneratePreviewThumbnails = true;
		public bool GeneratePreviewThumbnails {
			get => _GeneratePreviewThumbnails;
			set => this.RaiseAndSetIfChanged(ref _GeneratePreviewThumbnails, value);
		}
		bool _ExtendedFFToolsLogging = false;
		public bool ExtendedFFToolsLogging {
			get => _ExtendedFFToolsLogging;
			set => this.RaiseAndSetIfChanged(ref _ExtendedFFToolsLogging, value);
		}

		string _ScanProgressText;
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
		string _BusyText;
		public string IsBusyText {
			get => _BusyText;
			set => this.RaiseAndSetIfChanged(ref _BusyText, value);
		}
		int _ScanProgressMaxValue = 100;
		public int ScanProgressMaxValue {
			get => _ScanProgressMaxValue;
			set => this.RaiseAndSetIfChanged(ref _ScanProgressMaxValue, value);
		}
		int _Percent = 95;
		public int Percent {
			get => _Percent;
			set => this.RaiseAndSetIfChanged(ref _Percent, value);
		}
		int _Thumbnails = 1;
		public int Thumbnails {
			get => _Thumbnails;
			set => this.RaiseAndSetIfChanged(ref _Thumbnails, value);
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
		string _TotalDuplicatesSize;
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
		string _FilterByPath;

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
			var dir = new DirectoryInfo(Utils.ImageUtils.ThumbnailDirectory);
			if (!dir.Exists)
				dir.Create();
			FileInfo groupBlacklistFile = new(FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "BlacklistedGroups.json"));
			if (groupBlacklistFile.Exists && groupBlacklistFile.Length > 0) {
				using var stream = new FileStream(groupBlacklistFile.FullName, FileMode.Open);
				GroupBlacklist = JsonSerializer.Deserialize<List<HashSet<string>>>(stream);
			}
			_FileType = TypeFilters[0];
			_SortOrder = SortOrders[0];
			Scanner.ScanDone += Scanner_ScanDone;
			Scanner.Progress += Scanner_Progress;
			Scanner.ThumbnailsRetrieved += Scanner_ThumbnailsRetrieved;
			Scanner.DatabaseCleaned += Scanner_DatabaseCleaned;
			Scanner.FilesEnumerated += Scanner_FilesEnumerated;
			Logger.Instance.LogItemAdded += Instance_LogItemAdded;
			//Ensure items added before GUI was ready will be shown 
			Instance_LogItemAdded(string.Empty);

		}

		void Scanner_ThumbnailsRetrieved(object sender, EventArgs e) {
			//Reset properties
			ScanProgressText = string.Empty;
			RemainingTime = new TimeSpan();
			ScanProgressValue = 0;
			ScanProgressMaxValue = 100;
		}

		void Scanner_FilesEnumerated(object sender, EventArgs e) => IsBusy = false;

		async void Scanner_DatabaseCleaned(object sender, EventArgs e) {
			IsBusy = false;
			await MessageBoxService.Show("Database cleaned!");
		}

		public void SaveSettings() {
			var path = FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Settings.xml");
			var includes = new object[Includes.Count];
			for (var i = 0; i < Includes.Count; i++) {
				includes[i] = new XElement("Include", Includes[i]);
			}
			var excludes = new object[Blacklists.Count];
			for (var i = 0; i < Blacklists.Count; i++) {
				excludes[i] = new XElement("Exclude", Blacklists[i]);
			}

			var xDoc = new XDocument(new XElement("Settings",
					new XElement("Includes", includes),
					new XElement("Excludes", excludes),
					new XElement("Percent", Percent),
					new XElement("Thumbnails", Thumbnails),
					new XElement("IncludeSubDirectories", IncludeSubDirectories),
					new XElement("IncludeImages", IncludeImages),
					new XElement("IgnoreHardlinks", IgnoreHardlinks),
					new XElement("IgnoreReadOnlyFolders", IgnoreReadOnlyFolders),
					new XElement("HardwareAccelerationMode", HardwareAccelerationMode),
					new XElement("MaxDegreeOfParallelism", MaxDegreeOfParallelism),
					new XElement("GeneratePreviewThumbnails", GeneratePreviewThumbnails),
					new XElement("ExtendedFFToolsLogging", ExtendedFFToolsLogging),
					new XElement("CustomFFArguments", CustomFFArguments)
				)
			);
			xDoc.Save(path);
		}
		public void LoadSettings() {
			var path = FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Settings.xml");
			if (!File.Exists(path)) return;
			var xDoc = XDocument.Load(path);
			foreach (var n in xDoc.Descendants("Include"))
				Includes.Add(n.Value);
			foreach (var n in xDoc.Descendants("Exclude"))
				Blacklists.Add(n.Value);
			foreach (var n in xDoc.Descendants("Percent"))
				if (int.TryParse(n.Value, out var value))
					Percent = value;
			foreach (var n in xDoc.Descendants("MaxDegreeOfParallelism"))
				if (int.TryParse(n.Value, out var value))
					MaxDegreeOfParallelism = value;
			foreach (var n in xDoc.Descendants("Thumbnails"))
				if (int.TryParse(n.Value, out var value))
					Thumbnails = value;
			foreach (var n in xDoc.Descendants("IncludeSubDirectories"))
				if (bool.TryParse(n.Value, out var value))
					IncludeSubDirectories = value;
			foreach (var n in xDoc.Descendants("IncludeImages"))
				if (bool.TryParse(n.Value, out var value))
					IncludeImages = value;
			foreach (var n in xDoc.Descendants("IgnoreReadOnlyFolders"))
				if (bool.TryParse(n.Value, out var value))
					IgnoreReadOnlyFolders = value;
			//09.03.21: UseCuda is obsolete and has been replaced with UseHardwareAcceleration.
			foreach (var n in xDoc.Descendants("UseCuda"))
				if (bool.TryParse(n.Value, out var value))
					HardwareAccelerationMode = value ? Core.FFTools.FFHardwareAccelerationMode.auto : Core.FFTools.FFHardwareAccelerationMode.none;
			foreach (var n in xDoc.Descendants("HardwareAccelerationMode"))
				if (Enum.TryParse<Core.FFTools.FFHardwareAccelerationMode>(n.Value, out var value))
					HardwareAccelerationMode = value;
			foreach (var n in xDoc.Descendants("GeneratePreviewThumbnails"))
				if (bool.TryParse(n.Value, out var value))
					GeneratePreviewThumbnails = value;
			foreach (var n in xDoc.Descendants("IgnoreHardlinks"))
				if (bool.TryParse(n.Value, out var value))
					IgnoreHardlinks = value;
			foreach (var n in xDoc.Descendants("ExtendedFFToolsLogging"))
				if (bool.TryParse(n.Value, out var value))
					ExtendedFFToolsLogging = value;
			foreach (var n in xDoc.Descendants("CustomFFArguments"))
				CustomFFArguments = n.Value;
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

		void Scanner_Progress(object sender, ScanProgressChangedEventArgs e) =>
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

		void Scanner_ScanDone(object sender, EventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				IsScanning = false;
				IsBusy = false;

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

				if (GeneratePreviewThumbnails)
					Scanner.RetrieveThumbnails();

				view = new DataGridCollectionView(Duplicates);
				view.GroupDescriptions.Add(new DataGridPathGroupDescription($"{nameof(DuplicateItemVM.ItemInfo)}.{nameof(DuplicateItem.GroupId)}"));
				view.Filter += TextFilter;
				GetDataGrid.Items = view;

				TotalDuplicates = Duplicates.Count;
				TotalDuplicatesSize = Duplicates.Sum(x => x.ItemInfo.SizeLong).BytesToString();
				TotalSizeRemovedInternal = 0;
				TotalDuplicateGroups = Duplicates.GroupBy(x => x.ItemInfo.GroupId).Count();
			});
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
			if (!Includes.Contains(result))
				Includes.Add(result);
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

			string result = await new SaveFileDialog {
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

			List<FileDialogFilter> filterList = new(1);
			filterList.Add(new FileDialogFilter {
				Name = "Json Files",
				Extensions = new List<string>() { "json" }
			});

			string result = await new SaveFileDialog {
				DefaultExtension = ".json",
				Filters = filterList
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;


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

		public static ReactiveCommand<DuplicateItemVM, Unit> OpenItemCommand => ReactiveCommand.Create<DuplicateItemVM>(currentItem => {
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
		});
		public static ReactiveCommand<Unit, Unit> OpenSelectedItemInFolderCommand => ReactiveCommand.Create(() => {
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
		});
		public static ReactiveCommand<Unit, Unit> OpenItemInFolderCommand => ReactiveCommand.Create(() => {
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
		});
		public static ReactiveCommand<Unit, Unit> RenameFileCommand => ReactiveCommand.CreateFromTask(async () => {
			if (GetDataGrid.SelectedItem is not DuplicateItemVM currentItem) return;
			var fi = new FileInfo(currentItem.ItemInfo.Path);
			Debug.Assert(fi.Directory != null, "fi.Directory != null");
			string newName = await InputBoxService.Show("Enter new name", fi.Name, title: "Rename File");
			if (string.IsNullOrEmpty(newName)) return;
			newName = FileUtils.SafePathCombine(fi.DirectoryName, newName);
			fi.MoveTo(newName);
			currentItem.ItemInfo.Path = newName;
		});

		public static ReactiveCommand<Unit, Unit> ToggleCheckboxCommand => ReactiveCommand.Create(() => {
			if (GetDataGrid.SelectedItem is not DuplicateItemVM currentItem) return;
			currentItem.Checked = !currentItem.Checked;
		});

		public ReactiveCommand<ListBox, Action> RemoveIncludesFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems.Count > 0)
				Includes.Remove((string)lbox.SelectedItems[0]);
			return null;
		});
		public ReactiveCommand<Unit, Unit> AddBlacklistToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = "Select folder"
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			if (!Blacklists.Contains(result))
				Blacklists.Add(result);
		});
		public ReactiveCommand<ListBox, Action> RemoveBlacklistFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems.Count > 0)
				Blacklists.Remove((string)lbox.SelectedItems[0]);
			return null;
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


		public ReactiveCommand<Unit, Unit> StartScanCommand => ReactiveCommand.CreateFromTask(async () => {
			if (!ScanEngine.FFmpegExists) {
				await MessageBoxService.Show("Cannot find FFmpeg. Please follow instructions on Github and restart VDF");
				return;
			}
			if (!ScanEngine.FFprobeExists) {
				await MessageBoxService.Show("Cannot find FFprobe. Please follow instructions on Github and restart VDF");
				return;
			}

			Duplicates.Clear();
			try {
				foreach (var f in new DirectoryInfo(Utils.ImageUtils.ThumbnailDirectory).EnumerateFiles())
					f.Delete();
			}
			catch (Exception e) {
				Logger.Instance.Info(e.Message);
				return;
			}
			IsScanning = true;
			SaveSettings();
			//Set scan settings
			Scanner.Settings.IncludeSubDirectories = IncludeSubDirectories;
			Scanner.Settings.IncludeImages = IncludeImages;
			Scanner.Settings.GeneratePreviewThumbnails = GeneratePreviewThumbnails;
			Scanner.Settings.IgnoreReadOnlyFolders = IgnoreReadOnlyFolders;
			Scanner.Settings.IgnoreHardlinks = IgnoreHardlinks;
			Scanner.Settings.HardwareAccelerationMode = HardwareAccelerationMode;
			Scanner.Settings.Percent = Percent;
			Scanner.Settings.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
			Scanner.Settings.ThumbnailCount = Thumbnails;
			Scanner.Settings.ExtendedFFToolsLogging = ExtendedFFToolsLogging;
			Scanner.Settings.CustomFFArguments = CustomFFArguments;
			Scanner.Settings.IncludeList.Clear();
			foreach (var s in Includes)
				Scanner.Settings.IncludeList.Add(s);
			Scanner.Settings.BlackList.Clear();
			foreach (var s in Blacklists)
				Scanner.Settings.BlackList.Add(s);
			//Start scan
			IsBusy = true;
			IsBusyText = "Enumerating files...";
			Scanner.StartSearch();
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
			var dlg = new ExpressionBuilder();
			var res = await dlg.ShowDialog<bool>(ApplicationHelpers.MainWindow);
			if (!res) return;

			string currentExpression =
							((ExpressionBuilder.ExpressionBuilderVM)dlg.DataContext).ExpressionText;

			var blackListGroupID = new HashSet<Guid>();
			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already

				IEnumerable<DuplicateItemVM> l = Duplicates;
				try {
					var interpreter = new Interpreter().SetVariable("currentDuplicate", first).
						ParseAsDelegate<Func<DuplicateItemVM, bool>>(currentExpression + " && !arg.ItemInfo.Path.Equals(currentDuplicate.ItemInfo.Path)");
					l = l.Where(interpreter);
				}
				catch (ParseException e) {
					await MessageBoxService.Show($"Failed to parse '{currentExpression}': {e}");
					return;
				}

				var dupMods = l as DuplicateItemVM[] ?? l.ToArray();
				if (!dupMods.Any()) continue;
				foreach (var dup in dupMods)
					dup.Checked = true;
				first.Checked = false;
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
		public ReactiveCommand<Unit, Unit> MarkGroupAsNotAMatchCommand => ReactiveCommand.CreateFromTask(async () => {
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
		public ReactiveCommand<Unit, Unit> ClearSelectionCommand => ReactiveCommand.Create(() => {
			for (var i = 0; i < Duplicates.Count; i++)
				Duplicates[i].Checked = false;
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
			var dlgResult = await MessageBoxService.Show(
				fromDisk
					? $"Are you sure you want to{(CoreUtils.IsWindows ? " move" : " permanently delete")} the selected files{(CoreUtils.IsWindows ? " to recycle bin (only if supported, i.e. network files will be deleted instead)" : " from disk")}?"
					: $"Are you sure to delete selected from list (keep files){(blackList ? " and blacklist them" : string.Empty)}?",
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult == MessageBoxButtons.No) return;

			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				var dub = Duplicates[i];
				if (dub.Checked == false) continue;
				if (fromDisk)
					try {

						if (createSymbolLinksInstead) {
							DuplicateItemVM fileToKeep = Duplicates.FirstOrDefault(s =>
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
			if (blackList)
				ScanEngine.SaveDatabase();
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
			Utils.FileUtils.CopyFile(Duplicates.Where(s => s.Checked), result, true, true,
				out var errorCounter);
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
