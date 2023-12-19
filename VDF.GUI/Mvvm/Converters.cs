// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Globalization;
using ActiproSoftware.UI.Avalonia.Themes;
using Avalonia.Data.Converters;
using Avalonia.Media;
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
		public static readonly SolidColorBrush DefaultBrush = new();
		static Values() {
			App.Current!.TryGetResource(ThemeResourceKind.ControlBackgroundBrushSolidSuccess.ToResourceKey(), SettingsFile.Instance.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light, out var greenBrush);
			GreenBrush.Color = ((SolidColorBrush)greenBrush!).Color;
			App.Current.TryGetResource(ThemeResourceKind.ControlBackgroundBrushSolidDanger.ToResourceKey(), SettingsFile.Instance.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light, out var redBrush);
			RedBrush.Color = ((SolidColorBrush)redBrush!).Color;
			App.Current.TryGetResource(ThemeResourceKind.DefaultForegroundBrush.ToResourceKey(), SettingsFile.Instance.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light, out var defaultBrush);
			DefaultBrush.Color = ((SolidColorBrush)defaultBrush!).Color;
		}

	}
	public sealed class IsBestConverter : IValueConverter {
		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
			if (parameter is not null) {
				if (parameter.ToString() == "Size" && !SettingsFile.Instance.HighlightSize) {
					return Values.DefaultBrush;
				}

				if (parameter.ToString() == "Duration" && !SettingsFile.Instance.HighlightDuration) {
					return Values.DefaultBrush;
				}
			}

			if (!SettingsFile.Instance.HighlightBestResults && (bool)value!) {
				return Values.DefaultBrush;
			}

			if (!SettingsFile.Instance.HighlightNonBestResults && !(bool)value!) {
				return Values.DefaultBrush;
			}

			return (bool)value! ? Values.GreenBrush : Values.RedBrush;
		}

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
	public sealed class ExtraShortDateTimeConverter : IValueConverter {

		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ExtraShortDateTimeFormater.DateToString((DateTime)value!);

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
	}
}
