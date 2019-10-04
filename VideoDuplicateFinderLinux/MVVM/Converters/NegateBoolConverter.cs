using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace VideoDuplicateFinderLinux.MVVM.Converters
{
    public class NegateBoolConverter : IValueConverter

    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
    }
}
