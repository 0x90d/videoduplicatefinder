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

using System.Linq;
using System.Reactive;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already

				IEnumerable<DuplicateItemVM> l = Duplicates.Where(a => a.IsVisibleInFilter && a.ItemInfo.GroupId == first.ItemInfo.GroupId);
				IEnumerable<DuplicateItemVM> matches;

				try {
					var interpreter = new Interpreter().
						ParseAsDelegate<Func<DuplicateItem, bool>>(SettingsFile.Instance.LastCustomSelectExpression, shortIdentifier);
					matches = l.Where(arg => interpreter.Invoke(arg.ItemInfo));
				}
				catch (ParseException e) {
					await MessageBoxService.Show($"Failed to parse '{SettingsFile.Instance.LastCustomSelectExpression}': {e}");
					return;
				}

				if (!matches.Any()) continue;
				if (matches.Count() == l.Count()) {
					if (!userAsked) {
						MessageBoxButtons? result = await MessageBoxService.Show($"There are groups where all items match your expression, for example '{first.ItemInfo.Path}'.{Environment.NewLine}{Environment.NewLine}Do you want to have all items checked (Yes)? Or do you want to have NO items in these groups checked (No)?", MessageBoxButtons.Yes | MessageBoxButtons.No);
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
			HashSet<Guid> blackListGroupID = new();

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already

				var l = Duplicates.Where(d => d.IsVisibleInFilter && d.EqualsFull(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));

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

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				var l = Duplicates.Where(d => d.IsVisibleInFilter && d.EqualsButSize(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
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

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				var l = Duplicates.Where(d => d.IsVisibleInFilter && d.EqualsButSize(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
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

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				var l = Duplicates.Where(d => d.IsVisibleInFilter && d.EqualsButSize(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
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
		public ReactiveCommand<Unit, Unit> CheckLowestQualityCommand => ReactiveCommand.Create(() => {
			HashSet<Guid> blackListGroupID = new();

			foreach (var first in Duplicates) {
				if (blackListGroupID.Contains(first.ItemInfo.GroupId)) continue; //Dup has been handled already
				IEnumerable<DuplicateItemVM> l = Duplicates.Where(d => d.IsVisibleInFilter && d.EqualsButQuality(first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path));
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

			Utils.FileUtils.CopyFile(Duplicates.Where(s => s.Checked), result[0], true, false, out var errorCounter);
			if (errorCounter > 0)
				await MessageBoxService.Show("Failed to copy some files. Please check log!");
		});
		public ReactiveCommand<Unit, Unit> MoveSelectionCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					Title = "Select folder"
				});

			if (result == null || result.Count == 0) return;

			var selectedItems = Duplicates.Where(s => s.Checked).ToList();
			List<Tuple<DuplicateItemVM, FileEntry>> itemsToUpdate = new();
			foreach (var item in selectedItems) {
				ScanEngine.GetFromDatabase(item.ItemInfo.Path, out var dbEntry);
				itemsToUpdate.Add(Tuple.Create(item, dbEntry));
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

			IEnumerable<DuplicateItemVM> dups = Duplicates.Where(x => x.IsVisibleInFilter);
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
