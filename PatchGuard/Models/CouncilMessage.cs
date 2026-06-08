namespace PatchGuard.Models;

public enum CouncilMessageKind
{
    Debate,
    ChiefVerdict
}

public enum CouncilPhaseType
{
    Analysis,
    Research,
    Debate,
    Rebuttal,
    Verdict
}

public sealed class CouncilMessage
{
    public required string AgentRole { get; init; }
    public required string Content { get; init; }
    public required string Headline { get; init; }
    public CouncilPhaseType Phase { get; init; }
    public int Round { get; init; }
    public int Confidence { get; init; }
    public CouncilMessageKind Kind { get; init; } = CouncilMessageKind.Debate;
}
