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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VDF.GUI.Controls;
using VDF.GUI.Utils;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// Readability guard (WCAG 2.1 AA, 1.4.3): text needs a contrast ratio of 4.5:1 against what
/// is behind it, 3:1 when it is large, and the glyph of an icon button 3:1 (1.4.11). Measured
/// on the real views in both themes from the resolved brushes: every solid background up the
/// visual tree is composited with the opacities on the way, the text color is laid over that
/// with its own accumulated opacity. Secondary text dimmed with Opacity is the usual offender,
/// and the light theme the usual victim: the same 0.55 that reads fine as light-on-dark drops
/// to about 3.3:1 as dark-on-white.
/// </summary>
public class ContrastTests {

	const double NormalText = 4.5;
	const double LargeTextOrIcon = 3.0;

	public static TheoryData<string, string> ViewsAndThemes() {
		var data = new TheoryData<string, string>();
		foreach (string view in new[] { "Setup", "Scanning", "Settings", "Results", "Log" })
			foreach (string theme in new[] { "Dark", "Light" })
				data.Add(view, theme);
		return data;
	}

	[Theory]
	[MemberData(nameof(ViewsAndThemes))]
	public Task Text_IsReadable(string viewName, string theme) => HeadlessUi.Run(() => {
		var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		var (view, cleanup) = Create(viewName);
		var window = new Window { Width = 1300, Height = 950, RequestedThemeVariant = variant, Content = view };
		window.Show();
		HeadlessUi.Pump();
		if (view is SettingsView settings) {
			foreach (var panel in settings.FindControl<StackPanel>("SectionsHost")!.Children.OfType<StackPanel>())
				panel.IsVisible = true;
			foreach (var row in settings.GetLogicalDescendants().OfType<SettingRow>())
				row.IsVisible = true;
			HeadlessUi.Pump();
		}
		try {
			var failures = Measure(window, variant);
			Assert.True(failures.Count == 0,
				$"{failures.Count} kind(s) of text below the required contrast in the {theme} theme:\n  " + string.Join("\n  ", failures));
		}
		finally {
			window.Close();
			cleanup();
		}
	});

	public static TheoryData<string, string> ViewsAndHighContrastThemes() {
		var data = new TheoryData<string, string>();
		foreach (string view in new[] { "Setup", "Scanning", "Settings", "Results", "Log" })
			foreach (string theme in new[] { "HighContrastDark", "HighContrastLight" })
				data.Add(view, theme);
		return data;
	}

	internal static ThemeVariant HighContrastVariant(string name) =>
		name == "HighContrastDark" ? VdfThemes.HighContrastDark : VdfThemes.HighContrastLight;

	/// <summary>
	/// High contrast is asked for by people for whom 4.5:1 is not enough. The two high
	/// contrast themes are held to the enhanced level, at rest and under the pointer.
	/// </summary>
	[Theory]
	[MemberData(nameof(ViewsAndHighContrastThemes))]
	public Task HighContrast_TextMeetsTheEnhancedLevel(string viewName, string theme) => HeadlessUi.Run(() => {
		var variant = HighContrastVariant(theme);
		var (view, cleanup) = Create(viewName);
		var window = new Window { Width = 1300, Height = 950, RequestedThemeVariant = variant, Content = view };
		window.Show();
		HeadlessUi.Pump();
		if (view is SettingsView settings) {
			foreach (var panel in settings.FindControl<StackPanel>("SectionsHost")!.Children.OfType<StackPanel>())
				panel.IsVisible = true;
			foreach (var row in settings.GetLogicalDescendants().OfType<SettingRow>())
				row.IsVisible = true;
			HeadlessUi.Pump();
		}
		try {
			foreach (string state in new[] { "at rest", ":pointerover" }) {
				if (state != "at rest") PutInState(window, state);
				var failures = Measure(window, variant, Enhanced);
				Assert.True(failures.Count == 0,
					$"{failures.Count} kind(s) of text below 7:1 in the {theme} theme, controls {state}:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", failures));
			}
		}
		finally {
			window.Close();
			cleanup();
		}
	});

	/// <summary>
	/// WCAG 1.4.11: what tells a field or a button from its surroundings needs 3:1. The
	/// ordinary themes draw hairlines of about 1.5:1 there, a look the high contrast themes
	/// give up: their outlines are what one navigates by when fills and tints do not register.
	/// </summary>
	[Theory]
	[InlineData("HighContrastDark")]
	[InlineData("HighContrastLight")]
	public Task HighContrast_FieldsAndButtons_HaveOutlinesOneCanSee(string theme) => HeadlessUi.Run(() => {
		var variant = HighContrastVariant(theme);
		var settings = new SettingsView { DataContext = new MainWindowVM() };
		var window = new Window { Width = 1300, Height = 950, RequestedThemeVariant = variant, Content = settings };
		window.Show();
		HeadlessUi.Pump();
		foreach (var panel in settings.FindControl<StackPanel>("SectionsHost")!.Children.OfType<StackPanel>())
			panel.IsVisible = true;
		foreach (var row in settings.GetLogicalDescendants().OfType<SettingRow>())
			row.IsVisible = true;
		HeadlessUi.Pump();
		try {
			var weak = new List<string>();
			int measured = 0;
			foreach (var control in window.GetVisualDescendants().OfType<TemplatedControl>()
						 .Where(c => c is Button or TextBox or ComboBox && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)) {
				var outline = control.GetVisualDescendants().OfType<Visual>().Prepend(control)
					.Select(v => v switch {
						Border b when b.BorderThickness != default => b.BorderBrush,
						ContentPresenter p when p.BorderThickness != default => p.BorderBrush,
						_ => null,
					})
					.OfType<ISolidColorBrush>().FirstOrDefault(b => b.Color.A > 0);
				if (outline == null) continue; // borderless by design (link-like buttons, the nav)
				measured++;
				Color behind = BackgroundBehind(control, variant);
				double ratio = Ratio(Blend(outline.Color, outline.Opacity, behind), behind);
				if (ratio < 3)
					weak.Add($"{control.GetType().Name} '{AutomationProperties.GetName(control)}' {ratio:0.00}:1 ({outline.Color} on {behind})");
			}
			Assert.True(measured > 10, $"only {measured} outlined controls found, the check is not looking at the view");
			Assert.True(weak.Count == 0, $"{weak.Count} outlines below 3:1 in {theme}:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", weak.Distinct().Take(15)));
		}
		finally {
			window.Close();
		}
	});

	/// <summary>The solid color behind an element: every background up the tree, composited with the opacities on the way.</summary>
	internal static Color BackgroundBehind(Visual element, ThemeVariant variant) {
		Color background = variant == ThemeVariant.Dark || variant == VdfThemes.HighContrastDark ? Colors.Black : Colors.White;
		double opacity = 1;
		foreach (var ancestor in element.GetVisualAncestors().Reverse()) {
			opacity *= ancestor.Opacity;
			if (BackgroundOf(ancestor) is ISolidColorBrush fill && fill.Color.A > 0)
				background = Blend(fill.Color, fill.Opacity * opacity, background);
		}
		return background;
	}

	[Theory]
	[MemberData(nameof(ViewsAndThemes))]
	public Task Text_IsReadable_OnControlsUnderThePointer(string viewName, string theme) => HeadlessUi.Run(() => {
		var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		var (view, cleanup) = Create(viewName);
		var window = new Window { Width = 1300, Height = 950, RequestedThemeVariant = variant, Content = view };
		window.Show();
		HeadlessUi.Pump();
		try {
			foreach (string state in new[] { ":pointerover", ":pressed" }) {
				PutInState(window, state);
				var failures = Measure(window, variant);
				Assert.True(failures.Count == 0,
					$"{failures.Count} kind(s) of text below the required contrast in the {theme} theme with controls {state}:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", failures));
			}
		}
		finally {
			window.Close();
			cleanup();
		}
	});

	[Theory]
	[InlineData("Dark", false)]
	[InlineData("Dark", true)]
	[InlineData("Light", false)]
	[InlineData("Light", true)]
	[InlineData("HighContrastDark", false)]
	[InlineData("HighContrastDark", true)]
	[InlineData("HighContrastLight", false)]
	[InlineData("HighContrastLight", true)]
	public Task Results_TheSelectedRow_IsReadable(string theme, bool isChecked) => HeadlessUi.Run(() => {
		bool highContrast = theme.StartsWith("HighContrast");
		var variant = highContrast ? HighContrastVariant(theme) : theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		var vm = ResultsFixture.CreatePopulatedViewModel();
		vm.Duplicates[1].Checked = isChecked;
		var window = new Window { Width = 1300, Height = 950, RequestedThemeVariant = variant, Content = new DuplicateResultsView { DataContext = vm } };
		window.Show();
		HeadlessUi.Pump();
		try {
			// The row a user works on (arrow onto it, Space) is the one with the least
			// contrast: selection changes the text color and the background, and on a
			// checked row the two tints add up under the colored metric values.
			var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
			list.SelectedIndex = 2; // group header, first file, then the second one
			HeadlessUi.Pump();
			Assert.Equal(isChecked, ((ResultsItemRow)list.SelectedItem!).Item.Checked);

			var failures = Measure(window, variant, highContrast ? Enhanced : null);
			Assert.True(failures.Count == 0,
				$"{failures.Count} kind(s) of text below the required contrast in the {theme} theme:\n  " + string.Join("\n  ", failures));
		}
		finally {
			window.Close();
		}
	});

	[Theory]
	[InlineData("Dark", false, false)]
	[InlineData("Dark", false, true)]
	[InlineData("Dark", true, false)]
	[InlineData("Dark", true, true)]
	[InlineData("Light", false, false)]
	[InlineData("Light", false, true)]
	[InlineData("Light", true, false)]
	[InlineData("Light", true, true)]
	public Task Results_TheRowUnderThePointer_IsReadable(string theme, bool isChecked, bool isSelected) => HeadlessUi.Run(() => {
		var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		var vm = ResultsFixture.CreatePopulatedViewModel();
		vm.Duplicates[1].Checked = isChecked;
		var window = new Window { Width = 1300, Height = 950, RequestedThemeVariant = variant, Content = new DuplicateResultsView { DataContext = vm } };
		window.Show();
		HeadlessUi.Pump();
		try {
			var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
			if (isSelected) list.SelectedIndex = 2;
			// What the pointer resting on the row does to its styles; a headless window has
			// no pointer to move there.
			((IPseudoClasses)list.ContainerFromIndex(2)!.Classes).Set(":pointerover", true);
			HeadlessUi.Pump();

			var failures = Measure(window, variant);
			Assert.True(failures.Count == 0,
				$"{failures.Count} kind(s) of text below the required contrast in the {theme} theme:\n  " + string.Join("\n  ", failures));
		}
		finally {
			window.Close();
		}
	});

	[Theory]
	[InlineData("Dark")]
	[InlineData("Light")]
	public Task Results_TheSelectedRow_IsMarkedByAFrame_NotByItsTintAlone(string theme) => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = new Window {
			Width = 1300, Height = 950, Content = new DuplicateResultsView { DataContext = vm },
			RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
		};
		window.Show();
		HeadlessUi.Pump();
		try {
			var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ResultsList");
			var row = (ListBoxItem)list.ContainerFromIndex(1)!;
			var other = (ListBoxItem)list.ContainerFromIndex(2)!;
			static Border Frame(ListBoxItem item) => item.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("selframe"));
			double heightBefore = row.Bounds.Height;

			list.SelectedIndex = 1;
			HeadlessUi.Pump();

			// A tint that leaves every text on the row readable is too quiet to carry the
			// selection by itself (WCAG 1.4.11 wants 3:1 for the state of a component), and
			// telling it from the red of a checked row would be a matter of hue alone.
			var frame = Assert.IsAssignableFrom<ISolidColorBrush>(Frame(row).BorderBrush);
			var tint = Assert.IsAssignableFrom<ISolidColorBrush>(row.GetVisualDescendants().OfType<ContentPresenter>().First().Background);
			Assert.True(Ratio(frame.Color, tint.Color) >= 3, $"frame {frame.Color} on {tint.Color} is {Ratio(frame.Color, tint.Color):0.00}:1");
			Assert.True(Frame(row).BorderThickness.Left >= 1);
			Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(Frame(other).BorderBrush).Color);
			Assert.False(Frame(row).IsHitTestVisible); // it lies over the row and must not take its clicks
			Assert.Equal(heightBefore, row.Bounds.Height); // rows are sized ahead of their thumbnails (#862)
		}
		finally {
			window.Close();
		}
	});

	[Theory]
	[InlineData("Dark")]
	[InlineData("Light")]
	public Task ResultsMetrics_BestAndTheRest_DifferByMoreThanColor(string theme) => HeadlessUi.Run(() => {
		var vm = ResultsFixture.CreatePopulatedViewModel();
		var window = new Window {
			Width = 1300, Height = 950, Content = new DuplicateResultsView { DataContext = vm },
			RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
		};
		window.Show();
		HeadlessUi.Pump();

		var metrics = window.GetVisualDescendants().OfType<TextBlock>()
			.Where(t => t.IsEffectivelyVisible && t.Classes.Contains("metric-diff") && !t.Classes.Contains("eq")).ToList();
		var best = metrics.Where(t => t.Classes.Contains("hi")).ToList();
		var rest = metrics.Where(t => t.Classes.Contains("lo")).ToList();

		// Green against red is the pair color-blind users cannot tell apart, and it was the
		// only difference: both were semibold.
		Assert.NotEmpty(best);
		Assert.NotEmpty(rest);
		Assert.All(best, t => Assert.Equal(FontWeight.SemiBold, t.FontWeight));
		Assert.All(rest, t => Assert.Equal(FontWeight.Normal, t.FontWeight));
		window.Close();
	});

	static (Control View, Action Cleanup) Create(string name) {
		switch (name) {
			case "Setup": {
				var vm = new MainWindowVM { ShowNoDuplicatesNotice = true };
				vm.SetupFolders.Add(new SetupFolderVM(@"D:\Videos\Holiday", isExcluded: false) { MetaText = "1,204 files" });
				vm.SetupFolders.Add(new SetupFolderVM(@"E:\Archive\Old", isExcluded: true) { MetaText = "Excluded" });
				return (new SetupView { DataContext = vm }, () => { });
			}
			case "Scanning": {
				var vm = new MainWindowVM { IsScanning = true, ScanStageText = "Scanning files 27/819", ScanCurrentFile = @"D:\Videos\Holiday\beach.mp4" };
				return (new ScanningView { DataContext = vm }, () => vm.IsScanning = false);
			}
			case "Settings":
				return (new SettingsView { DataContext = new MainWindowVM() }, () => { });
			case "Results": {
				var vm = ResultsFixture.CreatePopulatedViewModel();
				vm.Duplicates[1].Checked = true;
				vm.ToggleItemDetailsCommand.Execute(vm.Duplicates[0]).Subscribe();
				return (new DuplicateResultsView { DataContext = vm }, () => { });
			}
			case "Log":
				return (new LogView { DataContext = new MainWindowVM() }, () => { });
			default:
				throw new ArgumentOutOfRangeException(nameof(name));
		}
	}

	/// <summary>
	/// Puts every button, toggle, combo box and list item at once into the state the pointer
	/// resting or pressing on it puts it in; a headless window has no pointer to move there.
	/// Pressed is for buttons only, which can be held down: on a list item it is the
	/// fraction of a second before "selected", on a fill of the theme's that neither its
	/// own text tone nor white reaches 4.5:1 on.
	/// </summary>
	internal static void PutInState(Window window, string pseudoClass) {
		foreach (var control in window.GetVisualDescendants().OfType<TemplatedControl>()
					 .Where(c => c is Button || (pseudoClass != ":pressed" && c is ComboBox or ListBoxItem)))
			((IPseudoClasses)control.Classes).Set(pseudoClass, true);
		HeadlessUi.Pump();
	}

	/// <summary>WCAG 1.4.6 (AAA), what the high contrast themes are held to: 7:1, and 4.5:1 for large text and icons.</summary>
	internal static readonly (double Normal, double Large) Enhanced = (7.0, 4.5);

	internal static List<string> Measure(Window window, ThemeVariant variant, (double Normal, double Large)? thresholds = null) {
		var (normalText, largeTextOrIcon) = thresholds ?? (NormalText, LargeTextOrIcon);
		var worst = new Dictionary<string, (double Ratio, double Needed, string Sample, int Count)>();
		foreach (var element in window.GetVisualDescendants().OfType<Control>()) {
			if (!TryGetText(element, out string text, out IBrush? foreground, out double fontSize, out FontWeight weight))
				continue;
			if (!element.IsEffectivelyVisible || !element.IsEffectivelyEnabled || string.IsNullOrWhiteSpace(text))
				continue;
			if (foreground is not ISolidColorBrush brush)
				continue;

			bool hasWords = text.Any(char.IsLetterOrDigit);
			bool isIconOfAControl = !hasWords && element.FindAncestorOfType<Button>() != null;
			if (!hasWords && !isIconOfAControl)
				continue; // separators and ornaments carry no information

			Color background = BackgroundBehind(element, variant);
			double opacity = element.GetVisualAncestors().Aggregate(element.Opacity, (o, ancestor) => o * ancestor.Opacity);
			Color shown = Blend(brush.Color, brush.Opacity * opacity, background);

			bool large = fontSize >= 24 || (fontSize >= 18.66 && weight >= FontWeight.Bold);
			double needed = large || isIconOfAControl ? largeTextOrIcon : normalText;
			double ratio = Ratio(shown, background);
			if (ratio >= needed)
				continue;

			string key = $"{fontSize:0.#}px text {brush.Color} at opacity {opacity:0.00} on {background}";
			string sample = text.Trim().ReplaceLineEndings(" ");
			if (sample.Length > 36) sample = sample[..36] + "...";
			worst[key] = worst.TryGetValue(key, out var seen)
				? (Math.Min(seen.Ratio, ratio), needed, seen.Sample, seen.Count + 1)
				: (ratio, needed, sample, 1);
		}
		return worst.OrderBy(w => w.Value.Ratio)
			.Select(w => $"{w.Value.Ratio:0.00}:1 (needs {w.Value.Needed:0.0}) x{w.Value.Count}  {w.Key}  e.g. '{w.Value.Sample}'")
			.ToList();
	}

	static bool TryGetText(Control element, out string text, out IBrush? foreground, out double fontSize, out FontWeight weight) {
		switch (element) {
			case TextBlock block:
				text = block.Text ?? block.Inlines?.Text ?? string.Empty;
				foreground = block.Foreground;
				fontSize = block.FontSize;
				weight = block.FontWeight;
				return true;
			case MiddleEllipsisTextBlock custom:
				text = custom.Text ?? string.Empty;
				foreground = custom.Foreground;
				fontSize = custom.FontSize;
				weight = FontWeight.Normal;
				return true;
			default:
				text = string.Empty;
				foreground = null;
				fontSize = 0;
				weight = FontWeight.Normal;
				return false;
		}
	}

	static IBrush? BackgroundOf(Visual visual) => visual switch {
		Border border => border.Background,
		Panel panel => panel.Background,
		ContentPresenter presenter => presenter.Background,
		TemplatedControl control => control.Background,
		_ => null
	};

	static Color Blend(Color top, double alphaFactor, Color bottom) {
		double alpha = top.A / 255.0 * alphaFactor;
		byte Channel(byte t, byte b) => (byte)Math.Round(t * alpha + b * (1 - alpha));
		return Color.FromRgb(Channel(top.R, bottom.R), Channel(top.G, bottom.G), Channel(top.B, bottom.B));
	}

	static double Luminance(Color color) {
		static double Linear(byte value) {
			double s = value / 255.0;
			return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
		}
		return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
	}

	internal static double Ratio(Color a, Color b) {
		double la = Luminance(a), lb = Luminance(b);
		return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
	}
}
