namespace PatchGuard.Models;

public sealed class ScenarioOption
{
    public required ScanScenario Scenario { get; init; }
    public string Title => Scenario.GetTitle();
    public string Description => Scenario.GetDescription();
}
