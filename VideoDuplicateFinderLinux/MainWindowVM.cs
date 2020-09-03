using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DuplicateFinderEngine;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Avalonia.Media;
using DynamicData;
using DynamicData.Binding;
using System.Reactive;
using System.Diagnostics;

namespace VideoDuplicateFinderLinux {
	public sealed class MainWindowVM : ReactiveObject {

		public ScanEngine Scanner { get; } = new ScanEngine();
		public ObservableCollection<LogItem> LogItems { get; } = new ObservableCollection<LogItem>();
		public ObservableCollection<string> Includes { get; } = new ObservableCollection<string>();
		public ObservableCollection<string> Blacklists { get; } = new ObservableCollection<string>();

		readonly SourceList<DuplicateItemViewModel> duplicateList =
			new SourceList<DuplicateItemViewModel>();

		ReadOnlyObservableCollection<DuplicateItemViewModel> duplicates;

		public ReadOnlyObservableCollection<DuplicateItemViewModel> Duplicates {
			get => duplicates;
			set => this.RaiseAndSetIfChanged(ref duplicates, value);
		}

		string _SearchText;
		public string SearchText {
			get => _SearchText;
			set => this.RaiseAndSetIfChanged(ref _SearchText, value);
		}
		bool _IsScanning;
		public bool IsScanning {
			get => _IsScanning;
			set => this.RaiseAndSetIfChanged(ref _IsScanning, value);
		}

		bool _IgnoreReadOnlyFolders;
		public bool IgnoreReadOnlyFolders {
			get => _IgnoreReadOnlyFolders;
			set => this.RaiseAndSetIfChanged(ref _IgnoreReadOnlyFolders, value);
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
		private TimeSpan _TimeElapsed;
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
		public MainWindowVM() {
			var dir = new DirectoryInfo(Utils.ThumbnailDirectory);
			if (!dir.Exists)
				dir.Create();
			Scanner.ScanDone += Scanner_ScanDone;
			Scanner.Progress += Scanner_Progress;
			Scanner.ThumbnailsPopulated += Scanner_ThumbnailsPopulated;
			Scanner.DatabaseCleaned += Scanner_DatabaseCleaned;
			Scanner.FilesEnumerated += Scanner_FilesEnumerated;
			Logger.Instance.LogItemAdded += Instance_LogItemAdded;
			//Ensure items added before GUI was ready will be shown 
			Instance_LogItemAdded(null, null);

		}

		private void Scanner_ThumbnailsPopulated(object sender, EventArgs e) {
			//Reset properties
			ScanProgressText = string.Empty;
			RemainingTime = new TimeSpan();
			ScanProgressValue = 0;
			ScanProgressMaxValue = 100;
		}

		private void Scanner_FilesEnumerated(object sender, EventArgs e) => IsBusy = false;

		private void Scanner_DatabaseCleaned(object sender, EventArgs e) => IsBusy = false;

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
					new XElement("Thumbnails", Thumbnails),
					new XElement("IncludeSubDirectories", IncludeSubDirectories),
					new XElement("IncludeImages", IncludeImages),
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
			foreach (var n in xDoc.Descendants("Percent"))
				if (int.TryParse(n.Value, out var percent))
					Percent = percent;
			foreach (var n in xDoc.Descendants("Thumbnails"))
				if (int.TryParse(n.Value, out var thumbnails))
					Thumbnails = thumbnails;
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
		private void Scanner_Progress(object sender, ScanEngine.OwnScanProgress e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				ScanProgressText = e.CurrentFile;
				RemainingTime = e.Remaining;
				ScanProgressValue = e.CurrentPosition;
				TimeElapsed = e.Elapsed;
				ScanProgressMaxValue = Scanner.ScanProgressMaxValue;
			});

		private void Instance_LogItemAdded(object sender, EventArgs e) =>
			Dispatcher.UIThread.InvokeAsync(() => {
				while (Logger.Instance.LogEntries.Count > 0) {
					if (Logger.Instance.LogEntries.TryTake(out var item))
						LogItems.Add(item);
				}
			});

		private void Scanner_ScanDone(object sender, EventArgs e) {
			Scanner.PopulateDuplicateThumbnails();

			var dupGroupHandled = new HashSet<Guid>();
			foreach (var itm in Scanner.Duplicates) {
				var dup = new DuplicateItemViewModel(itm);
				//Set best property in duplicate group
				var others = Scanner.Duplicates.Where(a => a.GroupId == dup.GroupId && a.Path != dup.Path).ToList();
				dup.SizeForeground = others.Any(a => a.SizeLong < dup.SizeLong) ? Brushes.Red : Brushes.Green;
				dup.FrameSizeForeground = others.Any(a => a.FrameSizeInt > dup.FrameSizeInt) ? Brushes.Red : Brushes.Green;
				dup.DurationForeground = others.Any(a => a.Duration > dup.Duration) ? Brushes.Red : Brushes.Green;
				dup.BitRateForeground = others.Any(a => a.BitRateKbs > dup.BitRateKbs) ? Brushes.Red : Brushes.Green;
				//Since we cannot group in Avalonia, let's at least highlight items that belong together
				if (!dupGroupHandled.Contains(dup.GroupId)) {
					duplicateList.Add(new DuplicateItemViewModel(Properties.Resources.DuplicateGroup, dup.GroupId));
					dupGroupHandled.Add(dup.GroupId);
				}
				duplicateList.Add(dup);
			}

			var dynamicFilter = this.WhenValueChanged(x => x.SearchText)
					.Select(BuildFilter);

			var loader = duplicateList.AsObservableList().Connect()
			.Filter(dynamicFilter)
			.Sort(new DuplicateItemComparer())
			.Bind(out var bindingData)
			.Subscribe();


			Duplicates = bindingData;

			//And done
			IsScanning = false;
		}

		private static Func<DuplicateItemViewModel, bool> BuildFilter(string searchText) {
			if (string.IsNullOrEmpty(searchText)) return trade => true;

			return t => t.IsGroupHeader == false && t.Path.Contains(searchText, StringComparison.OrdinalIgnoreCase);
		}

		public ReactiveCommand<Unit, Unit> AddIncludesToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = Properties.Resources.SelectFolder
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			if (!Includes.Contains(result))
				Includes.Add(result);
		});
		public ReactiveCommand<Unit, Unit> LatestReleaseCommand => ReactiveCommand.Create(() => {
			try {
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
					FileName = "https://github.com/0x90d/videoduplicatefinder/releases",
					UseShellExecute = true
				});
			}
			catch { }
		});
		public ReactiveCommand<Unit, Unit> CleanDatabaseCommand => ReactiveCommand.Create(() => {
			IsBusy = true;
			IsBusyText = Properties.Resources.CleaningDatabase;
			Scanner.CleanupDatabase();
		});
		public ReactiveCommand<ListBox, Action> RemoveIncludesFromListCommand => ReactiveCommand.Create<ListBox, Action>(lbox => {
			while (lbox.SelectedItems.Count > 0)
				Includes.Remove((string)lbox.SelectedItems[0]);
			return null;
		});
		public ReactiveCommand<Unit, Unit> AddBlacklistToListCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = Properties.Resources.SelectFolder
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
		public ReactiveCommand<DuplicateItemViewModel, Unit> OpenItemInFolderCommand => ReactiveCommand.Create< DuplicateItemViewModel>(currentItem => {
			Process.Start(new ProcessStartInfo {
				FileName = currentItem.Folder,
				UseShellExecute = true,
				Verb = "open"
			});
		});
		public ReactiveCommand<Unit, Unit> ClearLogCommand => ReactiveCommand.Create(() => { LogItems.Clear(); });
		public ReactiveCommand<Unit, Unit> SaveLogCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new SaveFileDialog().ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			var sb = new StringBuilder();
			foreach (var l in LogItems)
				sb.AppendLine(l.ToString());
			try {
				File.WriteAllText(result, sb.ToString());
			}
			catch (Exception e) {
				Logger.Instance.Info(e.Message);
			}
		});


		public ReactiveCommand<Unit, Unit> StartScanCommand => ReactiveCommand.CreateFromTask(async () => {
			if (!DuplicateFinderEngine.Utils.FfFilesExist) {
				await MessageBoxService.Show(Properties.Resources.FFmpegFFprobeIsMissing);
				return;
			}

			duplicateList.Clear();
			try {
				foreach (var f in new DirectoryInfo(Utils.ThumbnailDirectory).EnumerateFiles())
					f.Delete();
			}
			catch (Exception e) {
				Logger.Instance.Info(e.Message);
				return;
			}
			IsScanning = true;
			//Set scan settings
			Scanner.Settings.IncludeSubDirectories = IncludeSubDirectories;
			Scanner.Settings.IncludeImages = IncludeImages;
			Scanner.Settings.IgnoreReadOnlyFolders = IgnoreReadOnlyFolders;
			Scanner.Settings.Percent = Percent;
			Scanner.Settings.ThumbnailCount = Thumbnails;
			Scanner.Settings.IncludeList.Clear();
			foreach (var s in Includes)
				Scanner.Settings.IncludeList.Add(s);
			Scanner.Settings.BlackList.Clear();
			foreach (var s in Blacklists)
				Scanner.Settings.BlackList.Add(s);
			//Start scan
			IsBusy = true;
			IsBusyText = Properties.Resources.EnumeratingFiles;
			Scanner.StartSearch();
		});
		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalCommand => ReactiveCommand.Create(() => {
			var blackListGroupID = new HashSet<Guid>();
			duplicateList.Edit(updater => {
				foreach (var first in updater) {
					if (first.IsGroupHeader) continue;
					if (blackListGroupID.Contains(first.GroupId)) continue; //Dup has been handled already
					var l = updater.Where(d => !d.IsGroupHeader && d.Equals(first) && !d.Path.Equals(first.Path));
					var dupMods = l as DuplicateItemViewModel[] ?? l.ToArray();
					if (!dupMods.Any()) continue;
					foreach (var dup in dupMods)
						dup.Checked = true;
					first.Checked = false;
					blackListGroupID.Add(first.GroupId);
				}
			});
		});
		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalButSizeCommand => ReactiveCommand.Create(() => {
			var blackListGroupID = new HashSet<Guid>();
			duplicateList.Edit(updater => {
				foreach (var first in updater) {
					if (first.IsGroupHeader) continue;
					if (blackListGroupID.Contains(first.GroupId)) continue; //Dup has been handled already
					var l = updater.Where(d => !d.IsGroupHeader && d.EqualsButSize(first) && !d.Path.Equals(first.Path));
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
			});
		});
		public ReactiveCommand<Unit, Unit> CheckLowestQualityCommand => ReactiveCommand.Create(() => {
			var blackListGroupID = new HashSet<Guid>();
			duplicateList.Edit(updater => {
				foreach (var first in updater) {
					if (first.IsGroupHeader) continue;
					if (blackListGroupID.Contains(first.GroupId)) continue; //Dup has been handled already
					var l = updater.Where(d => !d.IsGroupHeader && d.EqualsButQuality(first) && !d.Path.Equals(first.Path));
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
			});
		});
		public ReactiveCommand<Unit, Unit> ClearSelectionCommand => ReactiveCommand.Create(() => {
			duplicateList.Edit(updater => {
				for (var i = 0; i < updater.Count; i++)
					updater[i].Checked = false;
			});
		});
		public ReactiveCommand<Unit, Unit> DeleteSelectionCommand => ReactiveCommand.Create(() => { DeleteInternal(true); });
		public ReactiveCommand<Unit, Unit> RemoveSelectionFromListCommand => ReactiveCommand.Create(() => { DeleteInternal(false); });

		async void DeleteInternal(bool fromDisk) {
			if (Duplicates.Count == 0) return;
			var dlgResult = await MessageBoxService.Show(
				fromDisk
					? Properties.Resources.ConfirmationDeleteFromDisk
					: Properties.Resources.ConfirmationDeleteFromList,
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult == MessageBoxButtons.No) return;
			duplicateList.Edit(updater => {
				for (var i = updater.Count - 1; i >= 0; i--) {
					var dub = updater[i];
					if (dub.Checked == false) continue;
					if (fromDisk)
						try {
							File.Delete(dub.Path);
						}
						catch (Exception ex) {
							Logger.Instance.Info(string.Format(
								Properties.Resources.FailedToDeleteFileReasonStacktrace,
								dub.Path, ex.Message, ex.StackTrace));
							continue;
						}

					updater.RemoveAt(i);
				}

				//Hide groups with just one item left
				for (var i = updater.Count - 1; i >= 0; i--) {
					var first = updater[i];
					if (updater.Any(s => !s.IsGroupHeader && s.GroupId == first.GroupId && s.Path != first.Path)) continue;
					updater.RemoveAt(i);
				}
			});
		}
		public ReactiveCommand<Unit, Unit> CopySelectionCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = Properties.Resources.SelectFolder
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			FileHelper.CopyFile(duplicateList.Items.Where(s => s.Checked).Select(s => s.Path), result, true, false,
				out var errorCounter);
			if (errorCounter > 0)
				await MessageBoxService.Show(Properties.Resources.FailedToCopyMoveSomeFilesPleaseCheckLog);
		});
		public ReactiveCommand<Unit, Unit> MoveSelectionCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await new OpenFolderDialog {
				Title = Properties.Resources.SelectFolder
			}.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(result)) return;
			FileHelper.CopyFile(duplicateList.Items.Where(s => s.Checked).Select(s => s.Path), result, true, true,
				out var errorCounter);
			if (errorCounter > 0)
				await MessageBoxService.Show(Properties.Resources.FailedToCopyMoveSomeFilesPleaseCheckLog);
		});

		public ReactiveCommand<Unit, Unit> SaveToHtmlCommand => ReactiveCommand.CreateFromTask(async (a) => {
			if (Scanner == null || Duplicates.Count == 0) return;
			var ofd = new SaveFileDialog {
				Title = Properties.Resources.SaveDuplicates,
				Filters = new List<FileDialogFilter>
				{
					new FileDialogFilter
					{
						Name = "Html",
						Extensions = new List<string> { "*.html" }
					}
			   }
			};

			var file = await ofd.ShowAsync(ApplicationHelpers.MainWindow);
			if (string.IsNullOrEmpty(file)) return;
			try {
				duplicateList.Items.Where(s => !s.IsGroupHeader).ToList().ToHtmlTable(file);
			}
			catch (Exception e) {
				Logger.Instance.Info(e.Message + Environment.NewLine + Environment.NewLine + e.StackTrace);
			}
		});

	}
}
