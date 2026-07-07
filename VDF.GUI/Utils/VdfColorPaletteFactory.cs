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

using ActiproSoftware.UI.Avalonia.Media;
using ActiproSoftware.UI.Avalonia.Themes.Generation;

namespace VDF.GUI.Utils {
	/// <summary>
	/// The default ActiPro color palette plus VDF's teal-green accent ramp (redesign
	/// locked decision 12). The ramp is generated from the mockup accent #34B39C as
	/// midtone, so the dark theme lands on the mockup's #34B39C-ish accent and the
	/// light theme on its darker counterpart.
	/// </summary>
	public sealed class VdfColorPaletteFactory : DefaultColorPaletteFactory {
		public const string AccentRampName = "VdfTeal";

		public override ColorPalette Create() {
			ColorPalette palette = base.Create();
			palette.Ramps.Add(CreateColorRamp(AccentRampName, isNeutral: false, UIColor.Parse("#34B39C")));
			return palette;
		}
	}
}
