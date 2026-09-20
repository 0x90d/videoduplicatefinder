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

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// Screen reader guard: every control a user operates must announce as something a person
/// can understand. Loads the real views and reads the automation tree, which is the one
/// source Narrator/NVDA (UIA), VoiceOver (NSAccessibility) and Orca (AT-SPI) are all fed from.
/// The classic failures: a Button whose Content is a panel announces as
/// "Avalonia.Controls.StackPanel", a list item announces as its view model's type name, an
/// icon button announces as its glyph, and a control next to a visual label has no name at all.
/// </summary>
public class AccessibleNameTests {

	/// <summary>
	/// A gap a later step of the accessibility work still owes. An allowance that no longer
	/// matches anything fails the test, so this list can only shrink.
	/// </summary>
	internal sealed record KnownGap(string Reason, Func<PeerNode, bool> Matches);

	internal static void AssertEverythingIsNamed(Control root, params KnownGap[] knownGaps) {
		var failures = new List<string>();
		var usedGaps = new HashSet<KnownGap>();
		// What the tree offers, plus every Tab stop: focus is what a screen reader follows,
		// and a focusable control without a usable peer (a bare Border, say) is in no tree.
		var nodes = PeerTree.Walk(root);
		var inTree = nodes.Select(n => n.Owner).ToHashSet();
		nodes.AddRange(PeerTree.TabStops(root).Where(n => !inTree.Contains(n.Owner)));
		foreach (var node in nodes) {
			bool isTabStop = !inTree.Contains(node.Owner);
			if (!isTabStop && (!PeerTree.IsInteractive(node.Type) || PeerTree.IsMouseOnlyTemplatePart(node)))
				continue;
			string? problem = PeerTree.NameProblem(node.Name);
			if (problem == null)
				continue;
			var gap = knownGaps.FirstOrDefault(g => g.Matches(node));
			if (gap != null) {
				usedGaps.Add(gap);
				continue;
			}
			failures.Add($"{problem}: {node.Describe()}");
		}
		foreach (var stale in knownGaps.Where(g => !usedGaps.Contains(g)))
			failures.Add($"stale allowance, remove it: {stale.Reason}");

		Assert.True(failures.Count == 0,
			$"{failures.Count} control(s) a screen reader cannot announce properly:\n  " + string.Join("\n  ", failures));
	}

	[Fact]
	public Task SetupView_ControlsAnnounceProperly() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		vm.SetupFolders.Add(new SetupFolderVM(@"D:\Videos\Holiday", isExcluded: false) { MetaText = "1,204 files" });
		vm.SetupFolders.Add(new SetupFolderVM(@"E:\Archive\Old", isExcluded: true) { MetaText = "Excluded" });
		vm.ShowNoDuplicatesNotice = true;
		var window = HeadlessUi.Show(new SetupView { DataContext = vm });

		AssertEverythingIsNamed(window);
		window.Close();
	});

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public Task ScanningView_ControlsAnnounceProperly(bool paused) => HeadlessUi.Run(() => {
		var vm = new MainWindowVM { IsScanning = true, IsPaused = paused };
		var window = HeadlessUi.Show(new ScanningView { DataContext = vm });

		AssertEverythingIsNamed(window);
		window.Close();
		vm.IsScanning = false;
	});

	[Fact]
	public Task SettingsView_EverySection_ControlsAnnounceProperly() => HeadlessUi.Run(() => {
		var vm = new MainWindowVM();
		var view = new SettingsView { DataContext = vm };
		var window = HeadlessUi.Show(view);

		// The page shows one section at a time and keeps a few rows collapsed; a screen
		// reader user reaches all of them eventually, so check them all at once.
		foreach (var panel in view.FindControl<StackPanel>("SectionsHost")!.Children.OfType<StackPanel>())
			panel.IsVisible = true;
		foreach (var row in view.GetLogicalDescendants().OfType<SettingRow>())
			row.IsVisible = true;
		HeadlessUi.Pump();

		AssertEverythingIsNamed(window);
		window.Close();
	});

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public Task Shell_TitlebarAndBusyOverlay_AnnounceProperly(bool busy) => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		vm.IsBusy = busy;
		HeadlessUi.Pump();
		try {
			AssertEverythingIsNamed(window);
		}
		finally {
			vm.IsBusy = false;
			HeadlessUi.Pump();
		}
	});

	[Fact]
	public Task ResultsView_ControlsAnnounceProperly() => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		vm.Duplicates[1].Checked = true; // brings up the action bar
		vm.ToggleItemDetailsCommand.Execute(vm.Duplicates[0]).Subscribe(); // and one details panel
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });

		AssertEverythingIsNamed(window);
		window.Close();
	});

	static List<PeerNode> ResultRows(Window window) => PeerTree.Walk(window)
		.Where(n => n.Type == AutomationControlType.ListItem && n.Owner?.FindAncestorOfType<ListBox>()?.Name == "ResultsList").ToList();

	[Fact]
	public Task ResultsView_RowsAnnounceTheFile_NotTheViewModelType() => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });

		var rows = ResultRows(window);

		Assert.StartsWith("Group 1, 2 files", rows[0].Name);
		Assert.StartsWith("beach_2019_final.mp4, ", rows[1].Name);
		Assert.Contains("1920x1080", rows[1].Name);
		Assert.Contains(@"D:\Videos\Holiday", rows[1].Name);
		window.Close();
	});

	[Fact]
	public Task ResultsView_CheckingARow_IsHeardOnTheRowThatHasFocus() => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });
		Assert.StartsWith("beach_2019_final (1).mp4", ResultRows(window)[2].Name);

		// Space on a focused row toggles its checkbox: focus stays on the row, so the row
		// itself has to say that it is now marked for deletion.
		vm.Duplicates[1].Checked = true;
		Assert.StartsWith("checked, beach_2019_final (1).mp4", ResultRows(window)[2].Name);

		vm.Duplicates[1].Checked = false;
		Assert.StartsWith("beach_2019_final (1).mp4", ResultRows(window)[2].Name);
		window.Close();
	});

	[Fact]
	public Task ResultsView_FolderLine_IsInTheTree_InFull() => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm }, width: 700);

		// Drawn by a custom control that trims in the middle; a screen reader needs the
		// whole path, and used to get none of it.
		var folders = PeerTree.Walk(window).Where(n => n.Owner is Controls.MiddleEllipsisTextBlock).Select(n => n.Name).ToList();

		Assert.Contains(@"D:\Videos\Holiday\copy", folders);
		window.Close();
	});

	[Theory]
	[InlineData("Don't show again ✕", false)] // the close glyph used to be part of the translated text
	[InlineData("▶ Scan", false)]
	[InlineData("Don't show again", true)]
	[InlineData("Compare 32×32 frames", true)] // a multiplication sign between numbers is text
	[InlineData("Holiday – 2019.mp4", true)]
	public void NameRule_KeepsIconGlyphsOutOfWhatIsReadAloud(string name, bool acceptable) =>
		Assert.Equal(acceptable, PeerTree.NameProblem(name) == null);
}
