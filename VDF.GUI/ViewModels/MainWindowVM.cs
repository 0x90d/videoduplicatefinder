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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Mvvm;
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

		private readonly AvaloniaList<RowNode> Duplicates = new() { ResetBehavior = ResetBehavior.Reset }; //For TreeDataGrid
		private readonly List<RowNode> _allGroups = new(); //Master list of all groups (for filtering)
		private readonly Dictionary<Guid, RowNode> _groupIndex = new();
		public HierarchicalTreeDataGridSource<RowNode> TreeSource { get; }

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

		bool _ShowTreeDataGrid = false;
		public bool ShowTreeDataGrid {
			get => _ShowTreeDataGrid;
			set => this.RaiseAndSetIfChanged(ref _ShowTreeDataGrid, value);
		}
		bool _IsScanning;
		public bool IsScanning {
			get => _IsScanning;
			set {
				this.RaiseAndSetIfChanged(ref _IsScanning, value);
			}
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

		private static readonly Mvvm.ExtraShortDateTimeConverter ExtraShortDateTimeConverterInstance = new();
		private static readonly Mvvm.IsBestConverter IsBestConverter = new();
		private static readonly Mvvm.NegateBoolConverter NegateBoolConverter = new();

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


			SortOrders = [
				new("None", (a, b) => 0),
				new("Size Ascending", (a, b) => CompareLeafBy<long>(a, b, vm => vm.ItemInfo.SizeLong)),
				new("Size Descending", (a, b) => CompareLeafBy<long>(a, b, vm => vm.ItemInfo.SizeLong, desc: true)),
				new("Resolution Ascending", (a, b) => CompareLeafBy<int>(a, b, vm => vm.ItemInfo.FrameSizeInt)),
				new("Resolution Descending", (a, b) => CompareLeafBy<int>(a, b, vm => vm.ItemInfo.FrameSizeInt, desc: true)),
				new("Duration Ascending", (a, b) => CompareLeafBy<TimeSpan>(a, b, vm => vm.ItemInfo.Duration)),
				new("Duration Descending", (a, b) => CompareLeafBy<TimeSpan>(a, b, vm => vm.ItemInfo.Duration, desc: true)),
				new("Date Created Ascending", (a, b) => CompareLeafBy<DateTime>(a, b, vm => vm.ItemInfo.DateCreated)),
				new("Date Created Descending", (a, b) => CompareLeafBy<DateTime>(a, b, vm => vm.ItemInfo.DateCreated, desc: true)),
				new("Similarity Ascending", (a, b) => CompareLeafBy<float>(a, b, vm => vm.ItemInfo.Similarity)),
				new("Similarity Descending", (a, b) => CompareLeafBy<float>(a, b, vm => vm.ItemInfo.Similarity, desc: true)),
				new("Group Has Selected Items Ascending",  (ga,gb) => CompareGroupsByInt(ga, gb, g => GroupHasChecked(g) ? 1 : 0)),
				new("Group Has Selected Items Descending", (ga,gb) => CompareGroupsByInt(ga, gb, g => GroupHasChecked(g) ? 1 : 0, desc:true)),
				new("Group Size Ascending",  (ga,gb) => CompareGroupsByInt(ga, gb, g => g.Children.Count)),
				new("Group Size Descending", (ga,gb) => CompareGroupsByInt(ga, gb, g => g.Children.Count, desc:true))
			];

			_SortOrder = SortOrders[0];

			this.WhenAnyValue(vm => vm.FilterByPath)
					.Throttle(TimeSpan.FromMilliseconds(500), RxApp.MainThreadScheduler)
						.Subscribe(_ => {
							RebuildSearchPathIndex();
							ApplyFilter();
							RefreshGroupStats();
						});


			TreeSource = new HierarchicalTreeDataGridSource<RowNode>(Duplicates) {
				Columns =
					{
					new HierarchicalExpanderColumn<RowNode>(
						new TemplateColumn<RowNode>(
							header:  App.Lang["DuplicateList.Header.GroupItem"],
							cellTemplate: new FuncDataTemplate<RowNode>((node, _) =>
							{
								if (node is null) return new Panel();
								if (node.IsGroup)
								{
									var tb = new TextBlock
									{
										FontWeight = FontWeight.Bold,
										Margin = new Thickness(8, 0, 0, 0)
									};
									tb.Bind(TextBlock.TextProperty, new Binding(nameof(RowNode.Header)));
									return tb;
								}

								var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

								var cb = new CheckBox();
								cb.Bind(CheckBox.IsCheckedProperty, new Binding("Item.Checked"));

								var img = new Image { Stretch = Stretch.None };
								img.Bind(Image.SourceProperty, new Binding("Item.Thumbnail"));
								img.DoubleTapped += (_, e) =>
								{
									if (img.DataContext is RowNode rn && rn.Item is { } item)
									{
										OpenItems();
									}
									e.Handled = true;
								};

								ToolTip.SetTip(img, new TextBlock { Text = App.Lang["DuplicateList.Thumbnail.Tooltip.OpenFile"] });



								panel.Children.Add(cb);
								panel.Children.Add(img);
								return panel;
							}, supportsRecycling: false)
						, width: GridLength.Auto),
						n => n.Children,
						isExpandedSelector: n => n.IsExpanded
					),
					
					// 2) Path
					new TemplateColumn<RowNode>(
						header: App.Lang["Path"],
						cellTemplate: new FuncDataTemplate<RowNode>((n, _) =>
						{
							if (n is null || n.IsGroup) return new Panel();

							var path = new TextBlock
							{
								TextWrapping = TextWrapping.Wrap,
								Margin = new Thickness(4, 0, 0, 0),
							};
							path.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.Path"));

							path.DoubleTapped += (_, e) =>
							{
								if (path.DataContext is RowNode rn && rn.Item is { })
									OpenItemsInFolder();
								e.Handled = true;
							};
							ToolTip.SetTip(path, new TextBlock { Text = App.Lang["DuplicateList.FilePath.Tooltip.OpenInExplorer"] });

							return path;
						}, supportsRecycling: false), width: new GridLength(1, GridUnitType.Star)
					),

					// 2) Duration / type  (Image: Format / Video: Duration)
					new TemplateColumn<RowNode>(
						header: MultiHeader(App.Lang["DuplicateList.Header.DurationType"], App.Lang["DuplicateList.Header.Resolution"], App.Lang["DuplicateList.Header.Size"], App.Lang["DuplicateList.Header.CreationDate"]),
						cellTemplate: new FuncDataTemplate<RowNode>((n, _) =>
						{
							if (n is null || n.IsGroup) return new Panel();

							var sp = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };

							// Row 0: Image -> Format
							var tFormat = new TextBlock();
							tFormat.Bind(Visual.IsVisibleProperty, new Binding("Item.ItemInfo.IsImage"));
							tFormat.Bind(TextBlock.TextProperty,   new Binding("Item.ItemInfo.Format"));
							sp.Children.Add(tFormat);

							// Row 0 (alternativ): Video -> Duration
							var tDur = new TextBlock();
							tDur.Bind(Visual.IsVisibleProperty, new Binding("Item.ItemInfo.IsImage"){ Converter = NegateBoolConverter });
							tDur.Bind(TextBlock.TextProperty,   new Binding("Item.ItemInfo.Duration"){ StringFormat = "{0:hh\\:mm\\:ss}"});
							tDur.Bind(TextBlock.ForegroundProperty, new Binding("Item.ItemInfo.IsBestDuration"){ Converter = IsBestConverter });
							sp.Children.Add(tDur);

							//Resolution
							var tRes = new TextBlock { Margin = new Thickness(0) };
							tRes.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.FrameSize"));
							tRes.Bind(TextBlock.ForegroundProperty, new Binding("Item.ItemInfo.IsBestFrameSize"){ Converter = IsBestConverter });
							sp.Children.Add(tRes);

							//Size
							var tSize = new TextBlock();
							tSize.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.Size"));
							tSize.Bind(TextBlock.ForegroundProperty, new Binding("Item.ItemInfo.IsBestSize"){ Converter = IsBestConverter });
							sp.Children.Add(tSize);

							//Created
							var tDate = new TextBlock();
							tDate.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.DateCreated")
							{
								Converter = ExtraShortDateTimeConverterInstance
							});
							sp.Children.Add(tDate);

							return sp;
						}, supportsRecycling: false)
					),

					// 3) Format / Fps / Bitrate (only for videos)
					new TemplateColumn<RowNode>(
						header: MultiHeader(App.Lang["DuplicateList.Header.VideoFormat"], App.Lang["DuplicateList.Header.VideoFps"], App.Lang["DuplicateList.Header.VideoBitRate"], " "),
						cellTemplate: new FuncDataTemplate<RowNode>((n, _) =>
						{
							if (n is null || n.IsGroup) return new Panel();

							var sp = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
							sp.Bind(Visual.IsVisibleProperty, new Binding("Item.ItemInfo.IsImage"){ Converter = NegateBoolConverter });

							var tFmt = new TextBlock();
							tFmt.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.Format"));
							sp.Children.Add(tFmt);

							var tFps = new TextBlock();
							tFps.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.Fps"){ StringFormat = "{0} fps" });
								tFps.Bind(TextBlock.ForegroundProperty, new Binding("Item.ItemInfo.IsBestFps"){ Converter = IsBestConverter });
							sp.Children.Add(tFps);

							var tBr = new TextBlock();
							tBr.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.BitRateKbs"){ StringFormat = "{0} kbps" });
							tBr.Bind(TextBlock.ForegroundProperty, new Binding("Item.ItemInfo.IsBestBitRateKbs"){ Converter = IsBestConverter });
							sp.Children.Add(tBr);

							return sp;
						}, supportsRecycling: false)
					),

					// 4) Audio (only for videos)
					new TemplateColumn<RowNode>(
						header: MultiHeader(App.Lang["DuplicateList.Header.AudioFormat"], App.Lang["DuplicateList.Header.AudioChannel"], App.Lang["DuplicateList.Header.AudioSampleRate"], " "),
						cellTemplate: new FuncDataTemplate<RowNode>((n, _) =>
						{
							if (n is null || n.IsGroup) return new Panel();

							var sp = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
							sp.Bind(Visual.IsVisibleProperty, new Binding("Item.ItemInfo.IsImage"){ Converter = NegateBoolConverter });

							var tAf = new TextBlock();
							tAf.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.AudioFormat"));
							sp.Children.Add(tAf);

							var tCh = new TextBlock();
							tCh.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.AudioChannel"));
							sp.Children.Add(tCh);

							var tSr = new TextBlock();
							tSr.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.AudioSampleRate"));
							tSr.Bind(TextBlock.ForegroundProperty, new Binding("Item.ItemInfo.IsBestAudioSampleRate"){ Converter = IsBestConverter });
							sp.Children.Add(tSr);

							return sp;
						}, supportsRecycling: false)
					),

					// 5) Similarity
					new TemplateColumn<RowNode>(
						header: App.Lang["DuplicateList.Header.Similarity"],
						cellTemplate: new FuncDataTemplate<RowNode>((n, _) =>
						{
							if (n is null || n.IsGroup) return new Panel();
							var tb = new TextBlock();
							tb.Bind(TextBlock.TextProperty, new Binding("Item.ItemInfo.Similarity"));
							tb.HorizontalAlignment = HorizontalAlignment.Center;
							return tb;
						}, supportsRecycling: false)
					)
				}
			};
			TreeSource.RowSelection!.SingleSelect = false;
		}


		public async void Thumbnails_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
			bool isReadyToCompare = IsGathered;
			isReadyToCompare &= Scanner.Settings.ThumbnailCount == e.NewValue;
			if (!isReadyToCompare && ApplicationHelpers.MainWindowDataContext.IsReadyToCompare)
				await MessageBoxService.Show($"Number of thumbnails can't be changed between quick rescans. Full scan will be required.");
			ApplicationHelpers.MainWindowDataContext.IsReadyToCompare = isReadyToCompare;
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

		void Scanner_FilesEnumerated(object? sender, EventArgs e) => ChangeIsBusyMessage();

		async void Scanner_DatabaseCleaned(object? sender, EventArgs e) {
			IsBusy = false;
			await MessageBoxService.Show("Database cleaned!");
		}

		public async Task<bool> SaveScanResults() {
			if (_allGroups.Count == 0 || !SettingsFile.Instance.AskToSaveResultsOnExit) {
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
			IsBusyOverlayText = "Loading database...";
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

				FillDuplicatesFromScanner(Scanner.Duplicates);

				if (SettingsFile.Instance.GeneratePreviewThumbnails)
					Scanner.RetrieveThumbnails();

				RebuildSearchPathIndex();
				RefreshGroupStats();
			});

		private void FillDuplicatesFromScanner(IEnumerable<DuplicateItem> items) {
			int groupCounter = 0;
			var groups = items
						   .GroupBy(x => x.GroupId)
						   .Select(g => {
							   var node = RowNode.Group(
								   header: $"Group #{groupCounter} ({g.Count()})",
								   items: g.Select(x => new DuplicateItemVM(x)));
							   groupCounter++;
							   return node;
						   })
						   .ToList();
			FillDuplicates(groups);
		}
		private void FillDuplicates(IEnumerable<RowNode>? items) {
			if (items == null) return;
			DetachAllCheckedHandlers();
			Duplicates.Clear();
			_allGroups.Clear();
			_groupIndex.Clear();

			foreach (var item in items) {
				if (item.AllChildren.Count == 0 ||
					item.AllChildren[0].Item == null) continue;
				_groupIndex[item.AllChildren[0].Item!.ItemInfo.GroupId] = item;
				foreach (var child in item.AllChildren)
					if (child.Item != null)
						AttachCheckedHandlers(child.Item);
			}

			_allGroups.AddRange(items);
			Duplicates.AddRange(items);
			ApplyFilter();
			TotalSizeRemovedInternal = 0;
			ShowTreeDataGrid = true;
		}
		void AttachCheckedHandlers(DuplicateItemVM vm) {
			vm.PropertyChanged += Vm_PropertyChanged;
		}

		private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			if (e.PropertyName == nameof(DuplicateItemVM.Checked)) {
				if (!Dispatcher.UIThread.CheckAccess())
					Dispatcher.UIThread.Post(RecomputeSelectionCounter);
				else
					RecomputeSelectionCounter();
			}
		}

		private void DetachChecked(DuplicateItemVM vm) {
			vm.PropertyChanged -= Vm_PropertyChanged;
		}
		private void DetachAllCheckedHandlers() {
			foreach (var vm in EnumerateAllItems())
				vm.PropertyChanged -= Vm_PropertyChanged;
		}
		void RecomputeSelectionCounter() => DuplicatesSelectedCounter = _allGroups.Sum(g => g.AllChildren.Count(n => n.Item?.Checked == true));
		private void RefreshGroupStats() {
			TotalDuplicates = Duplicates.Sum(x => x.Children.Count);
			TotalDuplicatesSize = Duplicates.Sum(x => x.Children.Sum(y => y.Item!.ItemInfo.SizeLong)).BytesToString();
			TotalDuplicateGroups = Duplicates.Count;
		}

		private DuplicateItemVM? GetSelectedDuplicateItem() {
			var selection = GetSelectedDuplicates();
			if (selection.Count == 0)
				return null;
			return selection[0];
		}

		private List<DuplicateItemVM> GetSelectedDuplicates() => (TreeSource.RowSelection?.SelectedItems ?? Array.Empty<RowNode>())
																		   .Where(n => n != null && !n.IsGroup && n.Item is not null)
																		   .Select(n => n!.Item!)
																		   .ToList();


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
			IsBusyOverlayText = "Cleaning database...";
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
				FileTypeFilter = [new FilePickerFileType("Json File") { Patterns = ["*.json"] }]
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
				FileTypeChoices = [new FilePickerFileType("Json Files") { Patterns = ["*.json"] }]
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
				FileTypeChoices = [new FilePickerFileType("Json Files") { Patterns = ["*.json"] }]
			});
			if (string.IsNullOrEmpty(result)) return;

			options.Converters.Add(new ImageJsonConverter());
			try {
				List<DuplicateItem> list = _allGroups
												.Where(g => g.IsGroup)
												.SelectMany(g => g.Children)
												.Where(c => !c.IsGroup && c.Item != null)
												.Select(c => c.Item!.ItemInfo)
												.OrderBy(x => x.GroupId)
												.ToList();
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

		private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
		private static JsonSerializerOptions CreateJsonOptions() {
			var o = new JsonSerializerOptions {
				IncludeFields = true,
			};
			o.Converters.Add(new BitmapJsonConverter());
			o.Converters.Add(new ImageJsonConverter());
			return o;
		}
		async Task ExportScanResultsIncludingThumbnails(string? path = null) {
			path ??= await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions() {
				SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
				DefaultExtension = ".json",
				FileTypeChoices = [new FilePickerFileType("Scan Results") { Patterns = ["*.scanresults"] }]
			});

			if (string.IsNullOrEmpty(path)) return;


			IsBusy = true;
			IsBusyOverlayText = "Saving scan results to disk...";
			var dir = Path.GetDirectoryName(path)!;
			var tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp");

			try {
				var snapshot = new List<RowNode>(_allGroups);

				await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true)) {
					await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
					await stream.FlushAsync();
				}

				File.Move(tmp, path, overwrite: true);
			}
			catch (Exception ex) {
				IsBusy = false;
				string error = $"Exporting scan results has failed because of {ex}";
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
				FileTypeFilter = [new FilePickerFileType("Scan Results") { Patterns = ["*.scanresults"] }]
			});
			if (string.IsNullOrEmpty(result)) return;
			ImportScanResultsIncludingThumbnails(result);
		});
		async void ImportScanResultsIncludingThumbnails(string? path = null) {
			if (_allGroups.Count > 0) {
				MessageBoxButtons? result = await MessageBoxService.Show($"Importing scan results will clear the current list, continue?", MessageBoxButtons.Yes | MessageBoxButtons.No);
				if (result != MessageBoxButtons.Yes) return;
			}

			if (path == null) {
				path = await Utils.PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions() {
					SuggestedStartLocation = await ApplicationHelpers.MainWindow.StorageProvider.TryGetFolderFromPathAsync(CoreUtils.CurrentFolder),
					FileTypeFilter = [new FilePickerFileType("Scan Results") { Patterns = ["*.scanresults"] }]
				});
			}
			if (string.IsNullOrEmpty(path)) return;

			try {
				using var stream = File.OpenRead(path);
				IsBusy = true;
				IsBusyOverlayText = "Importing scan results from disk...";
				var list = await JsonSerializer.DeserializeAsync<AvaloniaList<RowNode>>(stream, JsonOptions);
				FillDuplicates(list);

				RefreshGroupStats();
				IsBusy = false;
				stream.Close();
			}
			catch (JsonException) {
				IsBusy = false;
				string error = $"Importing scan results has failed because it's likely corrupted";
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
			catch (Exception ex) {
				IsBusy = false;
				string error = $"Importing scan results has failed because of {ex}";
				Logger.Instance.Info(error);
				await MessageBoxService.Show(error);
			}
		}

		public ReactiveCommand<DuplicateItemVM, Unit> OpenItemCommand => ReactiveCommand.Create<DuplicateItemVM>(currentItem => {
			OpenItems();
		});

		public ReactiveCommand<Unit, Unit> OpenItemInFolderCommand => ReactiveCommand.Create(OpenItemsInFolder);

		public ReactiveCommand<string, Unit> OpenGroupCommand => ReactiveCommand.Create<string>(openInFolder => {
			if (GetSelectedDuplicateItem() is DuplicateItemVM currentItem) {
				List<DuplicateItemVM> items = EnumerateItemsInGroup(currentItem.ItemInfo.GroupId).ToList();
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
				await MessageBoxService.Show($"Failed to open files: {ex.Message}");
				return;
			}
		}

		public async void OpenItemsInFolder() {
			if (AlternativeOpen(SettingsFile.Instance.CustomCommands.OpenItemInFolder,
								SettingsFile.Instance.CustomCommands.OpenMultipleInFolder))
				return;

			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			try {
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
			catch (Exception ex) {
				await MessageBoxService.Show($"Failed to open files: {ex.Message}");
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

		public ReactiveCommand<Unit, Unit> RenameFileCommand => ReactiveCommand.CreateFromTask(async () => {
			if (GetSelectedDuplicateItem() is not DuplicateItemVM currentItem) return;
			if (!File.Exists(currentItem.ItemInfo.Path)) {
				await MessageBoxService.Show("The file no longer exists");
				return;
			}
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
					61 => 7, // FFmpeg 7.x
					60 => 6, // FFmpeg 6.x
					59 => 5, // FFmpeg 5.x
					_ => 0  // unknown
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

			// Windows-specific placement instructions (VDF\bin)
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
				// macOS/Linux hints
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
				await MessageBoxService.Show("The custom database folder does not exist!");
				return;
			}
			if (_allGroups.Count > 0) {
				if (await MessageBoxService.Show("Do you want to discard the results and start a new scan?", MessageBoxButtons.Yes | MessageBoxButtons.No) != MessageBoxButtons.Yes) {
					return;
				}
			}

			if ((SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.NativeFFmpegExists) ||
				(!SettingsFile.Instance.UseNativeFfmpegBinding && !ScanEngine.FFmpegExists)) {
				await MessageBoxService.Show(GetRequiredFfmpegPackage(CoreUtils.CurrentFolder));
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


			_allGroups.Clear();
			Duplicates.Clear();
			_groupIndex.Clear();

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
			Scanner.Settings.UsePHashing = SettingsFile.Instance.UsePHash;
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

			ShowTreeDataGrid = false;
			ChangeIsBusyMessage();

			IsBusy = true;

			//Start scan
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
				foreach (DuplicateItemVM duplicateItem in EnumerateItemsInGroup(data.ItemInfo.GroupId))
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
				RemovePathsFromGroup(gid, blacklist);
			});
		});

		public ReactiveCommand<Unit, Unit> ShowGroupInThumbnailComparerCommand => ReactiveCommand.Create(() => {

			if (GetSelectedDuplicateItem() is not DuplicateItemVM data) return;
			List<LargeThumbnailDuplicateItem> items = new();

			if (GetSelectedDuplicates().Count == 1) {
				foreach (DuplicateItemVM duplicateItem in EnumerateItemsInGroup(data.ItemInfo.GroupId))
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
			}
			else {
				foreach (DuplicateItemVM duplicateItem in GetSelectedDuplicates())
					items.Add(new LargeThumbnailDuplicateItem(duplicateItem));
			}

			ThumbnailComparer thumbnailComparer = new(items);
			thumbnailComparer.Show();
		});
		async void DeleteInternal(bool fromDisk,
						  bool blackList = false,
						  bool createSymbolLinksInstead = false,
						  bool permanently = false) {
			if (_allGroups.Count == 0) return;

			var toDelete = EnumerateAllItems()
								.Where(d => d.Checked && d.IsVisibleInFilter)
								.ToList();
			if (toDelete.Count == 0) return;

			MessageBoxButtons? dlgResult = await MessageBoxService.Show(
				fromDisk
					? $"Are you sure you want to{(CoreUtils.IsWindows && !permanently ? " move" : " permanently delete")} the selected files{(CoreUtils.IsWindows && !permanently ? " to recycle bin (only if supported, i.e. network files will be deleted instead)" : " from disk")}?"
					: $"Are you sure to delete selected from list (keep files){(blackList ? " and blacklist them" : string.Empty)}?",
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (dlgResult != MessageBoxButtons.Yes) return;

			var keepByGroup = EnumerateAllItems()
				   .GroupBy(d => d.ItemInfo.GroupId)
				   .ToDictionary(
					   g => g.Key,
					   g => g.FirstOrDefault(x => !x.Checked)  // can be null if all are checked
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
						else if (CoreUtils.IsWindows && !permanently) {
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
						else {
							File.Delete(dub.ItemInfo.Path);
							freedBytes += dub.ItemInfo.SizeLong;
						}
					}

					ScanEngine.RemoveFromDatabase(fe);
					if (blackList)
						ScanEngine.BlackListFileEntry(dub.ItemInfo.Path);

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

			ApplyDeletionsAndDropSingles(actuallyDeleted);
			RefreshGroupStats();


			ScanEngine.SaveDatabase();

			if (SettingsFile.Instance.BackupAfterListChanged)
				await ExportScanResultsIncludingThumbnails(BackupScanResultsFile);
		}

		public ReactiveCommand<Unit, Unit> ExpandAllGroupsCommand => ReactiveCommand.Create(() => {
			foreach (var item in Duplicates) {
				if (item.IsGroup)
					item.IsExpanded = true;
			}
		});
		public ReactiveCommand<Unit, Unit> CollapseAllGroupsCommand => ReactiveCommand.Create(() => {
			foreach (var item in Duplicates) {
				if (item.IsGroup)
					item.IsExpanded = false;
			}
		});
		public ReactiveCommand<Unit, Unit> CopyPathsToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetSelectedDuplicates()) {
				if (item is not DuplicateItemVM currentItem) return;
				sb.AppendLine(currentItem.ItemInfo.Path);
			}
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
			await (ApplicationHelpers.MainWindow.Clipboard.SetTextAsync(sb.ToString().TrimEnd(new char[2] { '\r', '\n' })));
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
		});
		public ReactiveCommand<Unit, Unit> CopyFilenamesToClipboardCommand => ReactiveCommand.CreateFromTask(async () => {
			StringBuilder sb = new();
			foreach (var item in GetSelectedDuplicates()) {
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
