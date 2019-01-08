using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoDuplicateFinderWindows.MVVM
{
    sealed class SizeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DuplicateFinderEngine.Utils.BytesToString((long)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public sealed class NegateBooleanConverter : IValueConverter
    {
        object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
    }
    public sealed class InvertBooleanToVisibilityConverter : IValueConverter
    {
        object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture) => (bool)value ? Visibility.Collapsed : Visibility.Visible;

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
