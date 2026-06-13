using System.Globalization;
using System.Windows.Data;

namespace PatchGuard.Converters;

/// <summary>Returns true when the bound value equals the converter parameter (case-insensitive).</summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Used for RadioButton IsChecked one-way; checking selects via Command instead.
        if (value is true && parameter is not null)
        {
            return parameter.ToString()!;
        }

        return Binding.DoNothing;
    }
}
