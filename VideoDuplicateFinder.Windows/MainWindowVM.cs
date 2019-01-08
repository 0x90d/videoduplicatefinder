using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Shell;
using System.Xml.Linq;
using DuplicateFinderEngine;
using VideoDuplicateFinderWindows.Data;
using VideoDuplicateFinderWindows.MVVM;

// ReSharper disable once CheckNamespace
namespace VideoDuplicateFinderWindows {
	class MainWindowVM : ViewModelBase {
		public MainWindow host;
		public ScanEngine Scanner { get; } = new ScanEngine();

		public FastObservableCollection<DuplicateItemViewModel> Duplicates { get; } =
			new FastObservableCollection<DuplicateItemViewModel>();

		public ObservableCollection<LogItem> LogItems { get; } = new ObservableCollection<LogItem>();
		public ObservableCollection<string> Includes { get; } = new ObservableCollection<string>();
		public ObservableCollection<string> Blacklists { get; } = new ObservableCollection<string>();
		private CollectionView view;

		int _TotalGroups;
		public int TotalGroups {
			get => _TotalGroups;
			set {
				if (value == _TotalGroups) return;
				_TotalGroups = value;
				OnPropertyChanged(nameof(TotalGroups));
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
			Scanner.Progress += Scanner_Progress;
			Scanner.ScanDone += Scanner_ScanDone;
			Scanner.FilesEnumerated += Scanner_FilesEnumerated;
			Logger.Instance.LogItemAdded += Instance_LogItemAdded;
		}

		private void Instance_LogItemAdded(object sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(new Action(() => {
			while (Logger.Instance.LogEntries.Count > 0) {
				if (Logger.Instance.LogEntries.TryTake(out var item))
					LogItems.Add(item);
			}
		}));

		private void Scanner_FilesEnumerated(object sender, EventArgs e) => IsBusy = false;

		private void Scanner_ScanDone(object sender, EventArgs e) {
			//Reset properties
			ScanProgressText = string.Empty;
			RemainingTime = new TimeSpan();
			ScanProgressValue = 0;
			ScanProgressMaxValue = 100;
			host.TreeViewDuplicates.ItemsSource = null;
			Duplicates.SuspendCollectionChangeNotification();
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

			Duplicates.ResumeCollectionChangeNotification();
			//We no longer need the core duplicates
			Scanner.Duplicates.Clear();
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
		private bool TextFilter(object obj) {
			if (!(obj is DuplicateItemViewModel data)) return false;
			var success = true;
			if (!string.IsNullOrEmpty(FilterByPath))
				success = data.Path.Contains(FilterByPath, StringComparison.OrdinalIgnoreCase);
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

		bool _IsBusy;
		public bool IsBusy {
			get => _IsBusy;
			set {
				if (value == _IsBusy) return;
				_IsBusy = value;
				OnPropertyChanged(nameof(IsBusy));
			}
		}

		string _IsBusyText;
		public string IsBusyText {
			get => _IsBusyText;
			set {
				if (value == _IsBusyText) return;
				_IsBusyText = value;
				OnPropertyChanged(nameof(IsBusyText));
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
		public bool IncludeSubDirectories {
			get => Scanner.Settings.IncludeSubDirectories;
			set {
				if (value == Scanner.Settings.IncludeSubDirectories) return;
				Scanner.Settings.IncludeSubDirectories = value;
				OnPropertyChanged(nameof(IncludeSubDirectories));
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
		public bool IgnoreHiddenFolders {
			get => Scanner.Settings.IgnoreHiddenFolders;
			set {
				if (value == Scanner.Settings.IgnoreHiddenFolders) return;
				Scanner.Settings.IgnoreHiddenFolders = value;
				OnPropertyChanged(nameof(IgnoreHiddenFolders));
			}
		}
		public bool IgnoreSystemFolders {
			get => Scanner.Settings.IgnoreSystemFolders;
			set {
				if (value == Scanner.Settings.IgnoreSystemFolders) return;
				Scanner.Settings.IgnoreSystemFolders = value;
				OnPropertyChanged(nameof(IgnoreSystemFolders));
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
		public DelegateCommand ClearLogCommand => new DelegateCommand(a => { LogItems.Clear(); }, a => LogItems.Count > 0);
		public DelegateCommand CopyLogCommand => new DelegateCommand(a => {
			var sb = new StringBuilder();
			foreach (var l in LogItems)
				sb.AppendLine(l.ToString());
			Clipboard.SetText(sb.ToString());
		}, a => LogItems.Count > 0);
		public DelegateCommand OpenInFolderCommand => new DelegateCommand(a => {
			try {
				Process.Start("explorer.exe", $"/select, \"{((DuplicateItemViewModel)host.TreeViewDuplicates.SelectedItem).Path}\"");
			}
			catch (Exception e) {
				MessageBox.Show(e.Message + Environment.NewLine + Environment.NewLine + e.StackTrace, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
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
		public DelegateCommand StartScanCommand => new DelegateCommand(a => {
			Duplicates.Clear();
			IsScanning = true;
			//Set scan settings
			Scanner.Settings.IncludeList.Clear();
			foreach (var s in Includes)
				Scanner.Settings.IncludeList.Add(s);
			Scanner.Settings.BlackList.Clear();
			foreach (var s in Blacklists)
				Scanner.Settings.BlackList.Add(s);
			IsBusy = true;
			IsBusyText = VideoDuplicateFinder.Windows.Properties.Resources.EnumeratingInputFolders;
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
				Duplicates.SuspendCollectionChangeNotification();
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
				Duplicates.ResumeCollectionChangeNotification();
			}, a => Duplicates.Count > 0);
		public DelegateCommand CheckWhenIdenticalButSizeCommand =>
			new DelegateCommand(a => {
				Duplicates.SuspendCollectionChangeNotification();
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
				Duplicates.ResumeCollectionChangeNotification();
			}, a => Duplicates.Count > 0);
		public DelegateCommand CheckLowestQualityCommand =>
			new DelegateCommand(a => {
				Duplicates.SuspendCollectionChangeNotification();
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
					for (int i = 1; i < dupMods.Count; i++) {
						if (dupMods[i].Duration.TrimMiliseconds() > keep.Duration.TrimMiliseconds())
							keep = dupMods[i];
					}
					//resolution next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path))
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].Fps > keep.Fps)
								keep = dupMods[i];
						}
					//fps next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path))
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].Fps > keep.Fps)
								keep = dupMods[i];
						}
					//Bitrate next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path))
						for (int i = 1; i < dupMods.Count; i++) {
							if (dupMods[i].BitRateKbs > keep.BitRateKbs)
								keep = dupMods[i];
						}
					//Audio Bitrate next, but only when keep is unchanged
					if (keep.Path.Equals(dupMods[0].Path))
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
				Duplicates.ResumeCollectionChangeNotification();
			}, a => Duplicates.Count > 0);
		public DelegateCommand ClearSelectionCommand => new DelegateCommand(a => {
			Duplicates.SuspendCollectionChangeNotification();
			for (var i = 0; i < Duplicates.Count; i++)
				Duplicates[i].Checked = false;
			Duplicates.ResumeCollectionChangeNotification();
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
				MessageBox.Show(e.Message + Environment.NewLine + Environment.NewLine + e.StackTrace, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}, a => Duplicates.Count > 0);
		public DelegateCommand RemoveSelectionFromListCommand => new DelegateCommand(a => { InternalDelete(false); }, a => Duplicates.Count > 0);

		public DelegateCommand CopySelectionCommand => new DelegateCommand(a => {
			var ofd = new System.Windows.Forms.FolderBrowserDialog();
			if (ofd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
			CopyDuplicates(ofd.SelectedPath);
		}, a => Duplicates.Count > 0);

		async void CopyDuplicates(string targetFolder) {
			IsBusyText = VideoDuplicateFinder.Windows.Properties.Resources.CopyingFiles;
			IsBusy = true;
			var t =  new System.Threading.Tasks.Task<int>(() => {
				FileHelper.CopyFile(Duplicates.Where(s => s.Checked).Select(s => s.Path), targetFolder, true,
					out int errors);
				return errors;
			});
			t.Start();
			var errorCounter = await t;
			IsBusy = false;
			if (errorCounter > 0)
				MessageBox.Show(VideoDuplicateFinder.Windows.Properties.Resources.FailedToCopySomeFilesPleaseCheckLog, 
					VideoDuplicateFinder.Windows.Properties.Resources.Warning, MessageBoxButton.OK,
					MessageBoxImage.Warning);
		}
		private void InternalDelete(bool alsofromDisk) {
			if (Scanner == null || Duplicates.Count == 0) return;
			if (alsofromDisk) {
				var result = MessageBox.Show(VideoDuplicateFinder.Windows.Properties.Resources.DeleteAllCheckedItemsFromDISKToRecycleBinIfPossible,
					VideoDuplicateFinder.Windows.Properties.Resources.Confirmation, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No;
				if (result)
					return;
			}

			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				var dub = Duplicates[i];
				if (dub.Checked == false) continue;
				if (alsofromDisk) {
					if (!FileOperationAPIWrapper.MoveToRecycleBin(dub.Path))
						return;
					TotalSizeRemoved += dub.SizeLong;
				}

				dub.Checked = false;
				Duplicates.RemoveAt(i);
			}
			//Hide groups with just one item left
			for (var i = Duplicates.Count - 1; i >= 0; i--) {
				var first = Duplicates[i];
				if (Duplicates.Any(s => s.GroupId == first.GroupId && s.Path != first.Path)) continue;
				first.Checked = false;
				Duplicates.RemoveAt(i);
			}

		}

		public void SaveSettings() {
			var path = DuplicateFinderEngine.Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Settings.xml");
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
				new XElement("IncludeSubDirectories", IncludeSubDirectories),
				new XElement("IgnoreReadOnlyFolders", IgnoreReadOnlyFolders)
				)
			);
			xDoc.Save(path);
		}
		public void LoadSettings() {
			var path = DuplicateFinderEngine.Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Settings.xml");
			if (!File.Exists(path)) return;
			Includes.Clear();
			Blacklists.Clear();
			var xDoc = XDocument.Load(path);
			foreach (var n in xDoc.Descendants("Include"))
				Includes.Add(n.Value);
			foreach (var n in xDoc.Descendants("Exclude"))
				Blacklists.Add(n.Value);
			var node = xDoc.Descendants("IncludeSubDirectories").SingleOrDefault();
			if (node?.Value != null)
				IncludeSubDirectories = bool.Parse(node.Value);
			node = xDoc.Descendants("IgnoreReadOnlyFolders").SingleOrDefault();
			if (node?.Value != null)
				IgnoreReadOnlyFolders = bool.Parse(node.Value);
		}

	}
}
