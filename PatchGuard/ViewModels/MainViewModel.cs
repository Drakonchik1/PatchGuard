using CommunityToolkit.Mvvm.ComponentModel;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class MainViewModel : ObservableObject, IViewModelHost
{
    [ObservableProperty]
    private object? _currentViewModel;

    public string AppTitle => "PatchGuard";
}
