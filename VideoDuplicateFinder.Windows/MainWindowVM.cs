using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Shell;
using System.Xml.Linq;
using DuplicateFinderEngine;
using MahApps.Metro.Controls.Dialogs;
using VideoDuplicateFinder.Windows.Data;
using VideoDuplicateFinderWindows.Data;
using VideoDuplicateFinderWindows.MVVM;

// ReSharper disable once CheckNamespace
namespace VideoDuplicateFinderWindows {
	class MainWindowVM : ViewModelBase {
		public MainWindow host;
		public ScanEngine Scanner { get; } = new ScanEngine();

		public ObservableCollection<DuplicateItemViewModel> Duplicates { get; } =
			new ObservableCollection<DuplicateItemViewModel>();

		public ObservableCollection<LogItem> LogItems { get; } = new ObservableCollection<LogItem>();
		public ObservableCollection<string> Includes { get; } = new ObservableCollection<string>();
		public ObservableCollection<string> Blacklists { get; } = new ObservableCollection<string>();
		private CollectionView view;
		public KeyValuePair<string, SortDescription>[] SortOrders { get; } = {
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_None, new SortDescription()),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_SizeAscending, new SortDescription(nameof(DuplicateItemViewModel.SizeLong), ListSortDirection.Ascending)),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_SizeDescending, new SortDescription(nameof(DuplicateItemViewModel.SizeLong), ListSortDirection.Descending)),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_ResolutionAscending, new SortDescription(nameof(DuplicateItemViewModel.FrameSizeInt), ListSortDirection.Ascending)),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_ResolutionDescending, new SortDescription(nameof(DuplicateItemViewModel.FrameSizeInt), ListSortDirection.Descending)),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_DurationAscending, new SortDescription(nameof(DuplicateItemViewModel.Duration), ListSortDirection.Ascending)),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_DurationDescending, new SortDescription(nameof(DuplicateItemViewModel.Duration), ListSortDirection.Descending)),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_DateCreatedAscending, new SortDescription(nameof(DuplicateItemViewModel.DateCreated), ListSortDirection.Ascending)),
			new KeyValuePair<string, SortDescription>(VideoDuplicateFinder.Windows.Properties.Resources.Sort_DateCreatedDescending, new SortDescription(nameof(DuplicateItemViewModel.DateCreated), ListSortDirection.Descending)),
		};
		public KeyValuePair<string, FileTypeFilter>[] TypeFilters { get; } = {
			new KeyValuePair<string, FileTypeFilter>(VideoDuplicateFinder.Windows.Properties.Resources.FileTypeFilter_All,  FileTypeFilter.All),
			new KeyValuePair<string, FileTypeFilter>(VideoDuplicateFinder.Windows.Properties.Resources.FileTypeFilter_Videos,  FileTypeFilter.Videos),
			new KeyValuePair<string, FileTypeFilter>(VideoDuplicateFinder.Windows.Properties.Resources.FileTypeFilter_Images,  FileTypeFilter.Images),
		};

		int _TotalGroups;
		public int TotalGroups {
			get => _TotalGroups;
			set {
				if (value == _TotalGroups) return;
				_TotalGroups = value;
				OnPropertyChanged(nameof(TotalGroups));
			}
		}

		SortDescription _SortOrder;
		public SortDescription SortOrder {
			get => _SortOrder;
			set {
				if (value == _SortOrder) return;
				_SortOrder = value;
				view.SortDescriptions.Clear();
				if (!string.IsNullOrEmpty(_SortOrder.PropertyName))
					view.SortDescriptions.Add(_SortOrder);
				OnPropertyChanged(nameof(SortOrder));
			}
		}

		FileTypeFilter _FileType;

		public FileTypeFilter FileType {
			get => _FileType;
			set {
				if (value == _FileType) return;
				_FileType = value;
				OnPropertyChanged(nameof(FileType));
				view.Refresh();
			}
		}

		int _TotalDuplicates;
		public int TotalDuplicates {
			get => _TotalDuplicates;
			set {
				if (value == _TotalDuplicates) return;
				_TotalDuplicates = value;
				OnPropertyChanged(nameof(TotalDuplicates));
			}
		}

		long _TotalSize;
		public long TotalSize {
			get => _TotalSize;
			set {
				if (value == _TotalSize) return;
				_TotalSize = value;
				OnPropertyChanged(nameof(TotalSize));
			}
		}

		long _TotalSizeRemoved;
		public long TotalSizeRemoved {
			get => _TotalSizeRemoved;
			set {
				if (value == _TotalSizeRemoved) return;
				_TotalSizeRemoved = value;
				OnPropertyChanged(nameof(TotalSizeRemoved));
			}
		}

		string _FilterByPath;

		public string FilterByPath {
			get => _FilterByPath;
			set {
				if (value == FilterByPath) return;
				_FilterByPath = value;
				OnPropertyChanged(nameof(FilterByPath));
				view.Refresh();
			}
		}
		public MainWindowVM() {
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate.
			Scanner.Progress += Scanner_Progress;
			Scanner.ScanDone += Scanner_ScanDone;
			Scanner.ThumbnailsPopulated += Scanner_ThumbnailsPopulated;
			Scanner.FilesEnumerated += Scanner_FilesEnumerated;
			Scanner.DatabaseCleaned += Scanner_DatabaseCleaned;
			Scanner.DatabaseVideosExportedToCSV += Scanner_DatabaseVideosExportedToCSV;
			Logger.Instance.LogItemAdded += Instance_LogItemAdded;
#pragma warning restore CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate.
			//Ensure items added before GUI was ready will be shown 
			Instance_LogItemAdded(this, new EventArgs());
		}

		private void Scanner_DatabaseCleaned(object sender, EventArgs e) => CloseMessage();

		private void Scanner_DatabaseVideosExportedToCSV(object sender, EventArgs e) => CloseMessage();

		private void Instance_LogItemAdded(object sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(new Action(() => {
			while (Logger.Instance.LogEntries.Count > 0) {
				if (Logger.Instance.LogEntries.TryTake(out var item))
					LogItems.Add(item);
			}
		}));

		private void Scanner_FilesEnumerated(object sender, EventArgs e) => CloseMessage();

		private void Scanner_ScanDone(object sender, EventArgs e) {
			Scanner.PopulateDuplicateThumbnails();

			//Status bar information
			TotalGroups = Scanner.Duplicates.GroupBy(a => a.GroupId).Count();
			TotalSize = Scanner.Duplicates.Sum(a => a.SizeLong);
			TotalDuplicates = Scanner.Duplicates.Count;
			//Adding duplicates to view
			foreach (var itm in Scanner.Duplicates) {
				var dup = new DuplicateItemViewModel(itm);
				//Set best property in duplicate group
				var others = Scanner.Duplicates.Where(a => a.GroupId == dup.GroupId && a.Path != dup.Path).ToList();
				dup.SizeBest = !others.Any(a => a.SizeLong < dup.SizeLong);
				dup.FrameSizeBest = !others.Any(a => a.FrameSizeInt > dup.FrameSizeInt);
				dup.DurationBest = !others.Any(a => a.Duration.TrimMiliseconds() > dup.Duration.TrimMiliseconds());
				dup.BitrateBest = !others.Any(a => a.BitRateKbs > dup.BitRateKbs);
				Duplicates.Add(dup);
			}
			//Group results by GroupID
			view = (CollectionView)CollectionViewSource.GetDefaultView(Duplicates);
			var groupDescription = new PropertyGroupDescription(nameof(DuplicateItemViewModel.GroupId));
			view.GroupDescriptions.Clear();
			view.GroupDescriptions.Add(groupDescription);
			view.Filter += TextFilter;
			//And done

			host.TreeViewDuplicates.ItemsSource = view;
			IsScanning = false;
		}

		private void Scanner_ThumbnailsPopulated(object sender, EventArgs e) {
			//Reset properties
			ScanProgressText = string.Empty;
			RemainingTime = new TimeSpan();
			ScanProgressValue = 0;
			ScanProgressMaxValue = 100;
		}

		private bool TextFilter(object obj) {
			if (!(obj is DuplicateItemViewModel data)) return false;
			var success = true;
			if (!string.IsNullOrEmpty(FilterByPath))
				success = data.Path.Contains(FilterByPath, StringComparison.OrdinalIgnoreCase);
			if (success && FileType != FileTypeFilter.All)
				success = FileType == FileTypeFilter.Images ? data.IsImage : !data.IsImage;
			return success;
		}

		private void Scanner_Progress(object sender, ScanEngine.OwnScanProgress e) => Application.Current.Dispatcher.BeginInvoke(new Action(() => {
			ScanProgressText = e.CurrentFile;
			RemainingTime = e.Remaining;
			ScanProgressValue = e.CurrentPosition;
			TimeElapsed = e.Elapsed;
			ScanProgressMaxValue = Scanner.ScanProgressMaxValue;
			host.TaskbarItemInfo.ProgressValue = e.CurrentPosition / (double)Scanner.ScanProgressMaxValue;
		}));
		private string __scanProgressText;
		public string ScanProgressText {
			get => __scanProgressText;
			set {
				if (value == null || value == __scanProgressText) return;
				__scanProgressText = value;
				OnPropertyChanged(nameof(ScanProgressText));
			}
		}

		private int _scanProgressMaxValue = 100;
		public int ScanProgressMaxValue {
			get => _scanProgressMaxValue;
			set {
				if (value == _scanProgressMaxValue) return;
				_scanProgressMaxValue = value;
				OnPropertyChanged(nameof(ScanProgressMaxValue));
			}
		}

		private int _scanProgressValue;
		public int ScanProgressValue {
			get => _scanProgressValue;
			set {
				if (value == _scanProgressValue) return;
				_scanProgressValue = value;
				OnPropertyChanged(nameof(ScanProgressValue));
			}
		}
		private TimeSpan _TimeElapsed;
		public TimeSpan TimeElapsed {
			get => _TimeElapsed;
			set {
				if (value == _TimeElapsed) return;
				_TimeElapsed = value;
				OnPropertyChanged(nameof(TimeElapsed));
			}
		}


		private TimeSpan _RemainingTime;
		public TimeSpan RemainingTime {
			get => _RemainingTime;
			set {
				if (value == _RemainingTime) return;
				_RemainingTime = value;
				OnPropertyChanged(nameof(RemainingTime));
			}
		}
		bool _IsScanning;
		public bool IsScanning {
			get => _IsScanning;
			set {
				if (value == _IsScanning) return;
				_IsScanning = value;
				host.TaskbarItemInfo.ProgressState = value ? TaskbarItemProgressState.Normal : TaskbarItemProgressState.None;
				if (!value)
					host.TaskbarItemInfo.ProgressValue = 0d;
				OnPropertyChanged(nameof(IsScanning));
			}
		}

		bool _IsPaused;
		public bool IsPaused {
			get => _IsPaused;
			set {
				if (value == _IsPaused) return;
				_IsPaused = value;
				host.TaskbarItemInfo.ProgressState = value ? TaskbarItemProgressState.Paused : (_IsScanning ? TaskbarItemProgressState.Normal : TaskbarItemProgressState.None);
				OnPropertyChanged(nameof(IsPaused));
			}
		}
		public float Percent {
			get => Scanner.Settings.Percent;
			set {
				if (value == Scanner.Settings.Percent) return;
				Scanner.Settings.Percent = value;
				OnPropertyChanged(nameof(Percent));
			}
		}

		public int Thumbnails {
			get => Scanner.Settings.ThumbnailCount;
			set {
				if (value == Scanner.Settings.ThumbnailCount) return;
				Scanner.Settings.ThumbnailCount = value;
				OnPropertyChanged(nameof(Thumbnails));
			}

		}
		public bool IncludeSubDirectories {
			get => Scanner.Settings.IncludeSubDirectories;
			set {
				if (value == Scanner.Settings.IncludeSubDirectories) return;
				Scanner.Settings.IncludeSubDirectories = value;
				OnPropertyChanged(nameof(IncludeSubDirectories));
			}
		}
		public bool IncludeImages {
			get => Scanner.Settings.IncludeImages;
			set {
				if (value == Scanner.Settings.IncludeImages) return;
				Scanner.Settings.IncludeImages = value;
				OnPropertyChanged(nameof(IncludeImages));
			}
		}
		public bool IgnoreReadOnlyFolders {
			get => Scanner.Settings.IgnoreReadOnlyFolders;
			set {
				if (value == Scanner.Settings.IgnoreReadOnlyFolders) return;
				Scanner.Settings.IgnoreReadOnlyFolders = value;
				OnPropertyChanged(nameof(IgnoreReadOnlyFolders));
			}
		}
		public byte Threshhold {
			get => Scanner.Settings.Threshhold;
			set {
				if (value == Scanner.Settings.Threshhold) return;
				Scanner.Settings.Threshhold = value;
				OnPropertyChanged(nameof(Threshhold));
			}
		}
		public int ThumbnailCount {
			get => Scanner.Settings.ThumbnailCount;
			set {
				if (value == Scanner.Settings.ThumbnailCount) return;
				Scanner.Settings.ThumbnailCount = value;
				OnPropertyChanged(nameof(ThumbnailCount));
			}
		}

		ProgressDialogController dialogController;
		readonly MetroDialogSettings mySettings = new MetroDialogSettings() {
			AnimateShow = false,
			AnimateHide = false
		};
		async void ShowMessage(string message, string title, MessageDialogStyle style) => await host.ShowMessageAsync(title, message, style, settings: mySettings);
		async Task<bool> ShowProgressMessage(string busyText) {
			dialogController = await host.ShowProgressAsync(VideoDuplicateFinder.Windows.Properties.Resources.PleaseWait, busyText, settings: mySettings);
			dialogController.SetIndeterminate();
			return true;
		}
		void CloseMessage() {
			if (dialogController?.IsOpen == true) {
				dialogController.CloseAsync();
			}
		}


		public DelegateCommand AddIncludesToListCommand => new DelegateCommand(a => {
			var ofd = new System.Windows.Forms.FolderBrowserDialog();
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
			if (!Blacklists.Contains(ofd.SelectedPath))
				Includes.Add(ofd.SelectedPath);

		});
		public DelegateCommand AddBlacklistToListCommand => new DelegateCommand(a => {
			var ofd = new System.Windows.Forms.FolderBrowserDialog();
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
			if (!Blacklists.Contains(ofd.SelectedPath))
				Blacklists.Add(ofd.SelectedPath);

		});

		public DelegateCommand LatestReleaseCommand => new DelegateCommand(a => {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "https://github.com/0x90d/videoduplicatefinder/releases",
					UseShellExecute = true
				});
			}
			catch {
			}
		});
		public DelegateCommand CleanDatabaseCommand => new DelegateCommand(async a => {
			await ShowProgressMessage(VideoDuplicateFinder.Windows.Properties.Resources.CleaningUp);
			Scanner.CleanupDatabase();
		});
		public DelegateCommand ExportDatabaseVideosToCSVCommand => new DelegateCommand(async a => {
			await ShowProgressMessage(VideoDuplicateFinder.Windows.Properties.Resources.ExportingDatabaseVideosToCSV);
			Scanner.ExportDatabaseVideosToCSV(true, false);
		});
		public DelegateCommand ExportDatabaseExcluded => new DelegateCommand(async a => {
			await ShowProgressMessage(VideoDuplicateFinder.Windows.Properties.Resources.ExportingDatabaseVideosToCSV);
			Scanner.ExportDatabaseVideosToCSV(false, true);
		});
		public DelegateCommand ClearLogCommand => new DelegateCommand(a => { LogItems.Clear(); }, a => LogItems.Count > 0);
		public DelegateCommand CopyLogCommand => new DelegateCommand(a => {
			var sb = new StringBuilder();
			foreach (var l in LogItems)
				sb.AppendLine(l.ToString());
			Clipboard.SetText(sb.ToString());
		}, a => LogItems.Count > 0);
		public DelegateCommand OpenInFolderCommand => new DelegateCommand(a => {
			try {
				var procInfo = new ProcessStartInfo("explorer.exe", $"/select, \"{((DuplicateItemViewModel)host.TreeViewDuplicates.SelectedItem).Path}\"") {
					UseShellExecute = true
				};
				Process.Start(procInfo);
			}
			catch (Exception e) {
				ShowMessage(e.Message + Environment.NewLine + Environment.NewLine + e.StackTrace, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageDialogStyle.Affirmative);
			}
		}, a => host?.TreeViewDuplicates?.SelectedItem != null);
		public DelegateCommand RenameDuplicateFileCommand => new DelegateCommand(async a => {
			var selItem = (DuplicateItemViewModel)host.TreeViewDuplicates.SelectedItem;
			var dlg = await host.ShowInputAsync(VideoDuplicateFinder.Windows.Properties.Resources.RenameFile,
				VideoDuplicateFinder.Windows.Properties.Resources.FileName,
				new MetroDialogSettings {
					AnimateHide = false,
					AnimateShow = false,
					DefaultText = Path.GetFileNameWithoutExtension(selItem.Path)
			});
			if (string.IsNullOrEmpty(dlg)) return;
			try {
				var fi = new FileInfo(selItem.Path);
				Debug.Assert(fi.Directory != null, "fi.Directory != null");
				var newName = fi.Directory.FullName + "\\" + dlg + fi.Extension;
				if (File.Exists(newName)) {
					ShowMessage(string.Format(VideoDuplicateFinder.Windows.Properties.Resources.FileAlreadyExists, dlg), VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageDialogStyle.Affirmative);
					return;
				}
				fi.MoveTo(newName);
				selItem.ChangePath(newName);
			}
			catch (Exception e) {
				ShowMessage(e.Message + Environment.NewLine + Environment.NewLine + e.StackTrace, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageDialogStyle.Affirmative);
			}
		}, a => host?.TreeViewDuplicates?.SelectedItem != null);
		public DelegateCommand<System.Windows.Controls.ListBox> RemoveIncludesFromListCommand => new DelegateCommand<System.Windows.Controls.ListBox>(a => {
			while (a.SelectedItems.Count > 0)
				Includes.Remove((string)a.SelectedItems[0]);

		}, a => a?.SelectedItems.Count > 0);
		public DelegateCommand<System.Windows.Controls.ListBox> RemoveBlacklistFromListCommand => new DelegateCommand<System.Windows.Controls.ListBox>(a => {
			while (a.SelectedItems.Count > 0)
				Blacklists.Remove((string)a.SelectedItems[0]);
		}, a => a?.SelectedItems.Count > 0);
		public DelegateCommand StartScanCommand => new DelegateCommand(async a => {
			if (!DuplicateFinderEngine.Utils.FfFilesExist) {
				ShowMessage(
					VideoDuplicateFinder.Windows.Properties.Resources.FFmpegExeFFprobeExeIsMissing, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageDialogStyle.Affirmative);
				return;
			}
			Duplicates.Clear();
			IsScanning = true;
			//Set scan settings
			Scanner.Settings.IncludeList.Clear();
			foreach (var s in Includes)
				Scanner.Settings.IncludeList.Add(s);
			Scanner.Settings.BlackList.Clear();
			foreach (var s in Blacklists)
				Scanner.Settings.BlackList.Add(s);
			await ShowProgressMessage(VideoDuplicateFinder.Windows.Properties.Resources.EnumeratingInputFolders);
			//Start scan
			Scanner.StartSearch();
		}, a => !IsScanning && !IsPaused);
		public DelegateCommand PauseScanCommand => new DelegateCommand(a => {
			IsPaused = true;
			Scanner.Pause();
		}, a => IsScanning && !IsPaused);
		public DelegateCommand ResumeScanCommand => new DelegateCommand(a => {
			IsPaused = false;
			Scanner.Resume();
		}, a => IsPaused);
		public DelegateCommand StopScanCommand => new DelegateCommand(a => {
			IsScanning = false;
			Scanner.Stop();
			Scanner.Duplicates.Clear();
		}, a => IsScanning);

		public DelegateCommand DeleteSelectedCommand => new DelegateCommand(a => {
			InternalDelete(true);
		}, a => Duplicates.Count > 0);

		public DelegateCommand CheckWhenIdenticalCommand =>
			new DelegateCommand(a => {
				var blackListGroupID = new HashSet<Guid>();
				foreach (var first in Duplicates) {
					if (blackListGroupID.Contains(first.GroupId)) continue; //Dup has been handled already
					var l = Duplicates.Where(d => d.Equals(first) && !d.Path.Equals(first.Path));
					var dupMods = l as DuplicateItemViewModel[] ?? l.ToArray();
					if (!dupMods.Any()) continue;
					foreach (var dup in dupMods)
						dup.Checked = true;
					first.Checked = false;
					blackListGroupID.Add(first.GroupId);
				}
			}, a => Duplicates.Count > 0);
		public DelegateCommand CheckWhenIdenticalButSizeCommand =>
			new DelegateCommand(a => {
				var blackListGroupID = new HashSet<Guid>();
				foreach (var first in Duplicates) {
					if (blackListGroupID.Contains(first.GroupId)) continue; //Dup has been handled already
					var l = Duplicates.Where(d => d.EqualsButSize(first) && !d.Path.Equals(first.Path));
					var dupMods = l as List<DuplicateItemViewModel> ?? l.ToList();
					if (!dupMods.Any()) continue;
					dupMods.Add(first);
					dupMods = dupMods.OrderBy(s => s.SizeLong).ToList();
					dupMods[0].Checked = false;
					for (int i = 1; i < dupMods.Count; i++) {
						dupMods[i].Checked = true;
					}
					blackListGroupID.Add(first.GroupId);
				}
			}, a => Duplicates.Count > 0);
		public DelegateCommand CheckLowestQualityCommand =>
			new DelegateCommand(a => {
				var blackListGroupID = new HashSet<Guid>();
				foreach (var first in Duplicates) {
					if (blackListGroupID.Contains(first.GroupId)) continue; //Dup has been handled already
					var l = Duplicates.Where(d => d.EqualsButQuality(first) && !d.Path.Equals(first.Path));
					var dupMods = l as List<DuplicateItemViewModel> ?? l.ToList();
					if (!dupMods.Any()) continue;
					dupMods.Insert(0, first);

					var keep = dupMods[0];
					//TODO: Make this order become an option for the user
					//Duration first
					if (!keep.IsImage && keep.Duration != TimeSpan.Zero)
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].Duration.TrimMiliseconds() > keep.Duration.TrimMiliseconds())
								keep = dupMods[i];
						}
					//resolution next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path))
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].FrameSizeInt > keep.FrameSizeInt)
								keep = dupMods[i];
						}
					//fps next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path) && !keep.IsImage)
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].Fps > keep.Fps)
								keep = dupMods[i];
						}
					//Bitrate next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path) && !keep.IsImage)
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].BitRateKbs > keep.BitRateKbs)
								keep = dupMods[i];
						}
					//Audio Bitrate next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path) && !keep.IsImage)
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].AudioSampleRate > keep.AudioSampleRate)
								keep = dupMods[i];
						}

					keep.Checked = false;
					for (int i = 0; i < dupMods.Count; i++) {
						if (!keep.Path.Equals(dupMods[i].Path))
							dupMods[i].Checked = true;
					}

					blackListGroupID.Add(first.GroupId);
				}
			}, a => Duplicates.Count > 0);
		public DelegateCommand ClearSelectionCommand => new DelegateCommand(a => {
			for (var i = 0; i < Duplicates.Count; i++)
				Duplicates[i].Checked = false;
		}, a => Duplicates.Count > 0);
		public DelegateCommand ExpandAllGroupsCommand => new DelegateCommand(a => {
			host.TreeViewDuplicates.ToggleExpander(true);
		}, a => Duplicates.Count > 0);
		public DelegateCommand CollapseAllGroupsCommand => new DelegateCommand(a => {
			host.TreeViewDuplicates.ToggleExpander(false);
		}, a => Duplicates.Count > 0);
		public DelegateCommand SaveToHtmlCommand => new DelegateCommand(a => {
			var ofd = new System.Windows.Forms.SaveFileDialog { Filter = "Html|*.html" };
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
			try {
				Duplicates.ToHtmlTable(ofd.FileName);
			}
			catch (Exception e) {
				ShowMessage(e.Message + Environment.NewLine + Environment.NewLine + e.StackTrace, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageDialogStyle.Affirmative);
			}
		}, a => Duplicates.Count > 0);
		public DelegateCommand RemoveSelectionFromListCommand => new DelegateCommand(a => { InternalDelete(false); }, a => Duplicates.Count > 0);

		public DelegateCommand CopySelectionCommand => new DelegateCommand(a => {
			var ofd = new System.Windows.Forms.FolderBrowserDialog();
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
			CopyDuplicates(ofd.SelectedPath, false);
		}, a => Duplicates.Count > 0);
		public DelegateCommand MoveSelectionCommand => new DelegateCommand(a => {
			var ofd = new System.Windows.Forms.FolderBrowserDialog();
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
			CopyDuplicates(ofd.SelectedPath, true);
		}, a => Duplicates.Count > 0);

		async void CopyDuplicates(string targetFolder, bool move) {
			await ShowProgressMessage(VideoDuplicateFinder.Windows.Properties.Resources.CopyingFiles);
			var t = new System.Threading.Tasks.Task<int>(() => {
				FileHelper.CopyFile(Duplicates.Where(s => s.Checked).Select(s => s.Path), targetFolder, true, move,
					out int errors);
				return errors;
			});
			t.Start();
			var errorCounter = await t;
			await dialogController.CloseAsync();
			if (errorCounter > 0)
				ShowMessage(VideoDuplicateFinder.Windows.Properties.Resources.FailedToCopySomeFilesPleaseCheckLog,
					VideoDuplicateFinder.Windows.Properties.Resources.Warning, MessageDialogStyle.Affirmative);
		}
		private async void InternalDelete(bool alsofromDisk) {
			if (Scanner == null || Duplicates.Count == 0) return;
			//Let user confirm
			if (alsofromDisk) {
				var result = await host.ShowMessageAsync(VideoDuplicateFinder.Windows.Properties.Resources.DeleteAllCheckedItemsFromDISKToRecycleBinIfPossible,
					VideoDuplicateFinder.Windows.Properties.Resources.Confirmation, MessageDialogStyle.AffirmativeAndNegative, settings: mySettings);
				if (result == MessageDialogResult.Negative)
					return;
			}

			//Remove duplicates
			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				var dub = Duplicates[i];
				if (dub.Checked == false) continue;
				if (alsofromDisk) {
					if (!FileOperationAPIWrapper.MoveToRecycleBin(dub.Path))
						continue;
					TotalSizeRemoved += dub.SizeLong;
				}
				Duplicates.Remove(dub);
			}
			//Hide groups with just one item left
			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				var first = Duplicates[i];
				if (Duplicates.Any(s => s.GroupId == first.GroupId && s.Path != first.Path)) continue;
				Duplicates.RemoveAt(i);
			}

		}

		public void SaveSettings() {
#pragma warning disable CS8604 // Possible null reference argument.
			var path = DuplicateFinderEngine.Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Settings.xml");
#pragma warning restore CS8604 // Possible null reference argument.
			var includes = new object[Includes.Count];
			//Include directories
			for (var i = 0; i < Includes.Count; i++) {
				includes[i] = new XElement("Include", Includes[i]);
			}
			//Exclude directories
			var excludes = new object[Blacklists.Count];
			for (var i = 0; i < Blacklists.Count; i++) {
				excludes[i] = new XElement("Exclude", Blacklists[i]);
			}

			var xDoc = new XDocument(new XElement("Settings",
				new XElement("Includes", includes),
				new XElement("Excludes", excludes),
				new XElement("Percent", Percent.ToString(CultureInfo.InvariantCulture)),
				new XElement("Thumbnails", Thumbnails),
				new XElement("IncludeSubDirectories", IncludeSubDirectories),
				new XElement("IncludeImages", IncludeImages),
				new XElement("IgnoreReadOnlyFolders", IgnoreReadOnlyFolders)
				)
			);
			xDoc.Save(path);
		}
		public void LoadSettings() {
#pragma warning disable CS8604 // Possible null reference argument.
			var path = DuplicateFinderEngine.Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Settings.xml");
#pragma warning restore CS8604 // Possible null reference argument.
			if (!File.Exists(path)) return;
			Includes.Clear();
			Blacklists.Clear();
			var xDoc = XDocument.Load(path);
			foreach (var n in xDoc.Descendants("Include"))
				Includes.Add(n.Value);
			foreach (var n in xDoc.Descendants("Exclude"))
				Blacklists.Add(n.Value);
			foreach (var n in xDoc.Descendants("Percent"))
				Percent = float.Parse(n.Value);
			foreach (var n in xDoc.Descendants("Thumbnails"))
				Thumbnails = int.Parse(n.Value);
			var node = xDoc.Descendants("IncludeSubDirectories").SingleOrDefault();
			if (node?.Value != null)
				IncludeSubDirectories = bool.Parse(node.Value);
			node = xDoc.Descendants("IncludeImages").SingleOrDefault();
			if (node?.Value != null)
				IncludeImages = bool.Parse(node.Value);
			node = xDoc.Descendants("IgnoreReadOnlyFolders").SingleOrDefault();
			if (node?.Value != null)
				IgnoreReadOnlyFolders = bool.Parse(node.Value);
		}

	}
}
