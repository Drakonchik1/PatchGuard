using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PatchGuard.Converters;

/// <summary>
/// Maps a numeric metric to a status colour. Pass ConverterParameter="temp" for
/// temperature thresholds (warn 85, critical 95) or "load" for utilisation
/// thresholds (warn 80, critical 95). Defaults to load thresholds.
/// </summary>
public sealed class MetricBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Good = new(Color.FromRgb(48, 209, 88));
    private static readonly SolidColorBrush Warn = new(Color.FromRgb(255, 214, 10));
    private static readonly SolidColorBrush Critical = new(Color.FromRgb(255, 69, 58));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double v)
        {
            return Good;
        }

        var mode = parameter?.ToString()?.ToLowerInvariant();
        var (warnAt, critAt) = mode == "temp" ? (85.0, 95.0) : (80.0, 95.0);

        if (v >= critAt)
        {
            return Critical;
        }

        return v >= warnAt ? Warn : Good;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
