namespace PatchGuard.Data.Entities;

public sealed class GameFpsEntry
{
    public int Id { get; set; }
    public string GameName { get; set; } = string.Empty;
    public int Fps { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? Note { get; set; }
}
