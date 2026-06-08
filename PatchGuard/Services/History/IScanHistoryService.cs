using PatchGuard.Models;

namespace PatchGuard.Services.History;

public interface IScanHistoryService
{
    Task SaveScanAsync(ScanScenario scenario, IReadOnlyList<Finding> findings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScanHistoryEntry>> GetRecentScansAsync(int take = 6, CancellationToken cancellationToken = default);
}
