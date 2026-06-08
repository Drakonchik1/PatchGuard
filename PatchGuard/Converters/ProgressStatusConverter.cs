using System.Globalization;
using System.Windows.Data;
using PatchGuard.Models;

namespace PatchGuard.Converters;

public sealed class ProgressStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DiagnosticProgressStatus status)
        {
            return "—";
        }

        return status switch
        {
            DiagnosticProgressStatus.Pending => "Pending",
            DiagnosticProgressStatus.Running => "Running…",
            DiagnosticProgressStatus.Completed => "Done",
            DiagnosticProgressStatus.Skipped => "Planned",
            DiagnosticProgressStatus.Failed => "Failed",
            _ => status.ToString()
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
