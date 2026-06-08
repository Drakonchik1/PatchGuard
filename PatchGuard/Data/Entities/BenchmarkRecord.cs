namespace PatchGuard.Data.Entities;

public sealed class BenchmarkRecord
{
    public int Id { get; set; }
    public DateTime RecordedAt { get; set; }
    public double SyntheticFps { get; set; }
    public string GpuName { get; set; } = string.Empty;
}
