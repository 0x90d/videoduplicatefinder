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
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// A settings row shows its title and description as text beside the control. The row has to
/// hand both to the control itself, or a screen reader announces "button" for every switch.
/// </summary>
public class SettingRowAccessibilityTests {

	/// <summary>
	/// The row's real template lives in SettingsView.xaml; outside that view a bare presenter
	/// stands in so the content is realized (the full page is covered by AccessibleNameTests).
	/// </summary>
	static Window ShowRow(SettingRow row) {
		row.Template = new FuncControlTemplate<SettingRow>((owner, _) => new ContentPresenter {
			Name = "PART_ContentPresenter",
			[!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
		});
		return HeadlessUi.Show(row);
	}

	static string? NameOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();
	static string? HelpOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetHelpText();

	[Fact]
	public Task Control_TakesTitleAsNameAndDescriptionPlusWarningAsHelp() => HeadlessUi.Run(() => {
		var toggle = new ToggleSwitch();
		var window = ShowRow(new SettingRow {
			Title = "Include images:", Description = "Also scans photos.", Warning = "Slower.", Content = toggle
		});

		Assert.Equal("Include images", NameOf(toggle)); // the row strips the legacy trailing colon
		Assert.Equal("Also scans photos. Slower.", HelpOf(toggle));
		window.Close();
	});

	[Fact]
	public Task NameFollowsTheTitle_SoALanguageSwitchIsAnnouncedCorrectly() => HeadlessUi.Run(() => {
		var toggle = new ToggleSwitch();
		var row = new SettingRow { Title = "Dark mode", Content = toggle };
		var window = ShowRow(row);

		row.Title = "Dunkler Modus";

		Assert.Equal("Dunkler Modus", NameOf(toggle));
		window.Close();
	});

	[Fact]
	public Task NumericUpDown_PassesItsNameToTheInnerTextBoxThatTakesFocus() => HeadlessUi.Run(() => {
		var stepper = new NumericUpDown();
		var window = ShowRow(new SettingRow { Title = "Parallel workers", Description = "How many at once.", Content = stepper });

		var inner = stepper.GetVisualDescendants().OfType<TextBox>().Single();

		Assert.Equal("Parallel workers", NameOf(stepper));
		Assert.Equal("Parallel workers", NameOf(inner));
		Assert.Equal("How many at once.", HelpOf(inner));
		window.Close();
	});

	[Fact]
	public Task RowWithSeveralControls_NamesTheFirstInput_AndLeavesButtonsAlone() => HeadlessUi.Run(() => {
		var link = new Button { Content = "More info" };
		var combo = new ComboBox();
		var depth = new NumericUpDown();
		AutomationProperties.SetName(depth, "Folder depth");
		var window = ShowRow(new SettingRow {
			Title = "Folder match mode",
			Content = new StackPanel { Children = { link, combo, depth } }
		});

		Assert.Equal("More info", NameOf(link));
		Assert.Equal("Folder match mode", NameOf(combo));
		Assert.Equal("Folder depth", NameOf(depth));
		window.Close();
	});

	[Fact]
	public Task NameSetInXaml_WinsOverTheTitle() => HeadlessUi.Run(() => {
		var toggle = new ToggleSwitch();
		AutomationProperties.SetName(toggle, "Ignore black pixels");
		var window = ShowRow(new SettingRow { Title = "Ignore black / white pixels", Content = toggle });

		Assert.Equal("Ignore black pixels", NameOf(toggle));
		window.Close();
	});

	[Fact]
	public Task TextBox_WithOnlyAPlaceholder_AnnouncesThePlaceholder() => HeadlessUi.Run(() => {
		var search = new TextBox { PlaceholderText = "Search all settings" };
		var plain = new TextBox();
		var window = HeadlessUi.Show(new StackPanel { Children = { search, plain } });

		Assert.Equal("Search all settings", NameOf(search));
		Assert.True(string.IsNullOrEmpty(NameOf(plain)));
		window.Close();
	});
}
