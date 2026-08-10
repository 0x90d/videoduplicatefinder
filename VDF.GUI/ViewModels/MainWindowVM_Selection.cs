// /*
//     Copyright (C) 2026 0x90d
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
using System.Text.Json;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

		public List<string> QualityCriteriaOrder {
			get => SettingsFile.Instance.QualityCriteriaOrder;
			set => SettingsFile.Instance.QualityCriteriaOrder = value;
		}

		// When more than one row is highlighted in the grid, scope Selection-menu
		// and checked-items commands (delete/remove/blacklist/symlink/dry-run) to
		// those rows; otherwise fall back to the full list.
		List<DuplicateItemVM> ScopedDuplicates() {
			var selected = GetSelectedDuplicates();
			return selected.Count > 1 ? selected : Duplicates.ToList();
		}

		public ReactiveCommand<Unit, Unit> OpenCustomSelectionCommand => ReactiveCommand.Create(() => {
			CustomSelectionView dlg = new(string.Empty);
			dlg.Show(ApplicationHelpers.MainWindow);
		});

		public ReactiveCommand<Unit, Unit> CheckCustomCommand => ReactiveCommand.CreateFromTask(async () => {
			ExpressionBuilder dlg = new();
			((ExpressionBuilderVM)dlg.DataContext!).ExpressionText = SettingsFile.Instance.LastCustomSelectExpression;
			bool res = await dlg.ShowDialog<bool>(ApplicationHelpers.MainWindow);
			if (!res) return;

			var expression = ((ExpressionBuilderVM)dlg.DataContext).ExpressionText;
			SettingsFile.Instance.LastCustomSelectExpression = expression;
			UpdateExpressionHistory(expression);
			await ApplySelectionExpression(expression);
		});

		/// <summary>
		/// Applies a saved Expression Builder preset straight from the Auto-select menu,
		/// without opening the dialog (#850). Runs the same pipeline as the dialog's OK.
		/// </summary>
		public ReactiveCommand<Data.ExpressionPreset, Unit> ApplyExpressionPresetCommand =>
			ReactiveCommand.CreateFromTask<Data.ExpressionPreset>(async preset => {
				if (preset == null || string.IsNullOrWhiteSpace(preset.Expression)) return;
				SettingsFile.Instance.LastCustomSelectExpression = preset.Expression;
				UpdateExpressionHistory(preset.Expression);
				await ApplySelectionExpression(preset.Expression);
			});

		/// <summary>
		/// Compile-and-check pipeline shared by the Expression Builder dialog and the
		/// saved-preset menu (#850): checks every matching item, asking once how to treat
		/// groups where ALL members match (checking a whole group marks it for deletion).
		/// </summary>
		internal async Task ApplySelectionExpression(string expression) {
			Func<DuplicateItem, bool> interpreter;
			try {
				interpreter = Utils.SelectionExpression.Compile(expression);
			}
			catch (Exception ex) {
				await MessageBoxService.Show($"Expression error: {ex.Message}");
				return;
			}

			var groups = ScopedDuplicates()
							.Where(d => d.IsVisibleInFilter)
							.GroupBy(d => d.ItemInfo.GroupId)
							.Select(g => g.ToList())
							.ToList();

			var (partialMatches, fullGroups) = PartitionExpressionMatches(groups, interpreter);

			bool includeFullGroups = false;
			if (fullGroups.Count > 0) {
				var examplePath = fullGroups[0][0].ItemInfo.Path;
				var message = $"There are groups where all items match your expression, for example '{examplePath}'.{Environment.NewLine}{Environment.NewLine}Do you want to have all items checked (Yes)? Or do you want to have NO items in these groups checked (No)?";

				var dialogResult = await MessageBoxService.Show(message, MessageBoxButtons.Yes | MessageBoxButtons.No);
				includeFullGroups = dialogResult == MessageBoxButtons.Yes;
			}

			using var undoBatch = BeginSelectionUndoBatch();
			foreach (var dup in partialMatches)
				dup.Checked = true;
			if (includeFullGroups)
				foreach (var group in fullGroups)
					foreach (var dup in group)
						dup.Checked = true;
		}

		/// <summary>
		/// Pure partition of the expression's matches: items from groups where only SOME
		/// members match are checked unconditionally; groups where ALL members match are
		/// returned separately so the caller can apply the ask-once policy.
		/// </summary>
		internal static (List<DuplicateItemVM> PartialMatches, List<List<DuplicateItemVM>> FullGroups) PartitionExpressionMatches(
			IReadOnlyList<List<DuplicateItemVM>> groups, Func<DuplicateItem, bool> interpreter) {
			var evaluated = groups
							.AsParallel()
							.AsOrdered()
							.Select(group => (Items: group, Matches: group.Where(item => interpreter(item.ItemInfo)).ToList()))
							.ToList();
			var partial = new List<DuplicateItemVM>();
			var full = new List<List<DuplicateItemVM>>();
			foreach (var (items, matches) in evaluated) {
				if (matches.Count == 0)
					continue;
				if (matches.Count == items.Count)
					full.Add(matches);
				else
					partial.AddRange(matches);
			}
			return (partial, full);
		}

		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalCommand => ReactiveCommand.Create(() => {
			using var undoBatch = BeginSelectionUndoBatch();
			ForEachGroupCluster(ScopedDuplicates(), (d, first) => d.EqualsFull(first), (first, cluster) => {
				foreach (var dup in cluster)
					dup.Checked = true;
				first.Checked = false;
			});
		});

		public ReactiveCommand<Unit, Unit> CheckWhenIdenticalButSizeCommand => ReactiveCommand.Create(() => {
			using var undoBatch = BeginSelectionUndoBatch();
			ForEachGroupCluster(ScopedDuplicates(), (d, first) => d.EqualsButQuality(first), (first, cluster) => {
				cluster.Add(first);
				cluster = cluster.OrderBy(s => s.ItemInfo.SizeLong).ToList();
				cluster[0].Checked = false;
				for (int i = 1; i < cluster.Count; i++)
					cluster[i].Checked = true;
			});
		});

		public ReactiveCommand<Unit, Unit> CheckOldestCommand => ReactiveCommand.Create(() => {
			using var undoBatch = BeginSelectionUndoBatch();
			ForEachGroupCluster(ScopedDuplicates(), (d, first) => d.EqualsButQuality(first), (first, cluster) => {
				cluster.Add(first);
				cluster = cluster.OrderByDescending(s => s.ItemInfo.DateCreated).ToList();
				cluster[0].Checked = false;
				for (int i = 1; i < cluster.Count; i++)
					cluster[i].Checked = true;
			});
		});

		public ReactiveCommand<Unit, Unit> CheckNewestCommand => ReactiveCommand.Create(() => {
			using var undoBatch = BeginSelectionUndoBatch();
			ForEachGroupCluster(ScopedDuplicates(), (d, first) => d.EqualsButQuality(first), (first, cluster) => {
				cluster.Add(first);
				cluster = cluster.OrderBy(s => s.ItemInfo.DateCreated).ToList();
				cluster[0].Checked = false;
				for (int i = 1; i < cluster.Count; i++)
					cluster[i].Checked = true;
			});
		});

		public ReactiveCommand<Unit, Unit> CheckLowestQualityCommand => ReactiveCommand.CreateFromTask(async () => {
			var dlg = new QualityOrderDialog();
			var result = await dlg.ShowDialog<List<string>>(ApplicationHelpers.MainWindow);
			if (result == null || result.Count == 0) return;
			QualityCriteriaOrder = result;

			using var undoBatch = BeginSelectionUndoBatch();
			var criteria = ResolveCriteria(QualityCriteriaOrder);
			ForEachGroupCluster(ScopedDuplicates(), (d, first) => d.EqualsButQuality(first), (first, cluster) => {
				cluster.Insert(0, first);

				var keep = VDF.Core.Utils.QualityRanker.PickKeeper(
					cluster,
					criteria,
					d => d.ItemInfo.IsImage);

				keep.Checked = false;
				for (int i = 0; i < cluster.Count; i++)
					if (cluster[i].ItemInfo.Path != keep.ItemInfo.Path)
						cluster[i].Checked = true;
			});
		});

		public ReactiveCommand<Unit, Unit> CheckMissingFilesCommand => ReactiveCommand.Create(() => {
			using var undoBatch = BeginSelectionUndoBatch();
			foreach (var item in ScopedDuplicates().Where(d => d.IsVisibleInFilter))
				if (!File.Exists(item.ItemInfo.Path))
					item.Checked = true;
		});

		public ReactiveCommand<DuplicateItemVM, Unit> RemoveSingleItemCommand => ReactiveCommand.Create<DuplicateItemVM>(item => {
			if (item == null) return;
			Duplicates.Remove(item);
			RefreshResultsView();
			RefreshGroupStats();
		});

		public ReactiveCommand<Unit, Unit> ClearCheckedItemsCommand => ReactiveCommand.Create(() => {
			using var undoBatch = BeginSelectionUndoBatch();
			foreach (var item in ScopedDuplicates().Where(d => d.IsVisibleInFilter))
				item.Checked = false;
		});

		public ReactiveCommand<Unit, Unit> InvertCheckedItemsCommand => ReactiveCommand.Create(() => {
			using var undoBatch = BeginSelectionUndoBatch();
			foreach (var item in ScopedDuplicates().Where(d => d.IsVisibleInFilter))
				item.Checked = !item.Checked;
		});

		public ReactiveCommand<Unit, Unit> DeleteHighlightedCommand => ReactiveCommand.Create(() => {
			var selected = GetSelectedDuplicates();
			if (selected.Count == 0) return;
			foreach (var item in selected)
				Duplicates.Remove(item);
			RefreshGroupStats();
			RefreshResultsView();
		});

		public ReactiveCommand<Unit, Unit> DeleteCheckedItemsWithPromptCommand => ReactiveCommand.CreateFromTask(async () => {
			var doDelete = CheckedItemsToDelete;
			if (doDelete.Count == 0) {
				await MessageBoxService.Show(App.Lang["Message.NoMatchingDuplicates"]);
				return;
			}
			MessageBoxButtons? dlgResult = await MessageBoxService.Show(App.Lang["Message.DeleteFromDiskPrompt"],
				MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel,
				defaultButton: MessageBoxButtons.Cancel);
			if (dlgResult == MessageBoxButtons.Yes)
#pragma warning disable CS4014
				Dispatcher.UIThread.InvokeAsync(() => {
					DeleteInternal(fromDisk: true, toDelete: doDelete);
				});
			else if (dlgResult == MessageBoxButtons.No)
				Dispatcher.UIThread.InvokeAsync(() => {
					DeleteInternal(fromDisk: false, toDelete: doDelete);
				});
#pragma warning restore CS4014
		});

		public ReactiveCommand<Unit, Unit> DeleteCheckedItemsCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: true);
			});
		});

		public ReactiveCommand<Unit, Unit> DeleteCheckedItemsPermanentlyCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: true, permanently: true);
			});
		});

		public ReactiveCommand<Unit, Unit> RemoveCheckedItemsFromListCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: false);
			});
		});

		public ReactiveCommand<Unit, Unit> RemoveCheckedItemsFromListAndBlacklistCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: false, blackList: true);
			});
		});

		public ReactiveCommand<Unit, Unit> CreateSymbolLinksForCheckedItemsCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: false, blackList: false, createSymbolLinksInstead: true);
			});
		});

		public ReactiveCommand<Unit, Unit> CreateSymbolLinksForCheckedItemsAndBlacklistCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: false, blackList: true, createSymbolLinksInstead: true);
			});
		});

		public ReactiveCommand<Unit, Unit> CreateHardLinksForCheckedItemsCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: false, blackList: false, createHardLinksInstead: true);
			});
		});

		public ReactiveCommand<Unit, Unit> CreateHardLinksForCheckedItemsAndBlacklistCommand => ReactiveCommand.Create(() => {
			Dispatcher.UIThread.InvokeAsync(() => {
				DeleteInternal(fromDisk: false, blackList: true, createHardLinksInstead: true);
			});
		});

		public ReactiveCommand<Unit, Unit> ExportCheckedItemsCleanupDryRunReportCommand => ReactiveCommand.CreateFromTask(async () => {
			var toDelete = CheckedItemsToDelete;
			if (toDelete.Count == 0) {
				await MessageBoxService.Show(App.Lang["Message.NoMatchingDuplicates"]);
				return;
			}

			var report = BuildCleanupDryRunReport(toDelete);
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions {
				DefaultExtension = ".json",
				FileTypeChoices = new FilePickerFileType[] {
					new FilePickerFileType(App.Lang["CleanupDryRun.FileType"]) { Patterns = new[] { "*.json" } }
				}
			});
			if (string.IsNullOrEmpty(result)) return;
			var json = JsonSerializer.Serialize(report, GuiJsonPrettyContext.Default.CleanupDryRunReport);
			File.WriteAllText(result, json);
			await MessageBoxService.Show(App.Lang["Message.CleanupDryRunSaved"]);
		});

		public ReactiveCommand<Unit, Unit> CopyCheckedItemsCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					Title = App.Lang["Dialog.SelectFolder"]
				});

			if (result == null || result.Count == 0) return;

			var selectedItems = Duplicates.Where(s => s.Checked).ToList();
			if (selectedItems.Count == 0) return;

			IsBusy = true;
			IsBusyOverlayText = string.Format(App.Lang["Busy.Copying"], 0, selectedItems.Count);
			int errorCounter;
			var renames = new List<(DuplicateItemVM Item, string NewPath)>();
			try {
				errorCounter = await Task.Run(() =>
					Utils.FileUtils.CopyFile(selectedItems, result[0], true, false, renames,
						(done, total) => Dispatcher.UIThread.Post(() =>
							IsBusyOverlayText = string.Format(App.Lang["Busy.Copying"], done, total))));
			}
			finally {
				IsBusy = false;
			}
			foreach (var (item, newPath) in renames)
				item.ItemInfo.Path = newPath;
			if (errorCounter > 0)
				await MessageBoxService.Show(App.Lang["Message.CopyFailed"]);
		});

		public ReactiveCommand<Unit, Unit> MoveCheckedItemsCommand => ReactiveCommand.CreateFromTask(
			() => MoveItemsToPickedFolderAsync(() => Duplicates.Where(s => s.Checked).ToList()));

		// Row context menu: moves the highlighted row(s), independent of the checkboxes.
		// The file being relocated is typically the KEEPER - exactly the one you would
		// never check, since checked means "marked for deletion" (#843).
		public ReactiveCommand<Unit, Unit> MoveSelectedItemsCommand => ReactiveCommand.CreateFromTask(
			() => MoveItemsToPickedFolderAsync(GetSelectedDuplicates));

		/// <summary>
		/// Folder picker, then moves <paramref name="itemsProvider"/>'s files there with
		/// database path tracking (same "Set location" semantics as the checked-items
		/// move; the rows keep pointing at the new paths).
		/// </summary>
		async Task MoveItemsToPickedFolderAsync(Func<List<DuplicateItemVM>> itemsProvider) {
			var result = await Utils.PickerDialogUtils.OpenDialogPicker(
				new FolderPickerOpenOptions() {
					Title = App.Lang["Dialog.SelectFolder"]
				});

			if (result == null || result.Count == 0) return;

			var selectedItems = itemsProvider();
			if (selectedItems.Count == 0) return;

			IsBusy = true;
			IsBusyOverlayText = string.Format(App.Lang["Busy.Moving"], 0, selectedItems.Count);
			int errorCounter;
			var renames = new List<(DuplicateItemVM Item, string NewPath)>();
			try {
				errorCounter = await Task.Run(() => {
					// Database entries must be resolved by the OLD path, before the move.
					var dbEntries = new Dictionary<DuplicateItemVM, FileEntry>(ReferenceEqualityComparer<DuplicateItemVM>.Instance);
					foreach (var item in selectedItems) {
						if (ScanEngine.GetFromDatabase(item.ItemInfo.Path, out var dbEntry))
							dbEntries[item] = dbEntry!;
					}
					int errors = Utils.FileUtils.CopyFile(selectedItems, result[0], true, true, renames,
						(done, total) => Dispatcher.UIThread.Post(() =>
							IsBusyOverlayText = string.Format(App.Lang["Busy.Moving"], done, total)));
					foreach (var (item, newPath) in renames)
						if (dbEntries.TryGetValue(item, out var entry))
							ScanEngine.UpdateFilePathInDatabase(newPath, entry);
					ScanEngine.SaveDatabase();
					return errors;
				});
			}
			finally {
				IsBusy = false;
			}
			foreach (var (item, newPath) in renames)
				item.ItemInfo.Path = newPath;
			if (errorCounter > 0)
				await MessageBoxService.Show(App.Lang["Message.MoveFailed"]);
		}

		internal void RunCustomSelection(CustomSelectionData data) {
			var visibleItems = ScopedDuplicates().Where(x => x.IsVisibleInFilter).ToList();
			var plan = ComputeCustomSelection(visibleItems, data);

			using var undoBatch = BeginSelectionUndoBatch();
			foreach (var keeper in plan.Keepers)
				keeper.Checked = false;
			foreach (var item in plan.ToCheck)
				item.Checked = true;
		}

		/// <summary>
		/// Pure selection planner for the Custom Selection dialog. One pass over the
		/// visible items, then a per-group decision - replaces a shape that re-ran the
		/// whole filter chain (wildcard match on every path included) once per group,
		/// which froze the UI for hours on six-digit result lists (#864).
		/// Keeper semantics: with a date rule, the oldest/newest of each matched cluster
		/// is kept unchecked. Without one, identity modes keep the reference item of the
		/// cluster, and "Any" keeps the first matched item when the whole visible group
		/// matched - when the criteria matched only a subset, all matched items are
		/// checked and the unmatched members are the survivors. (The reference item used
		/// to be inserted twice and re-checked by the i&gt;=1 loop, so the intended
		/// keeper was checked along with everything else.)
		/// </summary>
		internal static CustomSelectionPlan ComputeCustomSelection(IReadOnlyList<DuplicateItemVM> visibleItems, CustomSelectionData data) {
			HashSet<Guid>? groupsWithChecked = null;
			if (data.IgnoreGroupsWithCheckedItems) {
				groupsWithChecked = new();
				foreach (var item in visibleItems)
					if (item.Checked)
						groupsWithChecked.Add(item.ItemInfo.GroupId);
			}

			// Escaped once per pattern, not once per item; see MatchesPathPattern.
			string[] pathContains = data.PathContains.Select(EscapeWildcardBackslashes).ToArray();
			string[] pathNotContains = data.PathNotContains.Select(EscapeWildcardBackslashes).ToArray();

			Dictionary<Guid, int> visibleGroupSize = new();
			foreach (var item in visibleItems) {
				visibleGroupSize.TryGetValue(item.ItemInfo.GroupId, out int size);
				visibleGroupSize[item.ItemInfo.GroupId] = size + 1;
			}

			Dictionary<Guid, List<DuplicateItemVM>> matchedByGroup = new();
			List<Guid> groupOrder = new();
			foreach (var item in visibleItems) {
				if (groupsWithChecked != null && groupsWithChecked.Contains(item.ItemInfo.GroupId))
					continue;
				if (!MatchesCriteria(item, data, pathContains, pathNotContains))
					continue;
				if (!matchedByGroup.TryGetValue(item.ItemInfo.GroupId, out var groupItems)) {
					matchedByGroup[item.ItemInfo.GroupId] = groupItems = new();
					groupOrder.Add(item.ItemInfo.GroupId);
				}
				groupItems.Add(item);
			}

			var plan = new CustomSelectionPlan(new(), new());
			foreach (var groupId in groupOrder) {
				var members = matchedByGroup[groupId];
				var first = members[0];
				List<DuplicateItemVM> cluster = data.IdenticalSelection switch {
					1 => members.Where(m => ReferenceEquals(m, first) || m.EqualsFull(first)).ToList(),
					2 => members.Where(m => ReferenceEquals(m, first) || m.EqualsButSize(first)).ToList(),
					// "Not identical": group members that aren't a 100% clone of the
					// reference item. This used to leak across groups, because every
					// item of every OTHER group also isn't identical to the reference.
					3 => members.Where(m => ReferenceEquals(m, first) || !m.EqualsFull(first)).ToList(),
					_ => members,
				};

				switch (data.DateTimeSelection) {
				case 1: // check the newest copies, keep the oldest
					cluster = cluster.OrderBy(s => s.ItemInfo.DateCreated).ToList();
					break;
				case 2: // check the oldest copies, keep the newest
					cluster = cluster.OrderByDescending(s => s.ItemInfo.DateCreated).ToList();
					break;
				default:
					if (data.IdenticalSelection == 0 && cluster.Count < visibleGroupSize[groupId]) {
						// Partial match: the unmatched members are the keepers. A group
						// whose only matching member was the filter target used to
						// select nothing here, which was the 2024 single-match report.
						// Only for "Any" - with an identity criterion the cluster is
						// narrowed by design and one of it must survive.
						plan.ToCheck.AddRange(cluster);
						continue;
					}
					break;
				}

				plan.Keepers.Add(cluster[0]);
				for (int i = 1; i < cluster.Count; i++)
					plan.ToCheck.Add(cluster[i]);
			}
			return plan;
		}

		static bool MatchesCriteria(DuplicateItemVM item, CustomSelectionData data, string[] pathContains, string[] pathNotContains) {
			if (data.FileTypeSelection == 1 && item.ItemInfo.IsImage)
				return false;
			if (data.FileTypeSelection == 2 && !item.ItemInfo.IsImage)
				return false;
			long megaBytes = item.ItemInfo.SizeLong.BytesToMegaBytes();
			if (megaBytes < data.MinimumFileSize)
				return false;
			if (megaBytes > data.MaximumFileSize)
				return false;
			foreach (var pattern in pathContains)
				if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, item.ItemInfo.Path))
					return false;
			foreach (var pattern in pathNotContains)
				if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, item.ItemInfo.Path))
					return false;
			if (item.ItemInfo.Similarity < data.SimilarityFrom)
				return false;
			if (item.ItemInfo.Similarity > data.SimilarityTo)
				return false;
			return true;
		}

		/// <summary>
		/// Shared engine of the classic per-group selection commands, grouped by GroupId
		/// so the cluster search stays inside one group (every Equals* variant requires an
		/// equal GroupId anyway). The previous shape re-scanned the entire results list
		/// per item, freezing the UI for hours on six-digit result lists (#864).
		/// Per group, the first member (list order) with a non-empty cluster wins and the
		/// rest of the group is skipped - the same one-cluster-per-group behavior the old
		/// group blacklist produced. Cluster candidates must be visible in the current
		/// filter; the reference item itself is excluded by path, as before.
		/// </summary>
		internal static void ForEachGroupCluster(IEnumerable<DuplicateItemVM> scoped,
				Func<DuplicateItemVM, DuplicateItemVM, bool> belongsToCluster,
				Action<DuplicateItemVM, List<DuplicateItemVM>> apply) {
			foreach (var group in scoped.GroupBy(d => d.ItemInfo.GroupId)) {
				var members = group.ToList();
				foreach (var first in members) {
					var cluster = new List<DuplicateItemVM>();
					foreach (var d in members)
						if (d.IsVisibleInFilter && belongsToCluster(d, first) && !d.ItemInfo.Path.Equals(first.ItemInfo.Path))
							cluster.Add(d);
					if (cluster.Count == 0) continue;
					apply(first, cluster);
					break;
				}
			}
		}

		CleanupDryRunReport BuildCleanupDryRunReport(IReadOnlyList<DuplicateItemVM> toDelete) {
			var itemsByGroup = toDelete.GroupBy(x => x.ItemInfo.GroupId).ToList();
			var groups = new List<CleanupDryRunGroup>();

			foreach (var group in itemsByGroup) {
				var allGroupItems = Duplicates.Where(d => d.ItemInfo.GroupId == group.Key).ToList();
				var keepItems = allGroupItems.Except(group).ToList();
				long savings = group.Sum(item => item.ItemInfo.SizeLong);
				groups.Add(new CleanupDryRunGroup {
					GroupId = group.Key,
					EstimatedSavingsBytes = savings,
					Reason = App.Lang["CleanupDryRun.Reason.Manual"],
					RemoveItems = group.Select(item => new CleanupDryRunItem {
						Path = item.ItemInfo.Path,
						SizeBytes = item.ItemInfo.SizeLong,
						Resolution = item.ItemInfo.FrameSize ?? string.Empty,
						DateCreated = item.ItemInfo.DateCreated
					}).ToList(),
					KeepItems = keepItems.Select(item => new CleanupDryRunItem {
						Path = item.ItemInfo.Path,
						SizeBytes = item.ItemInfo.SizeLong,
						Resolution = item.ItemInfo.FrameSize ?? string.Empty,
						DateCreated = item.ItemInfo.DateCreated
					}).ToList()
				});
			}

			return new CleanupDryRunReport {
				CreatedAt = DateTime.UtcNow,
				EstimatedTotalSavingsBytes = groups.Sum(g => g.EstimatedSavingsBytes),
				Groups = groups
			};
		}

		static void UpdateExpressionHistory(string expression) {
			if (string.IsNullOrWhiteSpace(expression))
				return;
			var history = SettingsFile.Instance.ExpressionHistory;
			if (history.Contains(expression))
				history.Remove(expression);
			history.Insert(0, expression);
		}

	}

	/// <summary>Planned Custom Selection changes: keepers get unchecked, the rest checked.</summary>
	internal sealed record CustomSelectionPlan(List<DuplicateItemVM> Keepers, List<DuplicateItemVM> ToCheck);

	// Top-level (not nested in MainWindowVM) so the source-generated JSON context
	// can reference them.
	internal sealed class CleanupDryRunReport {
		public DateTime CreatedAt { get; set; }
		public long EstimatedTotalSavingsBytes { get; set; }
		public List<CleanupDryRunGroup> Groups { get; set; } = new();
	}

	internal sealed class CleanupDryRunGroup {
		public Guid GroupId { get; set; }
		public long EstimatedSavingsBytes { get; set; }
		public string Reason { get; set; } = string.Empty;
		public List<CleanupDryRunItem> RemoveItems { get; set; } = new();
		public List<CleanupDryRunItem> KeepItems { get; set; } = new();
	}

	internal sealed class CleanupDryRunItem {
		public string Path { get; set; } = string.Empty;
		public long SizeBytes { get; set; }
		public string Resolution { get; set; } = string.Empty;
		public DateTime DateCreated { get; set; }
	}
}
