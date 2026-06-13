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

    /// <summary>Drives the highlighted item in the navigation rail.</summary>
    [ObservableProperty]
    private string _activeSection = "Home";

    public string AppTitle => "PatchGuard";

    partial void OnCurrentViewModelChanged(object? value)
    {
        // Keep the rail in sync when navigation happens from anywhere.
        ActiveSection = value switch
        {
            HomeViewModel => "Home",
            MonitorViewModel => "Monitor",
            FpsViewModel => "Fps",
            OptimizeViewModel => "Optimize",
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
            case "Home":
                navigation.NavigateHome();
                break;
            case "Monitor":
                navigation.NavigateTo<MonitorViewModel>();
                break;
            case "Fps":
                navigation.NavigateTo<FpsViewModel>();
                break;
            case "Optimize":
                navigation.NavigateTo<OptimizeViewModel>();
                break;
        }
    }
}
