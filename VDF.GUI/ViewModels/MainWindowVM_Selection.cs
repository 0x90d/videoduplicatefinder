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
using System.Reactive;
using ActiproSoftware.Properties.Shared;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DynamicData;
using DynamicExpresso;
using DynamicExpresso.Exceptions;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

        public List<string> QualityCriteriaOrder { get; set; } = ["Duration", "Resolution", "FPS", "Bitrate", "Audio Bitrate"];

		public ReactiveCommand<Unit, Unit> OpenCustomSelectionCommand => ReactiveCommand.Create(() => {
			CustomSelectionView dlg = new(string.Empty);
			dlg.Show(ApplicationHelpers.MainWindow);
		});

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

			const string shortIdentifier = "item";

			var interpreter = new Interpreter()
				.ParseAsDelegate<Func<DuplicateItem, bool>>(SettingsFile.Instance.LastCustomSelectExpression, shortIdentifier);

			var groups = EnumerateAllItems()
							.Where(d => d.IsVisibleInFilter)
							.GroupBy(d => d.ItemInfo.GroupId)
							.ToList();

			var matchResults = groups
								.AsParallel()
								.Select(group => new {
									GroupId = group.Key,
									Items = group.ToList(),
									Matches = group.Where(item => interpreter(item.ItemInfo)).ToList()
								})
								.ToList();

			foreach (var result in matchResults) {
				if (result.Matches.Count == 0)
					continue;

				if (result.Matches.Count == result.Items.Count) {
					if (!userAsked) {
						var examplePath = result.Items.First().ItemInfo.Path;
						var message = $"There are groups where all items match your expression, for example '{examplePath}'.{Environment.NewLine}{Environment.NewLine}Do you want to have all items checked (Yes)? Or do you want to have NO items in these groups checked (No)?";

						var dialogResult = await MessageBoxService.Show(message, MessageBoxButtons.Yes | MessageBoxButtons.No);
						skipIfAllMatches = dialogResult == MessageBoxButtons.No;
						userAsked = true;
					}

					if (skipIfAllMatches)
						continue;
				}

				foreach (var dup in result.Matches)
					dup.Checked = true;
			}
		});
		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalCommand => ReactiveCommand.Create(() => {
			HashSet<Guid> blackListGroupID = new();

			foreach (var first in EnumerateAllItems()) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already

				var l = EnumerateAllItems().Where(d => d.IsVisibleInFilter && d.EqualsFull(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));

				var dupMods = l as DuplicateItemVM[] ?? l.ToArray();
				if (!dupMods.Any()) continue;
				foreach (var dup in dupMods)
					dup.Checked = true;
				first.Checked = false;
				blackListGroupID.Add(first.ItemInfo.GroupId);
			}
		});

		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalButSizeCommand => ReactiveCommand.Create(() => {
			HashSet<Guid> blackListGroupID = new();

			foreach (var first in EnumerateAllItems()) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				var l = EnumerateAllItems().Where(d => d.IsVisibleInFilter && d.EqualsButSize(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
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
		public ReactiveCommand<Unit, Unit> CheckOldestCommand => ReactiveCommand.Create(() => {
			HashSet<Guid> blackListGroupID = new();

			foreach (var first in EnumerateAllItems()) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				var l = EnumerateAllItems().Where(d => d.IsVisibleInFilter && d.EqualsButSize(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
				var dupMods = l as List<DuplicateItemVM> ?? l.ToList();
				if (!dupMods.Any()) continue;
				dupMods.Add(first);
				dupMods = dupMods.OrderByDescending(s => s.ItemInfo.DateCreated).ToList();
				dupMods[0].Checked = false;
				for (int i = 1; i < dupMods.Count; i++) {
					dupMods[i].Checked = true;
				}

				blackListGroupID.Add(first.ItemInfo.GroupId);
			}
		});
		public ReactiveCommand<Unit, Unit> CheckNewestCommand => ReactiveCommand.Create(() => {
			HashSet<Guid> blackListGroupID = new();

			foreach (var first in EnumerateAllItems()) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue;
				var l = EnumerateAllItems().Where(d => d.IsVisibleInFilter && d.EqualsButSize(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
				var dupMods = l as List<DuplicateItemVM> ?? l.ToList();
				if (!dupMods.Any()) continue;
				dupMods.Add(first);
				dupMods = dupMods.OrderBy(s => s.ItemInfo.DateCreated).ToList();
				dupMods[0].Checked = false;
				for (int i = 1; i < dupMods.Count; i++) {
					dupMods[i].Checked = true;
				}

				blackListGroupID.Add(first.ItemInfo.GroupId);
			}
		});
		public ReactiveCommand<Unit, Unit> CheckLowestQualityCommand => ReactiveCommand.CreateFromTask(async () => {
			var dlg = new QualityOrderDialog();
			var result = await dlg.ShowDialog<List<string>>(ApplicationHelpers.MainWindow);
			if (result == null || result.Count == 0) return;
			QualityCriteriaOrder = result;

			HashSet<Guid> blackListGroupID = new();

			foreach (var first in EnumerateAllItems()) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue;

				var dupMods = EnumerateAllItems()
					.Where(d => d.IsVisibleInFilter && d.EqualsButQuality(first) && d.ItemInfo.Path != first.ItemInfo.Path)
					.ToList();
				if (dupMods.Count == 0) continue;

				dupMods.Insert(0, first);

				var keep = dupMods[0];

				bool anyApplied = false;
				string? lastCriterion = null;

				foreach (var criterion in QualityCriteriaOrder) {
					if (criterion is ("Duration" or "FPS" or "Bitrate" or "Audio Bitrate") && keep.ItemInfo.IsImage)
						continue;

					// 1) first applicable criterion: always apply
					// 2) then: only apply if there is a tie with the *last* criterion applied
					bool tieOnLast = anyApplied && HasTieOn(lastCriterion!, dupMods, keep);

					if (!anyApplied || tieOnLast) {
						keep = ApplyCriterion(criterion, dupMods); // always "best" (sort in descending order)
						anyApplied = true;
						lastCriterion = criterion;
					}
				}

				// Keep the best ones, tick all the others (the worse ones)
				keep.Checked = false;
				for (int i = 0; i < dupMods.Count; i++)
					if (dupMods[i].ItemInfo.Path != keep.ItemInfo.Path)
						dupMods[i].Checked = true;

				blackListGroupID.Add(first.ItemInfo.GroupId);
			}
		});

		public ReactiveCommand<Unit, Unit> ClearSelectionCommand => ReactiveCommand.Create(() => {
			foreach (var vm in Duplicates
								.SelectMany(g => g.Children)
								.Select(c => c.Item)
								.Select(it => it!)) {
				vm.Checked = false;
			}
		});

		public ReactiveCommand<Unit, Unit> DeleteHighlightedCommand => ReactiveCommand.Create(() => {
			if (GetSelectedDuplicateItem() == null) return;
			var sel = TreeSource.RowSelection?.SelectedItems?.ToArray() ?? Array.Empty<RowNode>();
			RemoveSelectionFromTree(TreeSource.RowSelection?.SelectedItems);
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
		public ReactiveCommand<Unit, Unit> DeleteSelectionPermanentlyCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(true, permanently: true);
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

		public ReactiveCommand<Unit, Unit> CopySelectionCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					Title = "Select folder"
				});

			if (result == null || result.Count == 0) return;

			Utils.FileUtils.CopyFile(EnumerateAllItems().Where(s => s.Checked), result[0], true, false, out var errorCounter);
			if (errorCounter > 0)
				await MessageBoxService.Show("Failed to copy some files. Please check log!");
		});
		public ReactiveCommand<Unit, Unit> MoveSelectionCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					Title = "Select folder"
				});

			if (result == null || result.Count == 0) return;

			var selectedItems = EnumerateAllItems().Where(s => s.Checked).ToList();
			List<Tuple<DuplicateItemVM, FileEntry>> itemsToUpdate = new();
			foreach (var item in selectedItems) {
				if (ScanEngine.GetFromDatabase(item.ItemInfo.Path, out var dbEntry))
					itemsToUpdate.Add(Tuple.Create(item, dbEntry!));
			}
			Utils.FileUtils.CopyFile(selectedItems, result[0], true, true, out var errorCounter);
			foreach (var pair in itemsToUpdate) {
				ScanEngine.UpdateFilePathInDatabase(pair.Item1.ItemInfo.Path, pair.Item2);
			}
			ScanEngine.SaveDatabase();
			if (errorCounter > 0)
				await MessageBoxService.Show("Failed to move some files. Please check log!");
		});

		internal void RunCustomSelection(CustomSelectionData data) {

			IEnumerable<DuplicateItemVM> dups = EnumerateAllItems().Where(x => x.IsVisibleInFilter);
#if DEBUG
			int itemsCount = dups.Count();
			System.Diagnostics.Trace.WriteLine($"Custom selection items count: {itemsCount}");
#endif
			if (data.IgnoreGroupsWithSelectedItems) {
				HashSet<Guid> blackList = new();
				foreach (var first in dups.Where(x => x.Checked)) {
					if (blackList.Contains(first.ItemInfo.GroupId)) continue;
					blackList.Add(first.ItemInfo.GroupId);
				}
				dups = dups.Where(x => !blackList.Contains(x.ItemInfo.GroupId));
#if DEBUG
				itemsCount = dups.Count();
				System.Diagnostics.Trace.WriteLine($"Custom selection items count: {itemsCount}");
#endif
			}

			dups = dups.Where(x => {
				if (data.FileTypeSelection == 1 && x.ItemInfo.IsImage)
					return false;
				if (data.FileTypeSelection == 2 && !x.ItemInfo.IsImage)
					return false;
				long megaBytes = x.ItemInfo.SizeLong.BytesToMegaBytes();
				if (megaBytes < data.MinimumFileSize)
					return false;
				if (megaBytes > data.MaximumFileSize)
					return false;
				foreach (var item in data.PathContains) {
					if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(item, x.ItemInfo.Path))
						return false;
				}
				foreach (var item in data.PathNotContains) {
					if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(item, x.ItemInfo.Path))
						return false;
				}
				if (x.ItemInfo.Similarity < data.SimilarityFrom)
					return false;
				if (x.ItemInfo.Similarity > data.SimilarityTo)
					return false;

				return true;
			});
#if DEBUG
			itemsCount = dups.Count();
		 System.Diagnostics.Trace.WriteLine($"Custom selection items count: {itemsCount}");
#endif

			HashSet<Guid> blackListGroupID = new();
			foreach (var first in dups) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already

				var l = dups.Where(d => {
					if (d.ItemInfo.Path.Equals(first.ItemInfo.Path))
						return true;
					switch (data.IdenticalSelection) {
					case 1:
						return d.EqualsFull(first);
					case 2:
						return d.EqualsButSize(first);
					case 3:
						return !d.EqualsFull(first) || !d.EqualsButSize(first);
					default:
						return d.ItemInfo.GroupId == first.ItemInfo.GroupId;
					}
				});

				var dupMods = l as List<DuplicateItemVM> ?? l.ToList();
				if (dupMods.Count == 0) continue;
				dupMods.Insert(0, first);
				switch (data.DateTimeSelection) {
				case 1:
					dupMods = dupMods.OrderBy(s => s.ItemInfo.DateCreated).ToList();
					break;
				case 2:
					dupMods = dupMods.OrderByDescending(s => s.ItemInfo.DateCreated).ToList();
					break;
				}
				dupMods[0].Checked = false;
				for (int i = 1; i < dupMods.Count; i++) {
					dupMods[i].Checked = true;
				}
				blackListGroupID.Add(first.ItemInfo.GroupId);

			}
		}
	}
}
