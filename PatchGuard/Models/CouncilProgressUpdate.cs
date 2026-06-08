namespace PatchGuard.Models;

public sealed class CouncilProgressUpdate
{
    public string? StatusText { get; init; }
    public CouncilPhaseType? Phase { get; init; }
    public string? ActiveAgent { get; init; }
    public CouncilMessage? Message { get; init; }
    public string? ChiefVerdict { get; init; }
    public AgentPanelSnapshot? Panel { get; init; }
}

public sealed class AgentPanelSnapshot
{
    public required string Role { get; init; }
    public required string PhaseLabel { get; init; }
    public required string Headline { get; init; }
    public int Confidence { get; init; }
    public bool IsActive { get; init; }
}
