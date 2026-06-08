using CommunityToolkit.Mvvm.ComponentModel;

namespace PatchGuard.ViewModels;

public partial class AgentPanelState : ObservableObject
{
    public AgentPanelState(string role, string glyph)
    {
        Role = role;
        Glyph = glyph;
    }

    public string Role { get; }
    public string Glyph { get; }

    [ObservableProperty]
    private string _phaseLabel = "Idle";

    [ObservableProperty]
    private string _headline = "Standing by…";

    [ObservableProperty]
    private int _confidence;

    [ObservableProperty]
    private bool _isActive;
}
