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

using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VDF.GUI.Utils;

/// <summary>
/// What the user told their operating system about how they need things shown, as far as
/// Avalonia does not report it itself (it reports light or dark and high contrast). Every
/// query answers null where the system has no such setting or cannot be asked, and null
/// always means "leave things as they are".
/// </summary>
static class SystemPreferences {

	/// <summary>
	/// The system's text size as a factor, 1.0 = normal. Windows: Settings, Accessibility,
	/// Text size (100 to 225 percent). GNOME and the desktops built on its settings: the
	/// text scaling factor. macOS has no system-wide text size.
	/// </summary>
	public static double? TextScale() {
		try {
			if (OperatingSystem.IsWindows()) return WindowsTextScale();
			if (OperatingSystem.IsLinux()) return ParseFactor(GSettings("org.gnome.desktop.interface", "text-scaling-factor"));
		}
		catch { /* a preference that cannot be read is no preference */ }
		return null;
	}

	[SupportedOSPlatform("windows")]
	static double? WindowsTextScale() {
		using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
		return key?.GetValue("TextScaleFactor") is int percent ? FromPercent(percent) : null;
	}

	internal static double? FromPercent(int percent) => percent is >= 50 and <= 400 ? percent / 100.0 : null;

	/// <summary>gsettings prints a bare number ("1.25"); anything else is no answer.</summary>
	internal static double? ParseFactor(string? output) =>
		double.TryParse(output?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) && factor is >= 0.5 and <= 4
			? factor
			: null;

	/// <summary>gsettings prints "true" or "false".</summary>
	internal static bool? ParseBool(string? output) => output?.Trim() switch {
		"true" => true,
		"false" => false,
		_ => null,
	};

	internal static string? GSettings(string schema, string key) {
		try {
			using var process = Process.Start(new ProcessStartInfo("gsettings") {
				ArgumentList = { "get", schema, key },
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			});
			if (process == null) return null;
			// Read without blocking on it: a synchronous read would wait for a hung process
			// forever and make the timeout below unreachable (#865).
			var output = process.StandardOutput.ReadToEndAsync();
			if (!process.WaitForExit(2000)) {
				try { process.Kill(); } catch { }
				return null;
			}
			return process.ExitCode == 0 && output.Wait(500) ? output.Result : null;
		}
		catch {
			return null; // no gsettings on this desktop
		}
	}
}
