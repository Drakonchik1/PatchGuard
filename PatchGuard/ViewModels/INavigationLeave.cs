namespace PatchGuard.ViewModels;

/// <summary>Implemented by view models that need to release resources (timers,
/// captures) when the user navigates away from them.</summary>
public interface INavigationLeave
{
    void OnNavigatedFrom();
}
