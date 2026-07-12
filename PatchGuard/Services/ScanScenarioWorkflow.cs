using PatchGuard.Models;
using PatchGuard.Services.Navigation;
using PatchGuard.ViewModels;

namespace PatchGuard.Services;

/// <summary>Provides the shared diagnostic scenario list and starts a selected scan.</summary>
public static class ScanScenarioWorkflow
{
    public static IReadOnlyList<ScenarioOption> CreateOptions() =>
    [
        new ScenarioOption { Scenario = ScanScenario.FullSystemAudit },
        new ScenarioOption { Scenario = ScanScenario.GamePerformanceCheck },
        new ScenarioOption { Scenario = ScanScenario.AfterWindowsUpdate },
        new ScenarioOption { Scenario = ScanScenario.QuickHealthCheck }
    ];

    /// <summary>Resets the current scan session and navigates when an option is selected.</summary>
    public static bool TryStart(
        ScenarioOption? option,
        ScanSessionState session,
        INavigationService navigation)
    {
        if (option is null)
        {
            return false;
        }

        session.Reset();
        session.SelectedScenario = option.Scenario;
        navigation.NavigateTo<ScanViewModel>();
        return true;
    }
}
