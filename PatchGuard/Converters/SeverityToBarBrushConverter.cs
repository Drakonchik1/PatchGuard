using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PatchGuard.Models;

namespace PatchGuard.Converters;

public sealed class SeverityToBarBrushConverter : IValueConverter
{
    public static SeverityToBarBrushConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FindingSeverity severity)
        {
            return new SolidColorBrush(Color.FromRgb(230, 57, 70));
        }

        var color = severity switch
        {
            FindingSeverity.Critical => Color.FromRgb(255, 59, 48),
            FindingSeverity.Warning => Color.FromRgb(255, 149, 0),
            _ => Color.FromRgb(100, 210, 255)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
