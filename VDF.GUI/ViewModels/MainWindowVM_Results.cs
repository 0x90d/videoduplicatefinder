// /*
//     Copyright (C) 2026 0x90d
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
using Avalonia.Collections;
using Avalonia.Input.Platform;
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public sealed record ResultsSortOption(string Name, ResultsSortMode Mode);

	// Flattened results view (redesign Stage 1; the classic DataGrid view was retired
	// in Stage 6).
	public partial class MainWindowVM : ReactiveObject {

		/// <summary>Flattened list the results ListBox renders: ResultsGroupHeader + ResultsItemRow.</summary>
		public AvaloniaList<object> ResultsRows { get; } = new();

		readonly HashSet<Guid> collapsedResultsGroups = new();
		readonly HashSet<DuplicateItemVM> expandedResultsDetails = new();
		/// <summary>Groups of the last build, in display order (for navigation).</summary>
		List<ResultsGroupHeader> resultsGroups = new();
		bool resultsHavePartialClips;

		/// <summary>
		/// The Clip offset column only exists when it can carry data: partial-clip
		/// detection is enabled, or the current results (e.g. an imported scan from a
		/// machine where it was enabled) actually contain partial clips.
		/// </summary>
		public bool ResultsShowClipOffsetColumn =>
			SettingsFile.Instance.EnablePartialClipDetection || resultsHavePartialClips;

		// Seams the new results view control wires up when it attaches; the VM stays
		// ignorant of the concrete ListBox.
		internal Func<List<DuplicateItemVM>>? NewResultsSelectionProvider;
		internal Action<ResultsItemRow>? NewResultsSelectAndScrollTo;

		public ResultsSortOption[] ResultsSortOptions { get; } = {
			new(App.Lang["Results.Sort.WastedSpace"], ResultsSortMode.WastedSpace),
			new(App.Lang["Results.Sort.TotalSize"], ResultsSortMode.TotalSize),
			new(App.Lang["Results.Sort.LargestFile"], ResultsSortMode.LargestFile),
			new(App.Lang["Results.Sort.FileCount"], ResultsSortMode.FileCount),
			new(App.Lang["Results.Sort.Similarity"], ResultsSortMode.Similarity),
			new(App.Lang["Results.Sort.DateCreated"], ResultsSortMode.DateCreated),
			new(App.Lang["Results.Sort.Duration"], ResultsSortMode.Duration),
			new(App.Lang["Results.Sort.FolderPath"], ResultsSortMode.FolderPath),
			new(App.Lang["Results.Sort.GroupsWithChecked"], ResultsSortMode.GroupsWithCheckedItems),
		};

		public ResultsSortOption SelectedResultsSort {
			get => ResultsSortOptions.FirstOrDefault(o => o.Mode == SettingsFile.Instance.ResultsSortMode) ?? ResultsSortOptions[0];
			set {
				if (value == null || value.Mode == SettingsFile.Instance.ResultsSortMode) return;
				SettingsFile.Instance.ResultsSortMode = value.Mode;
				this.RaisePropertyChanged(nameof(SelectedResultsSort));
				RebuildResultsList();
			}
		}

		public bool ResultsSortDescending {
			get => SettingsFile.Instance.ResultsSortDescending;
			set {
				if (value == SettingsFile.Instance.ResultsSortDescending) return;
				SettingsFile.Instance.ResultsSortDescending = value;
				this.RaisePropertyChanged(nameof(ResultsSortDescending));
				RebuildResultsList();
			}
		}

		/// <summary>
		/// BEST badge tooltip: names the criterion that produced the winner — the ranking
		/// looked arbitrary without it (#839) — and points to where the order is configured.
		/// Null criterion = the group stayed effectively tied through every criterion.
		/// </summary>
		internal static string BestBadgeTooltip(VDF.Core.Utils.QualityRanker.Criterion<DuplicateItemVM>? decidedBy) =>
			decidedBy == null
				? App.Lang["Results.Row.BestTipTied"]
				: string.Format(App.Lang["Results.Row.BestTip"], App.Lang[$"QualityCriteria.{decidedBy.Name}"]);

		GroupSummaryFormats BuildGroupSummaryFormats() => new() {
			GroupTitle = App.Lang["Results.GroupTitle"],
			Files = App.Lang["Results.Summary.Files"],
			SingleFile = App.Lang["Results.Summary.SingleFile"],
			SaveUpTo = App.Lang["Results.Summary.SaveUpTo"],
			OnDisk = App.Lang["Results.Summary.OnDisk"],
			PreviouslyDeleted = App.Lang["Results.Summary.PreviouslyDeleted"],
		};

		/// <summary>Rebuilds the flattened list from the current duplicates, filter and sort.</summary>
		internal void RebuildResultsList() {
			var result = ResultsListBuilder.Build(new ResultsBuildRequest {
				Items = Duplicates.ToList(),
				Filter = DuplicatesFilterCore,
				SortMode = SettingsFile.Instance.ResultsSortMode,
				SortDescending = SettingsFile.Instance.ResultsSortDescending,
				CollapsedGroups = collapsedResultsGroups,
				ExpandedDetails = expandedResultsDetails,
				PickBest = members => {
					var (keep, decidedBy) = VDF.Core.Utils.QualityRanker.PickKeeperWithReason(
						members.ToList(), ResolveCriteria(QualityCriteriaOrder), d => d.ItemInfo.IsImage);
					return (keep, BestBadgeTooltip(decidedBy));
				},
				Formats = BuildGroupSummaryFormats(),
			});
			resultsGroups = result.Groups;
			resultsHavePartialClips = result.HasPartialClips;
			ResultsRows.Clear();
			ResultsRows.AddRange(result.Rows);
			this.RaisePropertyChanged(nameof(ResultsShowClipOffsetColumn));
		}

		/// <summary>Refreshes the results list after filter/sort/list changes.</summary>
		internal void RefreshResultsView() => RebuildResultsList();

		/// <summary>Builds the results list from scratch (scan done, import).</summary>
		void BuildActiveResultsView(bool resetSessionStats = true) {
			RebuildResultsList();
			if (resetSessionStats)
				TotalSizeRemovedInternal = 0;
		}

		public ReactiveCommand<ResultsGroupHeader, Unit> ToggleGroupCollapsedCommand => ReactiveCommand.Create<ResultsGroupHeader>(header => {
			if (header == null) return;
			if (!collapsedResultsGroups.Remove(header.GroupId))
				collapsedResultsGroups.Add(header.GroupId);
			RebuildResultsList();
		});

		/// <summary>Expands/collapses the per-file details panel under a row (Tier 2 metadata).</summary>
		public ReactiveCommand<DuplicateItemVM, Unit> ToggleItemDetailsCommand => ReactiveCommand.Create<DuplicateItemVM>(item => {
			if (item == null) return;
			if (!expandedResultsDetails.Remove(item))
				expandedResultsDetails.Add(item);
			RebuildResultsList();
		});

		public ReactiveCommand<DuplicateItemVM, Unit> CopyItemDetailsCommand => ReactiveCommand.CreateFromTask<DuplicateItemVM>(async item => {
			if (item == null) return;
			if (ApplicationHelpers.MainWindow.Clipboard is { } clipboard)
				await clipboard.SetTextAsync(ResultsBadgeRules.BuildDetailsText(item.ItemInfo));
		});

		public ReactiveCommand<Unit, Unit> DismissResultsHintCommand => ReactiveCommand.Create(() => {
			SettingsFile.Instance.ResultsHintDismissed = true;
		});

		public ReactiveCommand<ResultsGroupHeader, Unit> CompareGroupHeaderCommand => ReactiveCommand.Create<ResultsGroupHeader>(header => {
			if (header != null) CompareGroup(header.GroupId);
		});

		public ReactiveCommand<ResultsGroupHeader, Unit> KeepBestInGroupHeaderCommand => ReactiveCommand.Create<ResultsGroupHeader>(header => {
			if (header != null) KeepBestInGroup(header.GroupId);
		});

		public ReactiveCommand<ResultsGroupHeader, Unit> MarkGroupHeaderNotAMatchCommand => ReactiveCommand.CreateFromTask<ResultsGroupHeader>(async header => {
			if (header != null) await MarkGroupAsNotAMatch(header.GroupId);
		});

		public ReactiveCommand<ResultsGroupHeader, Unit> LoadThumbnailsForGroupHeaderCommand => ReactiveCommand.CreateFromTask<ResultsGroupHeader>(async header => {
			if (header == null) return;
			var items = Duplicates.Where(d => d.ItemInfo.GroupId == header.GroupId).Select(d => d.ItemInfo).ToList();
			if (items.Count == 0) return;
			SyncCoreSettings();
			await Scanner.RetrieveThumbnailsForItems(items);
		});

		public ReactiveCommand<Unit, Unit> ToggleResultsDensityCommand => ReactiveCommand.Create(() => {
			SettingsFile.Instance.ResultsCompactRows = !SettingsFile.Instance.ResultsCompactRows;
		});

		/// <summary>Group navigation for the flattened view.</summary>
		Guid? NavigateGroupNewView(bool forward, Guid? fromGroupId = null) {
			if (resultsGroups.Count == 0) return null;

			Guid? referenceGroupId = fromGroupId ?? GetSelectedDuplicateItem()?.ItemInfo.GroupId;
			int currentIndex = -1;
			if (referenceGroupId.HasValue)
				currentIndex = resultsGroups.FindIndex(g => g.GroupId == referenceGroupId.Value);

			int targetIndex = forward
				? (currentIndex + 1 < resultsGroups.Count ? currentIndex + 1 : 0)
				: (currentIndex - 1 >= 0 ? currentIndex - 1 : resultsGroups.Count - 1);

			var target = resultsGroups[targetIndex];
			if (target.IsCollapsed) {
				collapsedResultsGroups.Remove(target.GroupId);
				RebuildResultsList();
				target = resultsGroups.FirstOrDefault(g => g.GroupId == target.GroupId) ?? target;
			}
			var firstRow = target.Rows.FirstOrDefault();
			if (firstRow == null) return null;
			NewResultsSelectAndScrollTo?.Invoke(firstRow);
			return target.GroupId;
		}
	}
}
