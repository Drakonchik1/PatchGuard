using CommunityToolkit.Mvvm.ComponentModel;

namespace PatchGuard.Models;

public enum DiagnosticProgressStatus
{
    Pending,
    Running,
    Completed,
    Skipped,
    Failed
}

public partial class DiagnosticProgressItem : ObservableObject
{
    public required string ModuleName { get; init; }

    [ObservableProperty]
    private DiagnosticProgressStatus _status = DiagnosticProgressStatus.Pending;

    [ObservableProperty]
    private string _message = string.Empty;
}
