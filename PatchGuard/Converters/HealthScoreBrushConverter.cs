using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PatchGuard.Converters;

public sealed class HealthScoreBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var score = value switch
        {
            int i => i,
            double d => (int)d,
            _ => 100
        };

        return score switch
        {
            >= 85 => Application.Current.FindResource("GoodBrush") as Brush ?? Brushes.LimeGreen,
            >= 70 => Application.Current.FindResource("InfoBrush") as Brush ?? Brushes.DodgerBlue,
            >= 50 => Application.Current.FindResource("WarnBrush") as Brush ?? Brushes.Gold,
            _ => Application.Current.FindResource("CriticalBrush") as Brush ?? Brushes.Red
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
