using PatchGuard.Models;

namespace PatchGuard.Services;

public sealed class ScanSessionState
{
    public ScanScenario? SelectedScenario { get; set; }
    public List<Finding> Findings { get; } = [];
    public List<DiagnosticProgressItem> ProgressItems { get; } = [];
    public RepairGuide? Guide { get; set; }

    public void Reset()
    {
        SelectedScenario = null;
        Findings.Clear();
        ProgressItems.Clear();
        Guide = null;
    }
}
