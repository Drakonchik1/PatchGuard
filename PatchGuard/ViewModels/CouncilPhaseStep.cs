using CommunityToolkit.Mvvm.ComponentModel;

namespace PatchGuard.ViewModels;

public partial class CouncilPhaseStep : ObservableObject
{
    public required string Name { get; init; }

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isComplete;
}
