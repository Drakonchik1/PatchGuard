using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.Alerts;

public interface IAlertRuleEngine
{
    IReadOnlyList<Alert> Evaluate(HardwareSnapshot snapshot);
    IReadOnlyList<Alert> Evaluate(SensorSnapshotRecord snapshot);
}
