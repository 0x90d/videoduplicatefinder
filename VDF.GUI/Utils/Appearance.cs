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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using VDF.GUI.Data;

namespace VDF.GUI.Utils;

/// <summary>
/// How the app looks follows what the user told their operating system: light or dark, and
/// (further down) contrast, text size and motion. People set these once, for their eyes, in
/// one place, and an app that ignores them makes them do it again in every app, if the app
/// lets them at all. The settings only ever override towards what a user asks for.
/// Every window calls <see cref="Attach"/> from its constructor.
/// </summary>
static class Appearance {
	static bool started;

	/// <summary>True when the app is dark right now, whether by choice or because the system is.</summary>
	public static bool IsDarkNow => IsDark(SettingsFile.Instance.ThemeMode, SystemColors().ThemeVariant);

	internal static bool IsDark(ThemeMode mode, PlatformThemeVariant system) => mode switch {
		ThemeMode.Dark => true,
		ThemeMode.Light => false,
		_ => system == PlatformThemeVariant.Dark,
	};

	static PlatformColorValues SystemColors() =>
		Application.Current?.PlatformSettings?.GetColorValues() ?? new PlatformColorValues();

	public static void Attach(Window window) => Start();

	static void Start() {
		if (started || Application.Current is not { } app) return;
		started = true;
		if (app.PlatformSettings is { } platform)
			platform.ColorValuesChanged += (_, _) => Apply(); // the user switched the system while the app runs
		SettingsFile.Instance.PropertyChanged += (_, e) => {
			if (e.PropertyName == nameof(SettingsFile.ThemeMode)) Apply();
		};
		Apply();
	}

	// Set on the application, not per window: the managed window chrome (caption bar,
	// titlebar buttons) resolves its brushes against the application's variant, and every
	// dialog follows along without code of its own.
	internal static void Apply() {
		if (Application.Current is { } app)
			app.RequestedThemeVariant = IsDarkNow ? ThemeVariant.Dark : ThemeVariant.Light;
	}
}
