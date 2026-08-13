using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Josha.Converters
{
    // Maps a 0-100 usage percentage to the theme's status brushes so CPU/memory
    // readouts in the status bar go green -> amber -> red as load rises.
    // Resource lookup happens on every conversion (not cached) so it stays
    // correct across a live theme switch.
    [ValueConversion(typeof(double), typeof(Brush))]
    public sealed class UsagePercentToBrushConverter : IValueConverter
    {
        private const double WarningThreshold = 60;
        private const double CriticalThreshold = 85;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var percent = value is double d ? d : 0;

            var key = percent >= CriticalThreshold ? "Brush.Status.Error"
                     : percent >= WarningThreshold  ? "Brush.Status.Pending"
                     : "Brush.Status.Ok";

            return Application.Current?.TryFindResource(key) as Brush;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
