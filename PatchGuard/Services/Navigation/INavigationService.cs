namespace PatchGuard.Services.Navigation;

public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : class;

    void NavigateTopLevel<TViewModel>() where TViewModel : class =>
        NavigateTo<TViewModel>();

    void NavigateHome();
    bool CanGoBack { get; }
    void GoBack();
}
