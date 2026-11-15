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

using Bunit;
using Microsoft.JSInterop;
using VDF.Web.Components.Layout;

namespace VDF.Web.Tests.Components;

public sealed class ThemeToggleTests : BunitContext {

	const string Sun = "☀", Moon = "☽";

	[Fact]
	public void ShowsTheThemeTheBrowserRemembers() {
		JSInterop.Setup<string>("vdf.getTheme").SetResult("light");

		var toggle = Render<ThemeToggle>();

		Assert.Equal(Moon, toggle.Find("button").TextContent.Trim());
	}

	[Fact]
	public void BrowserGoneOnFirstRender_KeepsTheDarkDefault() {
		JSInterop.Setup<string>("vdf.getTheme").SetException(new JSDisconnectedException("circuit gone"));

		var toggle = Render<ThemeToggle>();

		Assert.Equal(Sun, toggle.Find("button").TextContent.Trim());
	}

	[Fact]
	public void Toggle_StillFlipsWhenTheScriptFails() {
		JSInterop.Setup<string>("vdf.getTheme").SetResult("dark");
		JSInterop.SetupVoid("vdf.setTheme", _ => true).SetException(new JSException("vdf is not defined"));
		var toggle = Render<ThemeToggle>();

		toggle.Find("button").Click();

		Assert.Equal(Moon, toggle.Find("button").TextContent.Trim());
	}
}
