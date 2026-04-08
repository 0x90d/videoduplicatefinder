// /*
//     Copyright (C) 2025 0x90d
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

using System.Globalization;
using System.Text;
using ActiproSoftware.UI.Avalonia.Themes;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using VDF.GUI.Data;

namespace VDF.GUI.Mvvm {
	public sealed class NegateBoolConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !(bool)value!;

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => !(bool)value!;
	}
	static class Values {
		public static readonly SolidColorBrush GreenBrush = new();
		public static readonly SolidColorBrush RedBrush = new();
		static Values() {
			App.Current!.TryGetResource(ThemeResourceKind.ControlBackgroundBrushSolidSuccess.ToResourceKey(), SettingsFile.Instance.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light, out var greenBrush);
			GreenBrush.Color = ((ImmutableSolidColorBrush)greenBrush!).Color;
			App.Current.TryGetResource(ThemeResourceKind.ControlBackgroundBrushSolidDanger.ToResourceKey(), SettingsFile.Instance.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light, out var redBrush);
			RedBrush.Color = ((ImmutableSolidColorBrush)redBrush!).Color;
		}

	}
	public sealed class IsBestConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
			(bool)value! ? Values.GreenBrush : Values.RedBrush;

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	static class ExtraShortDateTimeFormater {
		static readonly string FormatString;
		public static string DateToString(DateTime value) => String.Format(FormatString, value);
		static ExtraShortDateTimeFormater() {
			// "g" would be something like "4/10/2008 6:30 AM"
			// If not using AM/PM notation the format would be: "d  hh:mm"
			// And to keep it as short as possible, the year is shortened to two digits but the date keeps the culture specific order:
			FormatString = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
			FormatString = FormatString.Replace("yyyy", "yy") + " hh:mm";
			FormatString = $"{{0:{FormatString}}}";
		}
	}
	public sealed class SimilarityToBadgeBackgroundConverter : IValueConverter {
		static readonly SolidColorBrush HighBrush = new(Color.Parse("#1B5E20"), 0.85);
		static readonly SolidColorBrush MediumBrush = new(Color.Parse("#0D47A1"), 0.85);
		static readonly SolidColorBrush LowBrush = new(Color.Parse("#E65100"), 0.85);

		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (value is not float similarity)
				return LowBrush;
			return similarity >= 99f ? HighBrush : similarity >= 90f ? MediumBrush : LowBrush;
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public sealed class SimilarityToBadgeForegroundConverter : IValueConverter {
		static readonly SolidColorBrush HighBrush = new(Color.Parse("#A5D6A7"));
		static readonly SolidColorBrush MediumBrush = new(Color.Parse("#90CAF9"));
		static readonly SolidColorBrush LowBrush = new(Color.Parse("#FFCC80"));

		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (value is not float similarity)
				return LowBrush;
			return similarity >= 99f ? HighBrush : similarity >= 90f ? MediumBrush : LowBrush;
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public sealed class SimilarityToFormattedConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (value is not float similarity)
				return string.Empty;
			return $"{similarity:F1}%";
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public sealed class ExtraShortDateTimeConverter : IValueConverter {

		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ExtraShortDateTimeFormater.DateToString((DateTime)value!);

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}
	public sealed class ShowSkeletonConverter : IMultiValueConverter {
		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) {
			// values[0] = Thumbnail (null/UnsetValue means loading), values[1] = GeneratePreviewThumbnails (bool)
			if (values.Count < 2) return false;
			bool hasThumbnail = values[0] is Avalonia.Media.Imaging.Bitmap;
			bool generateEnabled = values[1] is true;
			return !hasThumbnail && generateEnabled;
		}
	}

	public sealed class MetricDisplayConverter : IMultiValueConverter {
		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) {
			if (values.Count >= 2 && values[1] is string diff && !string.IsNullOrEmpty(diff))
				return diff;
			var val = values[0];
			if (val == null) return string.Empty;
			// Apply format string from ConverterParameter if provided
			if (parameter is string fmt && !string.IsNullOrEmpty(fmt))
				return string.Format(fmt, val);
			return val.ToString() ?? string.Empty;
		}
	}

	public sealed class MetricDiffForegroundConverter : IMultiValueConverter {
		static readonly SolidColorBrush GreenBrush = new(Color.Parse("#4CAF50"));
		static readonly SolidColorBrush RedBrush = new(Color.Parse("#F44336"));
		static readonly SolidColorBrush GrayBrush = new(Color.Parse("#9E9E9E"));

		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) {
			// values[0] = isBest (bool), values[1] = diff text (string? or null)
			if (values.Count >= 2 && values[1] is string diff && !string.IsNullOrEmpty(diff)) {
				if (diff == "=")
					return GrayBrush;
				// Keep the same best/worst coloring — isBest already knows which is better
				if (values[0] is bool best)
					return best ? GreenBrush : RedBrush;
			}
			if (values[0] is bool isBest)
				return isBest ? Values.GreenBrush : Values.RedBrush;
			return null;
		}
	}

	public sealed class PathDisplaySanitizer : IValueConverter {
		public static readonly PathDisplaySanitizer Instance = new();

		public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) {
			// Always return a non-null string
			if (value is null) return string.Empty;
			var s = value.ToString() ?? string.Empty;

			// 1) Normalize to NFC (helps with composed characters)
			s = s.Normalize(NormalizationForm.FormC);

			// 2) Strip bidi/format control chars that can destabilize wrapping
			//    Cf = "Other, Format" (includes LRM/RLM/LRE/RLE/RLO/PDF, ZWJ/ZWNJ, etc.)
			s = RemoveFormatControls(s);

			// 3) Fix or drop isolated surrogate halves
			s = RemoveIsolatedSurrogates(s);

			return s;
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
			=> Avalonia.Data.BindingOperations.DoNothing;

		// --- helpers ---
		private static string RemoveFormatControls(string s) {
			// Fast path: no control char present
			bool hasCf = false;
			foreach (var ch in s) {
				if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Format) { hasCf = true; break; }
			}
			if (!hasCf) return s;

			var sb = new System.Text.StringBuilder(s.Length);
			foreach (var ch in s) {
				if (char.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.Format)
					sb.Append(ch);
			}
			return sb.ToString();
		}

		private static string RemoveIsolatedSurrogates(string s) {
			var sb = new System.Text.StringBuilder(s.Length);
			for (int i = 0; i < s.Length; i++) {
				char c = s[i];
				if (!char.IsSurrogate(c)) { sb.Append(c); continue; }

				// High surrogate must be followed by low surrogate
				if (char.IsHighSurrogate(c)) {
					if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) {
						sb.Append(c);
						sb.Append(s[i + 1]);
						i++; // skip the low surrogate
					}
					// else: drop isolated high surrogate
				}
				// Low surrogate without preceding high surrogate → drop
			}
			return sb.ToString();
		}
	}
}
