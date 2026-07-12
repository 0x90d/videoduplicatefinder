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

using System.Xml.Linq;

namespace VDF.GUI.Tests;

/// <summary>
/// The titlebar "New scan" link must return to the Setup screen (NewScanCommand
/// discards the shown results) rather than immediately restarting a scan with the
/// current settings — with restored results there was otherwise no way back to the
/// folders/profile screen at all.
/// </summary>
public class NewScanNavlinkTests {

	static string RepoRoot() {
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
			dir = dir.Parent;
		Assert.NotNull(dir);
		return dir!.FullName;
	}

	static IEnumerable<XElement> ElementsBoundTo(string xamlFile, string attribute, string binding) {
		var doc = XDocument.Load(Path.Combine(RepoRoot(), "VDF.GUI", "Views", xamlFile));
		return doc.Descendants().Where(e =>
			(string?)e.Attribute(attribute) == binding);
	}

	[Fact]
	public void NewScanNavlink_DiscardsToSetup_InsteadOfStartingScan() {
		var navlink = Assert.Single(ElementsBoundTo("MainWindow.xaml", "IsVisible", "{Binding ShowNavNewScan}"));
		Assert.Equal("{Binding NewScanCommand}", (string?)navlink.Attribute("Command"));
		Assert.Null(navlink.Attribute("CommandParameter"));
	}

	[Fact]
	public void SetupScreenScanButton_StillStartsFullScan() {
		var scanButtons = ElementsBoundTo("SetupView.xaml", "Command", "{Binding StartScanCommand}").ToList();
		Assert.NotEmpty(scanButtons);
		Assert.All(scanButtons, b => Assert.Equal("FullScan", (string?)b.Attribute("CommandParameter")));
	}
}
