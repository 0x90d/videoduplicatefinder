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
using Avalonia.Input;
using Avalonia.VisualTree;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// Two things a results row offered to the mouse only: its details panel (the button for it
/// cannot be tabbed to inside a list) and the differences to the group's best values, which
/// show while the pointer rests on a metric. Both are in the row's menu, which the keyboard
/// opens with Shift+F10 or the Menu key.
/// </summary>
public class RowMenuKeyboardPathTests {

	static void WithOpenRowMenu(Action<MainWindowVM, ResultsItemRow, Func<string, MenuItem>> body) {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });
		var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
		var container = (ListBoxItem)list.ContainerFromIndex(2)!; // the second file of the first group
		var row = (ResultsItemRow)container.DataContext!;
		var menu = container.GetVisualDescendants().OfType<Border>().First(b => b.ContextMenu != null).ContextMenu!;
		container.Focus(NavigationMethod.Directional);
		list.SelectedIndex = 2;
		HeadlessUi.Pump();
		container.RaiseEvent(new ContextRequestedEventArgs()); // what Shift+F10 raises on the focused row
		HeadlessUi.Pump();
		try {
			Assert.True(menu.IsOpen);
			body(vm, row, header => menu.Items.OfType<MenuItem>().Single(m => (m.Header as string) == header));
		}
		finally {
			menu.Close();
			window.Close();
		}
	}

	static void Choose(MenuItem item) {
		Assert.NotNull(item.Command);
		Assert.True(item.Command!.CanExecute(item.CommandParameter));
		item.Command.Execute(item.CommandParameter);
		HeadlessUi.Pump();
	}

	[Fact]
	public Task ShowFileDetails_OpensTheDetailsOfTheFocusedRow() => HeadlessUi.Run(() =>
		WithOpenRowMenu((vm, row, item) => {
			Assert.DoesNotContain(vm.ResultsRows, r => r is ResultsDetailsRow);

			Choose(item("Show file details"));

			var details = Assert.Single(vm.ResultsRows.OfType<ResultsDetailsRow>());
			Assert.Same(row.Item, details.Item);
		}));

	[Fact]
	public Task CompareWithBest_ShowsTheDifferences_AndSaysThoseOfTheRow() => HeadlessUi.Run(() =>
		WithOpenRowMenu((vm, row, item) => {
			var said = new List<string>();
			vm.Announced += (text, _) => said.Add(text);
			var best = vm.Duplicates[0];
			Assert.Null(row.Item.SizeDiff);

			Choose(item("Compare values with the best"));

			// What resting the pointer on each metric in turn would have shown.
			Assert.Equal("BEST", best.SizeDiff);
			Assert.Equal("BEST", best.FrameSizeDiff);
			Assert.StartsWith("-", row.Item.SizeDiff);
			Assert.StartsWith("-", row.Item.FrameSizeDiff);
			Assert.Null(row.Item.DurationDiff); // the group agrees on it: nothing to show

			string spoken = Assert.Single(said);
			Assert.StartsWith("Compared with the best: duration =, resolution " + row.Item.FrameSizeDiff + ", size " + row.Item.SizeDiff, spoken);

			// The pointer crossing the row on its way elsewhere must not end what was asked for.
			vm.ClearHoveredMetric(row.Item);
			Assert.NotNull(row.Item.SizeDiff);

			Choose(item("Compare values with the best"));
			Assert.Null(row.Item.SizeDiff);
			Assert.Null(best.SizeDiff);
		}));

	[Fact]
	public Task CompareWithBest_OnAnotherGroup_MovesThere() => HeadlessUi.Run(() =>
		WithOpenRowMenu((vm, row, item) => {
			Choose(item("Compare values with the best"));
			Assert.NotNull(row.Item.SizeDiff);

			vm.ToggleGroupDiffs(vm.Duplicates[3]); // a file of the second group

			Assert.Null(row.Item.SizeDiff);
			Assert.NotNull(vm.Duplicates[3].SizeDiff);
			vm.ToggleGroupDiffs(vm.Duplicates[3]);
		}));
}
