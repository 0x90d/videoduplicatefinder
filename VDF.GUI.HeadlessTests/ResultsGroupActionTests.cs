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

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// #894 check a whole group, #909 hide groups with one file left, #879 the busy bar
/// showing real progress. Files that exist are real temp files; "Already deleted"
/// entries are paths in the same folder that do not exist.
/// </summary>
public sealed class ResultsGroupActionTests : IDisposable {
	readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "vdf-groups-" + Guid.NewGuid().ToString("N"))).FullName;

	public void Dispose() {
		try { Directory.Delete(folder, true); } catch { }
	}

	DuplicateItemVM Add(MainWindowVM vm, Guid group, string name, bool exists = true) {
		string path = Path.Combine(folder, name);
		if (exists)
			File.WriteAllBytes(path, new byte[16]);
		var item = new DuplicateItemVM(new DuplicateItem {
			Path = path, GroupId = group, Folder = folder, SizeLong = 16, Similarity = 100f,
			Duration = TimeSpan.FromSeconds(10), FrameSize = "1920x1080", FrameSizeInt = 1920 * 1080,
		});
		vm.Duplicates.Add(item);
		return item;
	}

	[Fact]
	public Task CheckAllInGroup_ChecksEveryFileOfThatGroupOnly() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		Guid group = Guid.NewGuid(), other = Guid.NewGuid();
		var a = Add(vm, group, "a.mp4");
		var b = Add(vm, group, "b.mp4");
		var c = Add(vm, group, "c.mp4");
		var tomb = Add(vm, group, "deleted.mp4", exists: false);
		var x = Add(vm, other, "x.mp4");
		var y = Add(vm, other, "y.mp4");
		vm.RebuildResultsList();

		var header = vm.ResultsRows.OfType<ResultsGroupHeader>().Single(h => h.GroupId == group);
		vm.CheckAllInGroupHeaderCommand.Execute(header).Subscribe();

		Assert.True(a.Checked && b.Checked && c.Checked);
		Assert.False(tomb.Checked); // nothing on disk to act on
		Assert.False(x.Checked || y.Checked);

		// One undo step restores the group.
		vm.UndoSelectionCommand.Execute().Subscribe();
		Assert.False(a.Checked || b.Checked || c.Checked);
	});

	[Fact]
	public Task HideGroupsWithOneFileLeft_HidesGroupsOfOneFileAndItsDeletedTwins() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		Guid survivor = Guid.NewGuid(), twoLeft = Guid.NewGuid();
		var live = Add(vm, survivor, "page001.jpg");
		Add(vm, survivor, "page001_deleted.jpg", exists: false);
		Add(vm, survivor, "page001_deleted_too.jpg", exists: false);
		Add(vm, twoLeft, "movie.mkv");
		Add(vm, twoLeft, "movie_copy.mkv");
		Add(vm, twoLeft, "movie_deleted.mkv", exists: false);
		vm.RebuildResultsList();
		Assert.Equal(2, vm.ResultsRows.OfType<ResultsGroupHeader>().Count());

		vm.FilterHideGroupsWithOneFileLeft = true;

		var header = Assert.Single(vm.ResultsRows.OfType<ResultsGroupHeader>());
		Assert.Equal(twoLeft, header.GroupId);
		// Hidden means out of reach of the checked-items actions as well.
		Assert.False(live.IsVisibleInFilter);

		// Deleting a file while the chip is on takes effect on the next rebuild.
		File.Delete(Path.Combine(folder, "movie_copy.mkv"));
		vm.RebuildResultsList();
		Assert.Empty(vm.ResultsRows.OfType<ResultsGroupHeader>());

		vm.FilterHideGroupsWithOneFileLeft = false;
		Assert.Equal(2, vm.ResultsRows.OfType<ResultsGroupHeader>().Count());
	});

	// #927: what the AI pass added beyond the classic comparison, whole groups at a time.
	[Fact]
	public Task OnlyGroupsWithAiMatches_ShowsWholeGroupsTheAiContributedTo() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		Guid classic = Guid.NewGuid(), ai = Guid.NewGuid(), mixed = Guid.NewGuid();
		var c1 = Add(vm, classic, "c1.mp4");
		Add(vm, classic, "c2.mp4");
		var partner = Add(vm, ai, "original.mp4");
		var cropped = Add(vm, ai, "cropped.mp4");
		cropped.ItemInfo.Flags = VDF.Core.DuplicateFlags.AiMatched;
		Add(vm, mixed, "m1.mp4");
		Add(vm, mixed, "m2.mp4");
		var mirrored = Add(vm, mixed, "m3_mirrored.mp4");
		mirrored.ItemInfo.Flags = VDF.Core.DuplicateFlags.AiMatched;
		vm.RebuildResultsList();
		Assert.True(vm.ResultsShowAiMatchFilter);
		Assert.Equal(3, vm.ResultsRows.OfType<ResultsGroupHeader>().Count());

		c1.Checked = true;
		vm.FilterOnlyGroupsWithAiMatches = true;

		var shown = vm.ResultsRows.OfType<ResultsGroupHeader>().Select(h => h.GroupId).ToHashSet();
		Assert.Equal(new HashSet<Guid> { ai, mixed }, shown);
		// The whole group stays: the AI-matched file is judged next to its partner.
		Assert.Equal(5, vm.ResultsRows.OfType<ResultsItemRow>().Count());
		Assert.True(partner.IsVisibleInFilter);
		// Hidden classic groups are out of reach of the checked-items actions.
		Assert.False(c1.IsVisibleInFilter);

		vm.FilterOnlyGroupsWithAiMatches = false;
		Assert.Equal(3, vm.ResultsRows.OfType<ResultsGroupHeader>().Count());
		Assert.True(c1.IsVisibleInFilter);
	});

	[Fact]
	public Task AiMatchChip_OnlyExistsWhenThereIsSomethingToFilter() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		Guid group = Guid.NewGuid();
		Add(vm, group, "a.mp4");
		var b = Add(vm, group, "b.mp4");
		vm.RebuildResultsList();
		var window = HeadlessUi.Show(new VDF.GUI.Views.DuplicateResultsView { DataContext = vm });
		try {
			ToggleButton Chip() => window.GetVisualDescendants().OfType<ToggleButton>()
				.Single(t => (t.Content as string) == "Only groups with AI matches");

			Assert.False(vm.ResultsShowAiMatchFilter); // a scan without AI: nothing to show
			Assert.False(Chip().IsVisible);

			b.ItemInfo.Flags = VDF.Core.DuplicateFlags.AiMatched;
			vm.RebuildResultsList();
			HeadlessUi.Pump();
			Assert.True(Chip().IsVisible);

			// Switched on, then the AI match leaves the results: the chip stays so it can be
			// switched off, instead of leaving an empty list with no visible cause.
			vm.FilterOnlyGroupsWithAiMatches = true;
			vm.Duplicates.Remove(b);
			vm.RebuildResultsList();
			HeadlessUi.Pump();
			Assert.Empty(vm.ResultsRows.OfType<ResultsGroupHeader>());
			Assert.True(Chip().IsVisible);
			vm.FilterOnlyGroupsWithAiMatches = false;
			HeadlessUi.Pump();
			Assert.False(Chip().IsVisible);
		}
		finally { window.Close(); }
	});

	[Fact]
	public void GroupsWithAiMatches_AreThoseHoldingAFlaggedFile() {
		Guid ai = Guid.NewGuid(), partial = Guid.NewGuid(), plain = Guid.NewGuid();
		var items = new[] {
			new DuplicateItemVM(new DuplicateItem { Path = "a", GroupId = ai, Flags = VDF.Core.DuplicateFlags.AiMatched | VDF.Core.DuplicateFlags.Flipped }),
			new DuplicateItemVM(new DuplicateItem { Path = "b", GroupId = ai }),
			// The AI partial-clip pass flags its clips the same way.
			new DuplicateItemVM(new DuplicateItem { Path = "c", GroupId = partial, Flags = VDF.Core.DuplicateFlags.PartialClip | VDF.Core.DuplicateFlags.AiMatched }),
			new DuplicateItemVM(new DuplicateItem { Path = "d", GroupId = plain, Flags = VDF.Core.DuplicateFlags.PartialClip }),
		};
		Assert.Equal(new HashSet<Guid> { ai, partial }, MainWindowVM.GroupsWithAiMatches(items));
	}

	[Fact]
	public void OfflineFilesCountAsFilesLeft() {
		Guid group = Guid.NewGuid();
		var items = new[] {
			new DuplicateItemVM(new DuplicateItem { Path = "a", GroupId = group }),
			new DuplicateItemVM(new DuplicateItem { Path = "b", GroupId = group }),
		};
		// Neither is a tombstone (b could be on an unplugged drive): two files left.
		Assert.Empty(MainWindowVM.GroupsWithAtMostOneFileLeft(items, _ => false));
		Assert.Equal(new[] { group }, MainWindowVM.GroupsWithAtMostOneFileLeft(items, i => i.ItemInfo.Path == "b"));
	}

	[Fact]
	public Task BusyBar_ShowsTheReportedProgress_AndAnimatesAgainAfterwards() => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		try {
			var bar = window.GetVisualDescendants().OfType<ProgressBar>()
				.First(p => p.FindAncestorOfType<Grid>()?.Name == "BusyIndicator" || p.GetVisualAncestors().OfType<Grid>().Any(g => g.Name == "BusyIndicator"));

			vm.IsBusy = true;
			HeadlessUi.Pump();
			Assert.True(bar.IsIndeterminate); // operations that cannot measure themselves

			vm.BusyProgress = 0.4;
			HeadlessUi.Pump();
			Assert.False(bar.IsIndeterminate);
			Assert.Equal(40, bar.Value, 3);

			vm.IsBusy = false;
			HeadlessUi.Pump();
			Assert.Null(vm.BusyProgress);

			vm.IsBusy = true;
			HeadlessUi.Pump();
			Assert.True(bar.IsIndeterminate); // the next operation does not inherit the old value
		}
		finally {
			vm.IsBusy = false;
			HeadlessUi.Pump();
		}
	});
}
