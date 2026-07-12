using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Navigation;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class GuidePrivacyUiTests
{
    [Fact]
    public async Task ConsentChoiceIsPassedForEachGuidanceRequest()
    {
        var council = new RecordingCouncilService();
        var viewModel = CreateViewModel(council);

        viewModel.HasExternalAiConsent = false;
        viewModel.RunCouncilCommand.Execute(null);
        await council.WaitForCallsAsync(1);

        viewModel.HasExternalAiConsent = true;
        viewModel.RunCouncilCommand.Execute(null);
        await council.WaitForCallsAsync(2);

        Assert.Equal([false, true], council.ExternalConsentValues);
    }

    [Fact]
    public void ExistingGuideExposesInspectableReferences()
    {
        var session = new ScanSessionState
        {
            SelectedScenario = ScanScenario.QuickHealthCheck,
            Guide = new RepairGuide
            {
                Summary = "Ready",
                ChiefVerdict = "Review source",
                WebReferences =
                [
                    new WebReference
                    {
                        Title = "Vendor article",
                        Url = "https://support.example.com/fix",
                        Domain = "support.example.com",
                        UsedFor = "Windows 11 Event logs troubleshooting"
                    }
                ],
                Sources = [GuidanceSource.Local, GuidanceSource.WebSourced]
            }
        };
        var viewModel = new GuideViewModel(
            new NoOpNavigationService(),
            session,
            new RecordingCouncilService());

        viewModel.OnNavigatedTo();

        var reference = Assert.Single(viewModel.WebReferences);
        Assert.Equal("Vendor article", reference.Title);
        Assert.Equal("support.example.com", reference.Domain);
        Assert.Equal("Windows 11 Event logs troubleshooting", reference.UsedFor);
    }

    private static GuideViewModel CreateViewModel(IAiCouncilService council) =>
        new(
            new NoOpNavigationService(),
            new ScanSessionState { SelectedScenario = ScanScenario.QuickHealthCheck },
            council);

    private sealed class RecordingCouncilService : IAiCouncilService
    {
        private readonly SemaphoreSlim _calls = new(0);
        public List<bool> ExternalConsentValues { get; } = [];

        public Task<RepairGuide> BuildGuideAsync(
            ScanScenario scenario,
            IReadOnlyList<Finding> findings,
            IProgress<CouncilProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default,
            bool allowExternalServices = false)
        {
            ExternalConsentValues.Add(allowExternalServices);
            _calls.Release();
            return Task.FromResult(new RepairGuide
            {
                Summary = "Ready",
                ChiefVerdict = "Ready"
            });
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (ExternalConsentValues.Count < count)
            {
                await _calls.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    private sealed class NoOpNavigationService : INavigationService
    {
        public bool CanGoBack => false;
        public void NavigateTo<TViewModel>() where TViewModel : class { }
        public void NavigateHome() { }
        public void GoBack() { }
    }
}
