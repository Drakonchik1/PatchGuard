using System.Windows;

namespace PatchGuard.Services.Platform;

public sealed class WpfUserConfirmationService : IUserConfirmationService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
