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
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VDF.Core.ViewModels;
using VDF.GUI.Controls;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// The comparer's culling keys (A / D keep a side, Space next pair, arrows step frames, Z zoom)
/// win over whatever control has focus. The one exception: a focused slider keeps its plain
/// arrow keys, or it could not be operated from the keyboard at all.
/// </summary>
public class ComparerKeyboardTests {

	static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None) {
		window.KeyPressQwerty(key, modifiers);
		window.KeyReleaseQwerty(key, modifiers);
		HeadlessUi.Pump();
	}

	/// <summary>Comparer on a two-file group with "Highlight differences" on, its sensitivity slider focused.</summary>
	static void WithFocusedSensitivitySlider(Action<ThumbnailComparer, ThumbnailComparerVM, Slider, List<LargeThumbnailDuplicateItem>> body) =>
		// Completes synchronously: nothing in here awaits unless the body does.
		WithFocusedSensitivitySlider((comparer, vm, slider, items) => {
			body(comparer, vm, slider, items);
			return Task.CompletedTask;
		}).GetAwaiter().GetResult();

	static async Task WithFocusedSensitivitySlider(Func<ThumbnailComparer, ThumbnailComparerVM, Slider, List<LargeThumbnailDuplicateItem>, Task> body) {
		HeadlessUi.Shell(); // the comparer takes the main window as its owner
		bool highlightBefore = SettingsFile.Instance.ThumbnailComparerHighlightDifferences;
		double sensitivityBefore = SettingsFile.Instance.ThumbnailComparerDiffSensitivity;
		var group = Guid.NewGuid();
		// No thumbnail timestamps: loading yields nothing without ever starting FFmpeg.
		var items = new[] { "left.mp4", "right.mp4" }
			.Select(name => new LargeThumbnailDuplicateItem(new DuplicateItemVM(new DuplicateItem {
				Path = $@"Z:\does\not\exist\{name}", GroupId = group, Similarity = 98.4f,
			})))
			.ToList();
		var comparer = new ThumbnailComparer(items);
		var vm = (ThumbnailComparerVM)comparer.DataContext!;
		comparer.Show();
		HeadlessUi.Pump();
		try {
			vm.HighlightDifferences = true;
			vm.DiffSensitivity = 0.5;
			HeadlessUi.Pump();
			var slider = comparer.GetVisualDescendants().OfType<Slider>().Single(s => s.IsEffectivelyVisible && s.Maximum == 1);
			slider.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();
			Assert.Same(slider, comparer.FocusManager!.GetFocusedElement());
			await body(comparer, vm, slider, items);
		}
		finally {
			vm.HighlightDifferences = highlightBefore;      // both are stored in the shared settings
			vm.DiffSensitivity = sensitivityBefore;
			comparer.Hide();
			HeadlessUi.Pump();
		}
	}

	[Fact]
	public Task FocusedSlider_IsOperatedByThePlainArrowKeys() => HeadlessUi.Run(() =>
		WithFocusedSensitivitySlider((comparer, vm, _, _) => {
			// The culling handler used to take the arrows from every control but a text box,
			// so a slider could be reached with Tab and then not moved.
			Press(comparer, PhysicalKey.ArrowRight);
			double raised = vm.DiffSensitivity;
			Press(comparer, PhysicalKey.ArrowLeft);
			Press(comparer, PhysicalKey.ArrowLeft);

			Assert.True(raised > 0.5 && raised < 1, $"one press should be a small step, got {raised}");
			Assert.True(vm.DiffSensitivity < 0.5, $"two presses back should end below the start, got {vm.DiffSensitivity}");
		}));

	[Fact]
	public Task FocusedSlider_LeavesTheFineStepShiftArrows_ToTheComparer() => HeadlessUi.Run(() =>
		WithFocusedSensitivitySlider((comparer, vm, _, _) => {
			Press(comparer, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
			Press(comparer, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);

			Assert.Equal(0.5, vm.DiffSensitivity);
		}));

	[Fact]
	public Task FocusedSlider_DoesNotTakeTheOtherCullingKeys() => HeadlessUi.Run(() =>
		WithFocusedSensitivitySlider((comparer, vm, _, items) => {
			Press(comparer, PhysicalKey.A); // keep left, check right

			Assert.True(items[1].Item.Checked);
			Assert.False(items[0].Item.Checked);
			Assert.Equal(0.5, vm.DiffSensitivity);
		}));

	[Fact]
	public Task KeepingASide_IsSaid_NotOnlyShown() => HeadlessUi.Run(() =>
		WithFocusedSensitivitySlider(async (comparer, _, _, _) => {
			var announcer = comparer.GetVisualDescendants().OfType<AnnouncerHost>().Single();
			announcer.Spacing = TimeSpan.FromMilliseconds(100);
			await Task.Delay(300); // whatever the window said while it opened has had its turn
			var peer = ControlAutomationPeer.CreatePeerForElement(announcer);
			var spoken = new List<(string Name, AutomationLiveSetting Live)>();
			peer.PropertyChanged += (_, e) => {
				if (e.Property == AutomationElementIdentifiers.NameProperty && peer.GetLiveSetting() != AutomationLiveSetting.Off)
					spoken.Add((peer.GetName(), peer.GetLiveSetting()));
			};

			// The toast that confirms the key never takes focus and is gone after a moment.
			Press(comparer, PhysicalKey.A); // keep left, check right

			// This was the last pair, so "No more groups" follows in the same instant. Shown,
			// it simply replaces the first message; a screen reader asks for the text after
			// the fact and would never hear "Checked". So the second one waits its turn.
			Assert.Equal([("Checked: right.mp4", AutomationLiveSetting.Polite)], spoken);
			Assert.Equal("Checked: right.mp4", peer.GetName());

			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (spoken.Count < 2 && DateTime.UtcNow < deadline)
				await Task.Delay(10);
			Assert.Equal(2, spoken.Count);
			Assert.StartsWith("No more groups", spoken[1].Name);
		}));
}
