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
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VDF.GUI.Controls;
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

	static List<string> Measure(Window window, ThemeVariant variant) {
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

			Color background = variant == ThemeVariant.Dark ? Colors.Black : Colors.White;
			double opacity = 1;
			foreach (var ancestor in element.GetVisualAncestors().Reverse()) {
				opacity *= ancestor.Opacity;
				if (BackgroundOf(ancestor) is ISolidColorBrush fill && fill.Color.A > 0)
					background = Blend(fill.Color, fill.Opacity * opacity, background);
			}
			opacity *= element.Opacity;
			Color shown = Blend(brush.Color, brush.Opacity * opacity, background);

			bool large = fontSize >= 24 || (fontSize >= 18.66 && weight >= FontWeight.Bold);
			double needed = large || isIconOfAControl ? LargeTextOrIcon : NormalText;
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
