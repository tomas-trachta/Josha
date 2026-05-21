using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Josha.Converters
{
    // Direct bool→Brush lookup for the pane-tab and active-pane border. Bypasses
    // WPF's DataTrigger revert path, which has been observed to leave
    // BorderBrush stuck on Brush.Accent after IsActive flips back to false on
    // chained triggers in BasedOn styles (the "both panes look active" bug).
    [ValueConversion(typeof(bool), typeof(Brush))]
    public sealed class ActiveToBorderBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var key = value is bool b && b ? "Brush.Accent" : "Brush.OutlineSubtle";
            return Application.Current?.TryFindResource(key) as Brush;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    // Companion: paints the top accent strip on the focused-pane tab. Transparent
    // when IsActive=false so inactive tabs blend with the chip background.
    [ValueConversion(typeof(bool), typeof(Brush))]
    public sealed class ActiveToAccentStripConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return Application.Current?.TryFindResource("Brush.Accent") as Brush;
            return Brushes.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
