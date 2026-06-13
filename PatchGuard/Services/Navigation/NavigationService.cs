using Microsoft.Extensions.DependencyInjection;
using PatchGuard.ViewModels;

namespace PatchGuard.Services.Navigation;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IViewModelHost _host;
    private readonly Stack<object> _history = new();

    public NavigationService(IServiceProvider serviceProvider, IViewModelHost host)
    {
        _serviceProvider = serviceProvider;
        _host = host;
    }

    public bool CanGoBack => _history.Count > 0;

    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        if (_host.CurrentViewModel is not null)
        {
            (_host.CurrentViewModel as INavigationLeave)?.OnNavigatedFrom();
            _history.Push(_host.CurrentViewModel);
        }

        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        _host.CurrentViewModel = viewModel;

        if (viewModel is INavigationAware aware)
        {
            aware.OnNavigatedTo();
        }
    }

    public void NavigateHome()
    {
        (_host.CurrentViewModel as INavigationLeave)?.OnNavigatedFrom();
        _history.Clear();
        var home = _serviceProvider.GetRequiredService<HomeViewModel>();
        _host.CurrentViewModel = home;

        if (home is INavigationAware aware)
        {
            aware.OnNavigatedTo();
        }
    }

    public void GoBack()
    {
        if (_history.Count == 0)
        {
            NavigateHome();
            return;
        }

        (_host.CurrentViewModel as INavigationLeave)?.OnNavigatedFrom();
        _host.CurrentViewModel = _history.Pop();
    }
}
