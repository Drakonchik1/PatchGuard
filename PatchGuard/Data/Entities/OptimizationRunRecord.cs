namespace PatchGuard.Data.Entities;

public sealed class OptimizationRunRecord
{
    public int Id { get; set; }
    public DateTime RanAt { get; set; }
    public long BytesFreed { get; set; }
    public int StepsSucceeded { get; set; }
    public string Summary { get; set; } = string.Empty;
}
