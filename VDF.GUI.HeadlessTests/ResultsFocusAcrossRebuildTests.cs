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
/// Every rebuild of the results list replaced the row that had keyboard focus, and the
/// focus went nowhere: after a delete, collapsing a group or a filter change, a keyboard or
/// screen reader user had to find the list again from the top of the window.
/// </summary>
public class ResultsFocusAcrossRebuildTests {
	static (Window Window, MainWindowVM Vm, ListBox List) ShowResults() {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });
		var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
		return (window, vm, list);
	}

	static object? FocusedRow(Window window) =>
		(window.FocusManager!.GetFocusedElement() as Control)?.DataContext;

	[Fact]
	public Task TheFocusedFile_KeepsTheFocus_WhenTheListIsRebuilt() => HeadlessUi.Run(() => {
		var (window, vm, list) = ShowResults();
		try {
			var row = (ResultsItemRow)vm.ResultsRows[2];
			list.ContainerFromIndex(2)!.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();

			vm.RebuildResultsList();
			HeadlessUi.Pump();

			var focused = Assert.IsType<ResultsItemRow>(FocusedRow(window));
			Assert.Same(row.Item, focused.Item);
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task RemovingTheFocusedFilesGroup_PassesTheFocusOn() => HeadlessUi.Run(() => {
		var (window, vm, list) = ShowResults();
		try {
			var row = (ResultsItemRow)vm.ResultsRows[1];
			list.ContainerFromIndex(1)!.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();

			// The file's group leaves the list (a group of one is not shown).
			vm.Duplicates.Remove(row.Item);
			vm.RebuildResultsList();
			HeadlessUi.Pump();

			Assert.NotNull(FocusedRow(window));
			Assert.Contains(FocusedRow(window), vm.ResultsRows);
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task FocusTheUserMovedOutOfTheList_IsLeftAlone() => HeadlessUi.Run(() => {
		var (window, vm, list) = ShowResults();
		try {
			list.ContainerFromIndex(1)!.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();
			var sortBox = window.GetVisualDescendants().OfType<ComboBox>().First(c => c.IsEffectivelyVisible);

			vm.RebuildResultsList();
			sortBox.Focus(NavigationMethod.Tab); // before the deferred refocus runs
			HeadlessUi.Pump();

			Assert.Same(sortBox, window.FocusManager!.GetFocusedElement());
		}
		finally {
			window.Close();
		}
	});
}
