using PatchGuard.Services.Navigation;
using PatchGuard.Services.Health;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void DefaultsToDashboardSection()
    {
        var viewModel = new MainViewModel(new EmptyServiceProvider());

        Assert.Equal("Dashboard", viewModel.ActiveSection);
    }

    [Theory]
    [MemberData(nameof(DiagnoseViewModels))]
    public void NestedDiagnosticDestinationsKeepDiagnoseActive(object destination)
    {
        var viewModel = new MainViewModel(new EmptyServiceProvider());

        viewModel.CurrentViewModel = destination;

        Assert.Equal("Diagnose", viewModel.ActiveSection);
    }

    public static TheoryData<object> DiagnoseViewModels() =>
        new()
        {
            new ScanViewModel(null!, null!, null!, null!),
            new FindingsViewModel(null!, null!, new HealthScorePolicy()),
            new GuideViewModel(null!, null!, null!)
        };

    [Theory]
    [InlineData("Dashboard", typeof(HomeViewModel))]
    [InlineData("Diagnose", typeof(DiagnoseViewModel))]
    [InlineData("LiveMonitor", typeof(MonitorViewModel))]
    [InlineData("GamePerformance", typeof(FpsViewModel))]
    [InlineData("Optimize", typeof(OptimizeViewModel))]
    public void SidebarCommandNavigatesToExpectedDestination(string section, Type expectedType)
    {
        var navigation = new RecordingNavigationService();
        var viewModel = new MainViewModel(new NavigationServiceProvider(navigation));

        viewModel.NavigateCommand.Execute(section);

        Assert.Equal(expectedType, navigation.DestinationType);
    }

    [Theory]
    [InlineData("Alerts", typeof(AlertsViewModel))]
    [InlineData("Settings", typeof(SettingsViewModel))]
    public void PlannedSidebarDestinationsNavigateToExactPlaceholder(string section, Type expectedType)
    {
        var navigation = new RecordingNavigationService();
        var viewModel = new MainViewModel(new NavigationServiceProvider(navigation));

        viewModel.NavigateCommand.Execute(section);

        Assert.Equal(expectedType, navigation.DestinationType);
    }

    [Fact]
    public void DiagnoseSidebarOpensScenarioSelectionInsteadOfStartingAnUnconfiguredScan()
    {
        var navigation = new RecordingNavigationService();
        var viewModel = new MainViewModel(new NavigationServiceProvider(navigation));

        viewModel.NavigateCommand.Execute("Diagnose");

        Assert.Equal(typeof(DiagnoseViewModel), navigation.DestinationType);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class NavigationServiceProvider(INavigationService navigation) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(INavigationService) ? navigation : null;
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
