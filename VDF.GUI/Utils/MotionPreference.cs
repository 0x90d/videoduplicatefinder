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

using System.Runtime.InteropServices;

namespace VDF.GUI.Utils;

/// <summary>
/// Whether the user asked their system for less motion (Windows: Settings, Accessibility,
/// Visual effects, "Animation effects"). People turn that off because movement on screen
/// makes them dizzy or pulls their attention away; an endless pulse on every loading
/// thumbnail is exactly that. Avalonia does not expose the setting.
/// </summary>
static partial class MotionPreference {
	const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

	/// <summary>True when animations that run by themselves should stand still.</summary>
	public static bool ReduceMotion => FromSystem(QueryClientAreaAnimation());

	/// <param name="animationsEnabled">The system's answer, null where there is none to ask.</param>
	internal static bool FromSystem(bool? animationsEnabled) => animationsEnabled == false;

	static bool? QueryClientAreaAnimation() {
		if (!OperatingSystem.IsWindows()) return null; // macOS and Linux: not read yet
		try {
			return SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, out int enabled, 0) ? enabled != 0 : null;
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) {
			return null;
		}
	}

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);
}
