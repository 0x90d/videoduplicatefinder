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
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using VDF.GUI.Data;

namespace VDF.GUI.Utils;

/// <summary>
/// How the app looks follows what the user told their operating system: light or dark, the
/// size of text, and (further down) contrast and motion. People set these once, for their
/// eyes, in one place, and an app that ignores them makes them do it again in every app, if
/// the app lets them at all. The settings only ever override towards what a user asks for.
/// Every window calls <see cref="Attach"/> from its constructor.
/// </summary>
static class Appearance {
	static bool started;
	static double? systemTextScale;
	static bool? systemAnimations;
	static bool? systemHighContrastOverride;
	static double appliedScale = 1.0;
	static readonly List<Window> windows = new();

	/// <summary>True when the app is dark right now, whether by choice or because the system is.</summary>
	public static bool IsDarkNow => IsDark(SettingsFile.Instance.ThemeMode, SystemColors().ThemeVariant);

	internal static bool IsDark(ThemeMode mode, PlatformThemeVariant system) => mode switch {
		ThemeMode.Dark => true,
		ThemeMode.Light => false,
		_ => system == PlatformThemeVariant.Dark,
	};

	/// <summary>True when the app shows one of its high contrast themes right now.</summary>
	public static bool HighContrastNow => ResolveHighContrast(SettingsFile.Instance.AlwaysHighContrast,
		systemHighContrastOverride ?? SystemColors().ContrastPreference == ColorContrastPreference.High);

	internal static bool ResolveHighContrast(bool always, bool systemAsksForIt) => always || systemAsksForIt;

	/// <summary>
	/// High contrast is not a third theme next to dark and light but a version of each: a
	/// user with a light high contrast scheme gets the light one. The variants inherit from
	/// Dark and Light, so whatever they do not redefine stays what it was.
	/// </summary>
	internal static ThemeVariant ResolveVariant(bool dark, bool highContrast) =>
		highContrast ? dark ? VdfThemes.HighContrastDark : VdfThemes.HighContrastLight
		: dark ? ThemeVariant.Dark : ThemeVariant.Light;

	/// <summary>The factor everything in a window is scaled by right now.</summary>
	public static double ScaleNow => ResolveScale(SettingsFile.Instance.UiScalePercent, systemTextScale);

	/// <param name="percent">The setting: 0 follows the system, anything else is that percentage.</param>
	/// <param name="systemTextScale">The system's text size factor, null where there is none to follow.</param>
	internal static double ResolveScale(int percent, double? systemTextScale) =>
		Math.Clamp(percent > 0 ? percent / 100.0 : systemTextScale ?? 1.0, 0.5, 3.0);

	/// <summary>True when animations that run by themselves should stand still.</summary>
	public static bool ReduceMotionNow => ResolveReduceMotion(SettingsFile.Instance.AlwaysReduceMotion, systemAnimations);

	/// <param name="animationsEnabled">The system's answer, null where there is none to ask.</param>
	internal static bool ResolveReduceMotion(bool always, bool? animationsEnabled) => always || animationsEnabled == false;

	static PlatformColorValues SystemColors() =>
		Application.Current?.PlatformSettings?.GetColorValues() ?? new PlatformColorValues();

	public static void Attach(Window window) {
		Start();
		if (windows.Contains(window)) return;
		windows.Add(window);
		window.Closed += (_, _) => windows.Remove(window);
		// Nothing reports a change of the system's text size: asked again whenever a window
		// comes to the front, which is when the user returns from the system settings.
		window.Activated += (_, _) => RefreshSystemPreferences();
		window.Opened += (_, _) => FitToScreen(window);
		ScaleWindow(window, 1.0, appliedScale);
		window.Classes.Set("reduce-motion", ReduceMotionNow);
	}

	static void Start() {
		if (started || Application.Current is not { } app) return;
		started = true;
		systemTextScale = SystemPreferences.TextScale();
		systemAnimations = SystemPreferences.AnimationsEnabled();
		appliedScale = ScaleNow;
		if (app.PlatformSettings is { } platform)
			platform.ColorValuesChanged += (_, _) => Apply(); // the user switched the system while the app runs
		SettingsFile.Instance.PropertyChanged += (_, e) => {
			if (e.PropertyName is nameof(SettingsFile.ThemeMode) or nameof(SettingsFile.AlwaysHighContrast)) Apply();
			if (e.PropertyName == nameof(SettingsFile.UiScalePercent)) Rescale();
			if (e.PropertyName == nameof(SettingsFile.AlwaysReduceMotion)) ApplyMotion();
		};
		Apply();
	}

	// Set on the application, not per window: the managed window chrome (caption bar,
	// titlebar buttons) resolves its brushes against the application's variant, and every
	// dialog follows along without code of its own.
	internal static void Apply() {
		if (Application.Current is { } app)
			app.RequestedThemeVariant = ResolveVariant(IsDarkNow, HighContrastNow);
	}

	/// <summary>For tests: what the system is taken to ask for, null to ask the system again.</summary>
	internal static void SetSystemHighContrast(bool? highContrast) {
		systemHighContrastOverride = highContrast;
		Apply();
	}

	static bool refreshing;

	static void RefreshSystemPreferences() {
		if (!OperatingSystem.IsLinux()) {
			// In-process calls; macOS wants its AppKit objects asked on the main thread.
			SetSystemAnswers(SystemPreferences.TextScale(), SystemPreferences.AnimationsEnabled());
			return;
		}
		if (refreshing) return;
		refreshing = true;
		// Off the UI thread: on Linux the answers come from child processes.
		Task.Run(() => (Scale: SystemPreferences.TextScale(), Animations: SystemPreferences.AnimationsEnabled()))
			.ContinueWith(query => Dispatcher.UIThread.Post(() => {
				refreshing = false;
				if (query.IsCompletedSuccessfully)
					SetSystemAnswers(query.Result.Scale, query.Result.Animations);
			}));
	}

	static void SetSystemAnswers(double? textScale, bool? animations) {
		systemTextScale = textScale;
		systemAnimations = animations;
		Rescale();
		ApplyMotion();
	}

	/// <summary>For tests: what the system is taken to have answered.</summary>
	internal static void SetSystemTextScale(double? factor) => SetSystemAnswers(factor, systemAnimations);

	/// <summary>For tests: what the system is taken to have answered.</summary>
	internal static void SetSystemAnimations(bool? enabled) => SetSystemAnswers(systemTextScale, enabled);

	// Styles that move something only apply outside this class (_Animations.xaml, _Buttons.xaml).
	static void ApplyMotion() {
		bool reduce = ReduceMotionNow;
		foreach (var window in windows)
			window.Classes.Set("reduce-motion", reduce);
	}

	/// <summary>Brings every open window to the scale that applies now.</summary>
	static void Rescale() {
		double now = ScaleNow;
		if (now == appliedScale) return;
		foreach (var window in windows.ToArray()) {
			ScaleWindow(window, appliedScale, now);
			if (window.IsVisible) FitToScreen(window);
		}
		appliedScale = now;
	}

	/// <summary>A window designed for 100 percent can outgrow a small screen at 200: never larger than the work area.</summary>
	static void FitToScreen(Window window) {
		var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
		if (screen == null || screen.Scaling <= 0) return;
		double width = screen.WorkingArea.Width / screen.Scaling, height = screen.WorkingArea.Height / screen.Scaling;
		if (width <= 0 || height <= 0) return;
		if (window.MinWidth > width) window.MinWidth = width;
		if (window.MinHeight > height) window.MinHeight = height;
		if (!double.IsNaN(window.Width) && window.Width > width) window.Width = width;
		if (!double.IsNaN(window.Height) && window.Height > height) window.Height = height;
	}

	/// <summary>
	/// Scales everything in the window: its content through a layout transform, and the sizes
	/// the window was designed with along with it, or a dialog laid out for 100 percent
	/// would cut off its own content. Scaling the whole interface rather than the fonts is
	/// deliberate: the results list has fixed column widths, and text alone growing would be
	/// clipped by them. Menus, dropdowns and tooltips follow through the Popup style in
	/// _Accessibility.xaml.
	/// </summary>
	static void ScaleWindow(Window window, double from, double to) {
		if (window.Content is LayoutTransformControl existing)
			existing.LayoutTransform = to == 1.0 ? null : new ScaleTransform(to, to);
		else if (to != 1.0 && window.Content is Control content) {
			window.Content = null;
			window.Content = new LayoutTransformControl { Child = content, LayoutTransform = new ScaleTransform(to, to) };
		}
		else if (to != 1.0) {
			// Attached before the XAML was loaded: wait for the content to arrive.
			void OnContent(object? s, AvaloniaPropertyChangedEventArgs e) {
				if (e.Property != ContentControl.ContentProperty || window.Content is not Control || window.Content is LayoutTransformControl) return;
				window.PropertyChanged -= OnContent;
				ScaleWindow(window, 1.0, appliedScale);
			}
			window.PropertyChanged += OnContent;
			return;
		}

		double ratio = to / from;
		if (ratio == 1.0) return;
		// The maximum first when growing and last when shrinking, so that the size never exceeds it.
		if (ratio > 1) {
			if (window.MaxWidth is > 0 and < double.PositiveInfinity) window.MaxWidth *= ratio;
			if (window.MaxHeight is > 0 and < double.PositiveInfinity) window.MaxHeight *= ratio;
		}
		if (!double.IsNaN(window.Width)) window.Width *= ratio;
		if (!double.IsNaN(window.Height)) window.Height *= ratio;
		if (window.MinWidth > 0) window.MinWidth *= ratio;
		if (window.MinHeight > 0) window.MinHeight *= ratio;
		if (ratio < 1) {
			if (window.MaxWidth is > 0 and < double.PositiveInfinity) window.MaxWidth *= ratio;
			if (window.MaxHeight is > 0 and < double.PositiveInfinity) window.MaxHeight *= ratio;
		}
	}
}
