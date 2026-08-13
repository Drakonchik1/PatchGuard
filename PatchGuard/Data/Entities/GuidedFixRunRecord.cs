namespace PatchGuard.Data.Entities;

public sealed class GuidedFixRunRecord
{
    public int Id { get; set; }
    public DateTime RanAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string PlanTitle { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public int StepsSucceeded { get; set; }
    public int StepsTotal { get; set; }
    public long BytesFreed { get; set; }
    public bool Verified { get; set; }
    public string? LinkedScanScenario { get; set; }
    public string Summary { get; set; } = string.Empty;
}
