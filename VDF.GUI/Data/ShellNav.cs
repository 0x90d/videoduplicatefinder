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

namespace VDF.GUI.Data {

	/// <summary>The window's top-level view: the scanner (Setup/Scanning/Review states),
	/// or one of the secondary views that replace it (mockup titlebar navigation).</summary>
	public enum ShellView {
		Main,
		Settings,
		Log,
	}

	/// <summary>Which titlebar nav links are visible for a shell view (mockup titlebars:
	/// each state links to the OTHER views; Review additionally offers "New scan").</summary>
	public readonly record struct ShellNavLinks(bool NewScan, bool BackToResults, bool Log, bool Settings);

	public static class ShellNav {
		public static ShellNavLinks For(ShellView view, bool isReviewState) => new(
			NewScan: view == ShellView.Main && isReviewState,
			BackToResults: view != ShellView.Main,
			Log: view != ShellView.Log,
			Settings: view != ShellView.Settings);
	}
}
