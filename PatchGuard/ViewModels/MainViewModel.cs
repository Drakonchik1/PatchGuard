using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class MainViewModel : ObservableObject, IViewModelHost
{
    private readonly IServiceProvider _serviceProvider;

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    [ObservableProperty]
    private object? _currentViewModel;

    /// <summary>Drives the highlighted item in the navigation sidebar.</summary>
    [ObservableProperty]
    private string _activeSection = "Dashboard";

    public string AppTitle => "PatchGuard";

    partial void OnCurrentViewModelChanged(object? value)
    {
        // Keep the sidebar in sync when navigation happens from anywhere.
        ActiveSection = value switch
        {
            HomeViewModel => "Dashboard",
            DiagnoseViewModel or ScanViewModel or FindingsViewModel or GuideViewModel => "Diagnose",
            MonitorViewModel => "LiveMonitor",
            FpsViewModel => "GamePerformance",
            OptimizeViewModel => "Optimize",
            AlertsViewModel => "Alerts",
            SettingsViewModel => "Settings",
            _ => ActiveSection
        };
    }

    [RelayCommand]
    private void Navigate(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        // Resolved lazily to avoid a constructor cycle (NavigationService depends
        // on this view model as the IViewModelHost).
        var navigation = _serviceProvider.GetRequiredService<INavigationService>();

        switch (section)
        {
            case "Dashboard":
                navigation.NavigateHome();
                break;
            case "Diagnose":
                navigation.NavigateTo<DiagnoseViewModel>();
                break;
            case "LiveMonitor":
                navigation.NavigateTo<MonitorViewModel>();
                break;
            case "GamePerformance":
                navigation.NavigateTo<FpsViewModel>();
                break;
            case "Optimize":
                navigation.NavigateTo<OptimizeViewModel>();
                break;
            case "Alerts":
                navigation.NavigateTo<AlertsViewModel>();
                break;
            case "Settings":
                navigation.NavigateTo<SettingsViewModel>();
                break;
        }
    }
}
