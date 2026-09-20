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

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using VDF.Core;
using VDF.GUI.Controls;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// Things that happen without the keyboard focus moving (a scan moving on, a scan ending,
/// the busy curtain, "copied") have to be said, or a screen reader user never learns of them.
/// </summary>
/// <remarks>
/// What these tests pin down is the contract Avalonia's platform backends consume: a Name
/// change on a peer that reports a live setting becomes the platform's live region event,
/// and only for an element the screen reader already knows, which for a screen reader
/// following the focus means the focused element and its ancestors. That last part was
/// measured against the real Windows backend with a UI Automation client; it cannot be
/// measured here, a headless session has no platform automation.
/// </remarks>
public class AnnouncementTests {

	sealed record Spoken(string? Name, AutomationLiveSetting Live);

	static List<Spoken> Listen(AnnouncerHost host) {
		var spoken = new List<Spoken>();
		var peer = ControlAutomationPeer.CreatePeerForElement(host);
		peer.PropertyChanged += (_, e) => {
			// What a screen reader gets when it asks back on the event.
			if (e.Property == AutomationElementIdentifiers.NameProperty)
				spoken.Add(new Spoken(peer.GetName(), peer.GetLiveSetting()));
		};
		return spoken;
	}

	[Fact]
	public Task Announcer_Announce_IsALiveNameChangeOnItsPeer() => HeadlessUi.Run(() => {
		var host = new AnnouncerHost { Child = new Button { Content = "inside" } };
		var window = HeadlessUi.Show(host, 300, 200);
		try {
			var spoken = Listen(host);

			host.Announce("comparing duplicates");
			host.Announce("Delete failed", interrupt: true);

			Assert.Equal([
				new Spoken("comparing duplicates", AutomationLiveSetting.Polite),
				new Spoken("Delete failed", AutomationLiveSetting.Assertive),
			], spoken);
		}
		finally {
			window.Close();
		}
	});

	static async Task Until(Func<bool> condition) {
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);
	}

	[Fact]
	public Task Announcer_TheSameTextTwice_IsSaidTwice() => HeadlessUi.Run(async () => {
		var host = new AnnouncerHost { Spacing = TimeSpan.FromMilliseconds(30) };
		var window = HeadlessUi.Show(host, 300, 200);
		try {
			var spoken = Listen(host);

			// Checking two rows in a row is two things that happened.
			host.Announce("checked");
			host.Announce("checked");
			await Until(() => spoken.Count == 2);

			Assert.Equal(2, spoken.Count);
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task Announcer_MessagesInTheSameInstant_AreEachHeard() => HeadlessUi.Run(async () => {
		var host = new AnnouncerHost { Spacing = TimeSpan.FromMilliseconds(150) };
		var window = HeadlessUi.Show(host, 300, 200);
		try {
			var spoken = Listen(host);
			var peer = ControlAutomationPeer.CreatePeerForElement(host);

			host.Announce("Checked: right.mp4");
			host.Announce("No more groups");

			// A screen reader asks for the text after the event reached it. Had the second
			// message replaced the first at once, it would ask twice and hear the second twice.
			Assert.Equal([new Spoken("Checked: right.mp4", AutomationLiveSetting.Polite)], spoken);
			await Task.Delay(40);
			Assert.Equal("Checked: right.mp4", peer.GetName());

			await Until(() => spoken.Count == 2);
			Assert.Equal(new Spoken("No more groups", AutomationLiveSetting.Polite), spoken.Last());
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task Announcer_FallingBehind_DropsTheOldestNotTheNewest() => HeadlessUi.Run(async () => {
		var host = new AnnouncerHost { Spacing = TimeSpan.FromMilliseconds(15) };
		var window = HeadlessUi.Show(host, 300, 200);
		try {
			var spoken = Listen(host);

			for (int i = 1; i <= 40; i++)
				host.Announce("message " + i);
			await Until(() => spoken.Any(m => m.Name == "message 40"));
			await Task.Delay(100);

			Assert.Equal("message 1", spoken.First().Name);
			Assert.Equal("message 40", spoken.Last().Name);
			Assert.InRange(spoken.Count, 2, 10); // not a backlog of forty
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task Announcer_AnInterruptingMessage_DoesNotWaitItsTurn() => HeadlessUi.Run(() => {
		var host = new AnnouncerHost();
		var window = HeadlessUi.Show(host, 300, 200);
		try {
			var spoken = Listen(host);

			host.Announce("comparing duplicates");
			host.Announce("40 percent");
			host.Announce("Delete failed", interrupt: true);

			Assert.Equal(new Spoken("Delete failed", AutomationLiveSetting.Assertive), spoken.Last());
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task Announcer_Idle_IsAnOrdinaryNamelessContainer() => HeadlessUi.Run(async () => {
		var host = new AnnouncerHost { Lifetime = TimeSpan.FromMilliseconds(30) };
		var window = HeadlessUi.Show(host, 300, 200);
		try {
			var peer = ControlAutomationPeer.CreatePeerForElement(host);
			Assert.Equal(string.Empty, peer.GetName());
			Assert.Equal(AutomationLiveSetting.Off, peer.GetLiveSetting());

			host.Announce("Scan complete. Found 3 duplicate group(s).");
			host.Announce(null);
			host.Announce("  ");
			Assert.Equal("Scan complete. Found 3 duplicate group(s).", peer.GetName());

			// A message left standing would be read out as the name of the group around
			// everything, every time focus comes back into the window.
			var spoken = Listen(host);
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (host.Message != null && DateTime.UtcNow < deadline)
				await Task.Delay(20);
			Assert.Equal(string.Empty, peer.GetName());
			// Going quiet is a plain name change, not something to speak.
			Assert.Equal([new Spoken(string.Empty, AutomationLiveSetting.Off)], spoken);
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task Shell_TheAnnouncer_IsAnAncestorOfWhateverCanHaveFocus() => HeadlessUi.Run(() => {
		var (window, _) = HeadlessUi.Shell();
		var host = window.GetVisualDescendants().OfType<AnnouncerHost>().Single();
		var hostPeer = ControlAutomationPeer.CreatePeerForElement(host);

		// The platform only forwards an announcement from an element the screen reader has
		// met, and following the focus it meets the focused element and its ancestors. So
		// the chain of automation parents from any focusable control has to pass the host.
		var outside = new List<string>();
		var focusable = ((Control)window.Content!).GetVisualDescendants().OfType<Control>()
			.Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled).ToList();
		foreach (var control in focusable) {
			bool passesHost = false;
			for (AutomationPeer? peer = ControlAutomationPeer.CreatePeerForElement(control); peer != null; peer = peer.GetParent())
				if (ReferenceEquals(peer, hostPeer)) { passesHost = true; break; }
			if (!passesHost) outside.Add(control.GetType().Name + " '" + AutomationProperties.GetName(control) + "'");
		}

		Assert.NotEmpty(focusable);
		Assert.True(outside.Count == 0, "not below the announcer in the automation tree: " + string.Join(", ", outside));
	});

	[Fact]
	public Task Shell_WhatTheViewModelAnnounces_IsSpokenByTheWindow() => HeadlessUi.Run(async () => {
		var (window, vm) = HeadlessUi.Shell();
		var host = window.GetVisualDescendants().OfType<AnnouncerHost>().Single();
		var spoken = Listen(host);

		vm.Announce("comparing duplicates");
		// Not necessarily at once: the window may still be spacing out what other tests said.
		await Until(() => spoken.Any(m => m.Name == "comparing duplicates"));

		Assert.Contains(new Spoken("comparing duplicates", AutomationLiveSetting.Polite), spoken);
	});

	[Fact]
	public Task Shell_BusyCurtain_SaysWhatItIsBusyWith_NotWhatRanBefore() => HeadlessUi.Run(() => {
		var (_, vm) = HeadlessUi.Shell();
		var said = new List<string>();
		void OnAnnounced(string text, bool interrupt) => said.Add(text);
		vm.IsBusyOverlayText = "Deleting files... 200/200"; // left over from the operation before
		HeadlessUi.Pump();
		vm.Announced += OnAnnounced;
		try {
			// The order every operation uses: the flag first, its text second.
			vm.IsBusy = true;
			vm.IsBusyOverlayText = "Cleaning database...";
			HeadlessUi.Pump();

			Assert.Equal(["Cleaning database..."], said);
		}
		finally {
			vm.Announced -= OnAnnounced;
			vm.IsBusy = false;
			vm.IsBusyOverlayText = string.Empty;
			HeadlessUi.Pump();
		}
	});

	[Fact]
	public Task Shell_ScanProgressAndOutcome_AreAnnounced() => HeadlessUi.Run(() => {
		var (_, vm) = HeadlessUi.Shell();
		var said = new List<string>();
		void OnAnnounced(string text, bool interrupt) => said.Add(text);
		vm.Announced += OnAnnounced;
		try {
			var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
			var snapshot = ScanProgressSnapshot.Default with { MaxPosition = 1000 };
			vm.AnnounceScanProgress(snapshot, now);
			vm.AnnounceScanProgress(snapshot with { CurrentStage = "sampling frames", CurrentPosition = 300, Remaining = TimeSpan.FromSeconds(95) }, now.AddMinutes(1));
			vm.AnnounceScanDone(); // this view model holds no duplicates

			Assert.Equal(["Scanning", "30 percent, about 1m, 35s left", "Your last scan finished and found no duplicate files."], said);
		}
		finally {
			vm.Announced -= OnAnnounced;
		}
	});

	[Fact]
	public Task ScanOutcome_WithDuplicates_SaysHowManyGroups() => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		string? said = null;
		vm.Announced += (text, _) => said = text;
		vm.TotalDuplicateGroups = 2;

		vm.AnnounceScanDone();

		Assert.Equal("Scan complete. Found 2 duplicate group(s).", said);
	});

	static Control? Focused(Window window) => window.FocusManager!.GetFocusedElement() as Control;

	[Fact]
	public Task Shell_FocusFollowsTheScan_InsteadOfVanishingWithTheButtonThatHadIt() => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		var scanButton = window.GetVisualDescendants().OfType<SetupView>().First().FindControl<Button>("ScanButton")!;
		var scanning = window.GetVisualDescendants().OfType<ScanningView>().First();
		scanButton.Focus(NavigationMethod.Tab);
		HeadlessUi.Pump();
		Assert.Same(scanButton, Focused(window));
		try {
			// Scan hides the Setup view, and the button that had focus with it. Focus used to
			// be left on nothing: the next Tab started over at the top of the window, and a
			// screen reader, which speaks what gets focus, said nothing at all.
			vm.IsScanning = true;
			HeadlessUi.Pump();
			Assert.Same(scanning.FindControl<Button>("PauseButton"), Focused(window));

			// Pause replaces itself with Resume.
			vm.IsPaused = true;
			HeadlessUi.Pump();
			Assert.Same(scanning.FindControl<Button>("ResumeButton"), Focused(window));

			vm.IsPaused = false;
			HeadlessUi.Pump();
			Assert.Same(scanning.FindControl<Button>("PauseButton"), Focused(window));
		}
		finally {
			vm.IsPaused = false;
			vm.IsScanning = false;
			HeadlessUi.Pump();
		}

		// A scan that found nothing, or was stopped, ends on the Setup view again.
		Assert.Same(scanButton, Focused(window));
	});

	[Fact]
	public Task Shell_WhenAScanEndsWithResults_FocusGoesToTheFirstRow() => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		window.GetVisualDescendants().OfType<SetupView>().First().FindControl<Button>("ScanButton")!.Focus(NavigationMethod.Tab);
		HeadlessUi.Pump();
		try {
			vm.IsScanning = true;
			HeadlessUi.Pump();
			foreach (var item in ResultsFixture.CreatePopulatedViewModel().Duplicates)
				vm.Duplicates.Add(item);
			vm.RebuildResultsList();
			vm.IsScanning = false;
			HeadlessUi.Pump();

			var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
			Assert.Same(list.ContainerFromIndex(0), Focused(window));
		}
		finally {
			vm.IsScanning = false;
			vm.Duplicates.Clear();
			vm.RebuildResultsList();
			HeadlessUi.Pump();
		}
	});

	[Fact]
	public Task Shell_FocusTheUserPutSomewhereElse_IsLeftAlone() => HeadlessUi.Run(() => {
		var (window, vm) = HeadlessUi.Shell();
		try {
			// Reading the log while the scan runs: the scan ending must not pull focus away.
			vm.ActiveShellView = ShellView.Log;
			HeadlessUi.Pump();
			var inLog = window.GetVisualDescendants().OfType<LogView>().First().GetVisualDescendants().OfType<Control>()
				.First(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
			inLog.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();
			Assert.Same(inLog, Focused(window));

			vm.IsScanning = true;
			HeadlessUi.Pump();
			vm.IsScanning = false;
			HeadlessUi.Pump();

			Assert.Same(inLog, Focused(window));
		}
		finally {
			vm.IsScanning = false;
			vm.ActiveShellView = ShellView.Main;
			HeadlessUi.Pump();
		}
	});

	[Fact]
	public Task Results_SpaceOnAFocusedRow_SaysTheNewState() => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });
		var said = new List<string>();
		vm.Announced += (text, _) => said.Add(text);
		try {
			var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
			var row = (ListBoxItem)list.ContainerFromIndex(1)!;
			var item = ((ResultsItemRow)row.DataContext!).Item;
			row.Focus(NavigationMethod.Directional);
			HeadlessUi.Pump();

			// Focus stays on the row, and a name that changes under the focus raises nothing
			// a screen reader would speak: toggling was silent.
			item.Checked = true;
			item.Checked = false;

			Assert.Equal(["checked", "not checked"], said);
		}
		finally {
			window.Close();
		}
	});

	[Fact]
	public Task Results_CheckingManyRowsAtOnce_DoesNotTalkOncePerRow() => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = HeadlessUi.Show(new DuplicateResultsView { DataContext = vm });
		var said = new List<string>();
		vm.Announced += (text, _) => said.Add(text);
		try {
			var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
			list.ContainerFromIndex(1)!.Focus(NavigationMethod.Directional);
			HeadlessUi.Pump();

			foreach (var item in vm.Duplicates) // what "select all" and the auto-select rules do
				item.Checked = true;

			Assert.Equal(["checked"], said); // the row under the focus, and only that one
		}
		finally {
			window.Close();
		}
	});
}
