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

using VDF.GUI.Data;

namespace VDF.GUI.Tests;

// Mockup titlebars: Setup/Scanning link to Log+Settings; Review additionally offers
// "New scan"; Settings and Log each link back to the main view and to each other.
public class ShellNavTests {

	[Fact]
	public void Main_WithoutResults_ShowsLogAndSettingsOnly() {
		var links = ShellNav.For(ShellView.Main, isReviewState: false);
		Assert.False(links.NewScan);
		Assert.False(links.BackToResults);
		Assert.True(links.Log);
		Assert.True(links.Settings);
	}

	[Fact]
	public void Main_InReviewState_AddsNewScan() {
		var links = ShellNav.For(ShellView.Main, isReviewState: true);
		Assert.True(links.NewScan);
		Assert.False(links.BackToResults);
		Assert.True(links.Log);
		Assert.True(links.Settings);
	}

	[Fact]
	public void Settings_ShowsBackAndLog_NoSettingsLink() {
		var links = ShellNav.For(ShellView.Settings, isReviewState: true);
		Assert.False(links.NewScan); // "New scan" belongs to the Review state only
		Assert.True(links.BackToResults);
		Assert.True(links.Log);
		Assert.False(links.Settings);
	}

	[Fact]
	public void Log_ShowsBackAndSettings_NoLogLink() {
		var links = ShellNav.For(ShellView.Log, isReviewState: false);
		Assert.False(links.NewScan);
		Assert.True(links.BackToResults);
		Assert.False(links.Log);
		Assert.True(links.Settings);
	}
}
