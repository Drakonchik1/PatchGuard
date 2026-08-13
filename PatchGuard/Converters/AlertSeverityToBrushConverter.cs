using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PatchGuard.Models;

namespace PatchGuard.Converters;

public sealed class AlertSeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AlertSeverity severity)
        {
            return new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }

        var color = severity switch
        {
            AlertSeverity.Critical => Color.FromRgb(248, 113, 113),
            AlertSeverity.Warning => Color.FromRgb(251, 191, 36),
            _ => Color.FromRgb(96, 165, 250)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
