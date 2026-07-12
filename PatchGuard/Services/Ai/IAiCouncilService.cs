using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public interface IAiCouncilService
{
    Task<RepairGuide> BuildGuideAsync(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        IProgress<CouncilProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowExternalServices = false);
}
