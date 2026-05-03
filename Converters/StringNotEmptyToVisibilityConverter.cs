using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Josha.Converters
{
    /// <summary>
    /// Visible when the bound string is non-null and non-empty; Collapsed otherwise.
    /// Used by the toolbar's "Ctrl+X" key-binding badge so the pill disappears
    /// entirely when no key is bound rather than rendering an empty chip.
    /// </summary>
    public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
