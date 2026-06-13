using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PatchGuard.Models;

namespace PatchGuard.Converters;

public sealed class OptimizationStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OptimizationStatus status)
        {
            return "—";
        }

        return status switch
        {
            OptimizationStatus.Pending => "Pending",
            OptimizationStatus.Running => "Working…",
            OptimizationStatus.Success => "Done",
            OptimizationStatus.Skipped => "Skipped",
            OptimizationStatus.Failed => "Failed",
            _ => status.ToString()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class OptimizationStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = (value as OptimizationStatus?) switch
        {
            OptimizationStatus.Success => Color.FromRgb(48, 209, 88),
            OptimizationStatus.Running => Color.FromRgb(10, 132, 255),
            OptimizationStatus.Failed => Color.FromRgb(255, 69, 58),
            OptimizationStatus.Skipped => Color.FromRgb(142, 142, 147),
            _ => Color.FromRgb(142, 142, 147)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
