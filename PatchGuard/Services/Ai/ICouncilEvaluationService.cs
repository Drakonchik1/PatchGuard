using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public interface ICouncilEvaluationService
{
    Task SaveAsync(
        ScanScenario scenario,
        RepairGuide guide,
        TimeSpan latency,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CouncilEvaluationRecord>> GetRecentAsync(
        int take = 10,
        CancellationToken cancellationToken = default);
}
