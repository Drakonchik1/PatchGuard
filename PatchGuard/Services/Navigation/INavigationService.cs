namespace PatchGuard.Services.Navigation;

public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : class;
    void NavigateHome();
    bool CanGoBack { get; }
    void GoBack();
}
