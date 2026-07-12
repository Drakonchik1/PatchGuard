using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Navigation;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class ScanScenarioWorkflowTests
{
    [Fact]
    public void CreateOptionsReturnsCanonicalScenarioOrder()
    {
        var scenarios = ScanScenarioWorkflow.CreateOptions()
            .Select(option => option.Scenario)
            .ToArray();

        Assert.Equal(
        [
            ScanScenario.FullSystemAudit,
            ScanScenario.GamePerformanceCheck,
            ScanScenario.AfterWindowsUpdate,
            ScanScenario.QuickHealthCheck
        ],
        scenarios);
    }

    [Fact]
    public void TryStartResetsSessionAndNavigatesToScan()
    {
        var session = new ScanSessionState
        {
            SelectedScenario = ScanScenario.AfterWindowsUpdate,
            Guide = new RepairGuide { Summary = "Existing", ChiefVerdict = "Existing" }
        };
        session.Findings.Add(new Finding
        {
            ModuleName = "Test",
            Title = "Existing",
            Details = "Existing finding",
            Severity = FindingSeverity.Warning
        });
        var navigation = new RecordingNavigationService();
        var option = new ScenarioOption { Scenario = ScanScenario.QuickHealthCheck };

        var started = ScanScenarioWorkflow.TryStart(option, session, navigation);

        Assert.True(started);
        Assert.Equal(ScanScenario.QuickHealthCheck, session.SelectedScenario);
        Assert.Empty(session.Findings);
        Assert.Null(session.Guide);
        Assert.Equal(typeof(ScanViewModel), navigation.DestinationType);
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public Type? DestinationType { get; private set; }
        public bool CanGoBack => false;
        public void NavigateTo<TViewModel>() where TViewModel : class =>
            DestinationType = typeof(TViewModel);
        public void NavigateHome() => DestinationType = typeof(HomeViewModel);
        public void GoBack() { }
    }
}
