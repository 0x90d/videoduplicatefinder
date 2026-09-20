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
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// The screen reader name guard of <see cref="AccessibleNameTests"/>, run over the secondary
/// windows: every dialog, the log, the database editor and the comparer.
/// </summary>
public class DialogAccessibleNameTests {

	/// <summary>Shows the dialog without an owner-modal loop, checks it, and hides it again.</summary>
	static void Check(Func<Window> create, params AccessibleNameTests.KnownGap[] knownGaps) {
		HeadlessUi.Shell(); // dialogs take their owner and icon from the main window
		var dialog = create();
		dialog.Show();
		HeadlessUi.Pump();
		try {
			Assert.False(string.IsNullOrWhiteSpace(dialog.Title), "a window needs a title, it is the first thing announced");
			AccessibleNameTests.AssertEverythingIsNamed(dialog, knownGaps);
		}
		finally {
			// Hidden, not closed: several of these save or shut down in their Closing handler.
			dialog.Hide();
			HeadlessUi.Pump();
		}
	}

	[Fact]
	public Task MessageBox() => HeadlessUi.Run(() => Check(() => new MessageBoxView(
		"Delete 3 files?", MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel, "Confirm", MessageBoxButtons.No)));

	[Fact]
	public Task InputBox() => HeadlessUi.Run(() => Check(() => new InputBoxView("New name:", "clip.mp4", "file name")));

	[Fact]
	public Task About() => HeadlessUi.Run(() => Check(() => new AboutWindow()));

	[Fact]
	public Task ChooseAlgorithm() => HeadlessUi.Run(() => Check(() => new ChooseAlgoView()));

	[Fact]
	public Task BlacklistManager() => HeadlessUi.Run(() => Check(() => new BlacklistManagerView()));

	[Fact]
	public Task CustomSelection() => HeadlessUi.Run(() => Check(() => new CustomSelectionView(string.Empty)));

	[Fact]
	public Task ExpressionBuilder() => HeadlessUi.Run(() => Check(() => new ExpressionBuilder()));

	[Fact]
	public Task QualityOrder() => HeadlessUi.Run(() => Check(() => new QualityOrderDialog()));

	[Fact]
	public Task RelocateFiles() => HeadlessUi.Run(() => Check(() => new RelocateFilesDialog()));

	[Fact]
	public Task DatabaseEditor() => HeadlessUi.Run(() => Check(() => new DatabaseViewer()));

	[Fact]
	public Task Comparer() => HeadlessUi.Run(() => Check(() => {
		var group = Guid.NewGuid();
		// No thumbnail timestamps: loading yields nothing without ever starting FFmpeg.
		var items = new[] { "beach_2019_final.mp4", "beach_2019_final (1).mp4" }
			.Select(name => new LargeThumbnailDuplicateItem(new DuplicateItemVM(new DuplicateItem {
				Path = $@"Z:\does\not\exist\{name}", GroupId = group, Similarity = 98.4f,
			})))
			.ToList();
		return new ThumbnailComparer(items);
	}));

	[Fact]
	public Task LogView() => HeadlessUi.Run(() => {
		var window = HeadlessUi.Show(new LogView { DataContext = new MainWindowVM() });
		AccessibleNameTests.AssertEverythingIsNamed(window);
		window.Close();
	});
}
