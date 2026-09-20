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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VDF.GUI.Utils;

/// <summary>
/// What the user told their operating system about how they need things shown, as far as
/// Avalonia does not report it itself (it reports light or dark and high contrast). Every
/// query answers null where the system has no such setting or cannot be asked, and null
/// always means "leave things as they are".
/// </summary>
static partial class SystemPreferences {

	/// <summary>
	/// False when the user switched animations off in their system. Windows: Settings,
	/// Accessibility, Visual effects, Animation effects. macOS: Accessibility, Display,
	/// Reduce motion. GNOME and the desktops built on its settings: enable-animations.
	/// People turn them off because movement on screen makes them dizzy or keeps pulling
	/// their attention away.
	/// </summary>
	public static bool? AnimationsEnabled() {
		try {
			if (OperatingSystem.IsWindows()) return WindowsClientAreaAnimation();
			if (OperatingSystem.IsMacOS()) return MacReduceMotion() is { } reduce ? !reduce : null;
			if (OperatingSystem.IsLinux()) return ParseBool(GSettings("org.gnome.desktop.interface", "enable-animations"));
		}
		catch { /* a preference that cannot be read is no preference */ }
		return null;
	}

	const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

	[SupportedOSPlatform("windows")]
	static bool? WindowsClientAreaAnimation() =>
		SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, out int enabled, 0) ? enabled != 0 : null;

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

	// [[NSWorkspace sharedWorkspace] accessibilityDisplayShouldReduceMotion]. The selector is
	// asked for first: sending one an object does not know raises an Objective-C exception,
	// which managed code cannot catch.
	[SupportedOSPlatform("macos")]
	static bool? MacReduceMotion() {
		IntPtr workspaceClass = objc_getClass("NSWorkspace");
		if (workspaceClass == IntPtr.Zero) return null;
		IntPtr workspace = SendPointer(workspaceClass, sel_registerName("sharedWorkspace"));
		if (workspace == IntPtr.Zero) return null;
		IntPtr selector = sel_registerName("accessibilityDisplayShouldReduceMotion");
		if (!SendBoolWithSelector(workspace, sel_registerName("respondsToSelector:"), selector)) return null;
		return SendBool(workspace, selector);
	}

	const string ObjC = "/usr/lib/libobjc.dylib";

	[LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
	private static partial IntPtr objc_getClass(string name);

	[LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
	private static partial IntPtr sel_registerName(string name);

	[LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
	private static partial IntPtr SendPointer(IntPtr receiver, IntPtr selector);

	[LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static partial bool SendBool(IntPtr receiver, IntPtr selector);

	[LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static partial bool SendBoolWithSelector(IntPtr receiver, IntPtr selector, IntPtr argument);

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
