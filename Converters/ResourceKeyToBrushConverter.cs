using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Josha.Converters
{
    [ValueConversion(typeof(string), typeof(Brush))]
    public sealed class ResourceKeyToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string key || string.IsNullOrEmpty(key)) return null;
            return Application.Current?.TryFindResource(key) as Brush;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
