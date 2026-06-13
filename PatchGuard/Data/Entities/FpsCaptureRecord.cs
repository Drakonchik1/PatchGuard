namespace PatchGuard.Data.Entities;

public sealed class FpsCaptureRecord
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double AverageFps { get; set; }
    public double OnePercentLowFps { get; set; }
    public double PointOnePercentLowFps { get; set; }
    public int FrameCount { get; set; }
    public DateTime CapturedAt { get; set; }
}
