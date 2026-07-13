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
	/// <summary>
	/// Feeds the similarity chip's Classes.mid / Classes.low bindings; the actual
	/// colors live in theme-variant styles (Vdf* brushes) so they follow the theme.
	/// Parameter: "mid" or "low" (see <see cref="ResultsBadgeRules"/> for the bands).
	/// </summary>
	public sealed class SimilarityTierConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (value is not float similarity)
				return false;
			return (parameter as string) switch {
				"mid" => ResultsBadgeRules.IsMidSimilarity(similarity),
				"low" => ResultsBadgeRules.IsLowSimilarity(similarity),
				_ => false
			};
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public sealed class SimilarityToFormattedConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (value is not float similarity)
				return string.Empty;
			return ResultsBadgeRules.FormatSimilarity(similarity, CultureInfo.CurrentCulture);
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	/// <summary>Bitrate cell text ("8.4 Mb/s" / "192 kb/s"); empty for zero/unknown.</summary>
	public sealed class BitrateDisplayConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (value is not decimal kbs)
				return string.Empty;
			return ResultsBadgeRules.FormatBitrate(kbs, CultureInfo.CurrentCulture);
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

	/// <summary>
	/// True while the hover-diff shows "=" (both values equal). Drives the metric
	/// TextBlocks' Classes.eq binding, which mutes the best/worst coloring.
	/// </summary>
	public sealed class EqualDiffConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
			ResultsBadgeRules.IsEqualDiff(value as string);

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	/// <summary>
	/// Results-list sizing: [0] ResultsPreviewWidth, [1] ResultsCompactRows, then in any
	/// order a bool (ShowThumbnailColumn) and/or the row's composite Bitmap (Item.Thumbnail,
	/// whose aspect ratio drives the height once loaded). Parameter "row" yields the row
	/// height, anything else the thumbnail height. Logic lives in ResultsRowSizing.
	/// </summary>
	public sealed class ResultsRowSizingConverter : IMultiValueConverter {
		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) {
			double width = values.Count > 0 && values[0] is double d ? d : 160;
			bool compact = values.Count > 1 && values[1] is true;
			bool previewVisible = true;
			double thumbWidth = 0, thumbHeight = 0;
			for (int i = 2; i < values.Count; i++) {
				switch (values[i]) {
					case bool visible:
						previewVisible = visible;
						break;
					case Avalonia.Media.Imaging.Bitmap bmp:
						thumbWidth = bmp.Size.Width;
						thumbHeight = bmp.Size.Height;
						break;
				}
			}
			return string.Equals(parameter as string, "row", StringComparison.OrdinalIgnoreCase)
				? Utils.ResultsRowSizing.RowHeight(width, compact, previewVisible, thumbWidth, thumbHeight)
				: Utils.ResultsRowSizing.ImageHeight(width, compact, thumbWidth, thumbHeight);
		}
	}

	/// <summary>
	/// Generic bool switch for XAML resources: returns TrueValue/FalseValue, converting
	/// to the binding's target type (double for sizes, IBrush for colors) on demand.
	/// </summary>
	public sealed class BoolToValueConverter : IValueConverter {
		public object? TrueValue { get; set; }
		public object? FalseValue { get; set; }

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			object? result = value is true ? TrueValue : FalseValue;
			if (result == null) return null;
			if (targetType == typeof(double))
				return System.Convert.ToDouble(result, CultureInfo.InvariantCulture);
			if (typeof(IBrush).IsAssignableFrom(targetType) && result is not IBrush)
				return new SolidColorBrush(Color.Parse(result.ToString()!));
			return result;
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public sealed class FileNameConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
			value is string path && path.Length > 0 ? Path.GetFileName(path) : string.Empty;

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}

	public sealed class DirectoryPathConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (value is not string path || path.Length == 0) return string.Empty;
			try { return Path.GetDirectoryName(path) ?? path; }
			catch (Exception) { return path; }
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
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
