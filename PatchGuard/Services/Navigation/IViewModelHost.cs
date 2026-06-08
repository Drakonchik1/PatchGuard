namespace PatchGuard.Services.Navigation;

public interface IViewModelHost
{
    object? CurrentViewModel { get; set; }
}
