using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.History;

public interface ISensorHistoryService
{
    /// <summary>Persists a numeric snapshot and prunes rows older than retention.</summary>
    Task SaveSnapshotAsync(HardwareSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SensorSnapshotRecord>> GetRecentAsync(
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<SensorSnapshotRecord?> GetLatestAsync(CancellationToken cancellationToken = default);
}
