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
using Avalonia.Styling;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>The readability guard of <see cref="ContrastTests"/>, for every dialog and the comparer.</summary>
public class DialogContrastTests {

	static readonly Dictionary<string, Func<Window>> Dialogs = new() {
		["MessageBox"] = () => new MessageBoxView("Delete 3 files?",
			MessageBoxButtons.Yes | MessageBoxButtons.No | MessageBoxButtons.Cancel, "Confirm", MessageBoxButtons.No),
		["InputBox"] = () => new InputBoxView("New name:", "clip.mp4", "file name"),
		["About"] = () => new AboutWindow(),
		["ChooseAlgorithm"] = () => new ChooseAlgoView(),
		["BlacklistManager"] = () => new BlacklistManagerView(),
		["CustomSelection"] = () => new CustomSelectionView(string.Empty),
		["ExpressionBuilder"] = () => new ExpressionBuilder(),
		["QualityOrder"] = () => new QualityOrderDialog(),
		["RelocateFiles"] = () => new RelocateFilesDialog(),
		["DatabaseEditor"] = () => new DatabaseViewer(),
		["Comparer"] = () => {
			var group = Guid.NewGuid();
			// No thumbnail timestamps: loading yields nothing without ever starting FFmpeg.
			var items = new[] { "beach_2019_final.mp4", "beach_2019_final (1).mp4" }
				.Select(name => new LargeThumbnailDuplicateItem(new DuplicateItemVM(new DuplicateItem {
					Path = $@"Z:\does\not\exist\{name}", GroupId = group, Similarity = 98.4f,
					SizeLong = 700_000_000, FrameSize = "1280x720", Duration = TimeSpan.FromSeconds(754),
				})))
				.ToList();
			return new ThumbnailComparer(items);
		},
	};

	public static TheoryData<string, string> DialogsAndThemes() {
		var data = new TheoryData<string, string>();
		foreach (string dialog in Dialogs.Keys)
			foreach (string theme in new[] { "Dark", "Light", "HighContrastDark", "HighContrastLight" })
				data.Add(dialog, theme);
		return data;
	}

	[Theory]
	[MemberData(nameof(DialogsAndThemes))]
	public Task Text_IsReadable(string dialogName, string theme) => HeadlessUi.Run(() => {
		HeadlessUi.Shell(); // dialogs take their owner and icon from the main window
		bool highContrast = theme.StartsWith("HighContrast");
		var variant = highContrast ? ContrastTests.HighContrastVariant(theme) : theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		var dialog = Dialogs[dialogName]();
		dialog.RequestedThemeVariant = variant;
		dialog.Show();
		HeadlessUi.Pump();
		try {
			// At rest, then with every control under the pointer, then pressed.
			foreach (string state in new[] { "at rest", ":pointerover", ":pressed" }) {
				if (state != "at rest") ContrastTests.PutInState(dialog, state);
				// The high contrast themes are held to the enhanced level (7:1).
				var failures = ContrastTests.Measure(dialog, variant, highContrast ? ContrastTests.Enhanced : null);
				Assert.True(failures.Count == 0,
					$"{failures.Count} kind(s) of text below the required contrast in the {theme} theme, controls {state}:\n  " + string.Join("\n  ", failures));
			}
		}
		finally {
			// Hidden, not closed: several of these save or shut down in their Closing handler.
			dialog.Hide();
			HeadlessUi.Pump();
		}
	});
}
