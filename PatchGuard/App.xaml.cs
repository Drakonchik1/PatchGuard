using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PatchGuard.Data;
using PatchGuard.ViewModels;

namespace PatchGuard;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var services = new ServiceCollection();
        services.AddPatchGuard(configuration);
        _serviceProvider = services.BuildServiceProvider();

        try
        {
            await _serviceProvider
                .GetRequiredService<DatabaseSchemaInitializer>()
                .InitializeAsync();
        }
        catch (Exception)
        {
            MessageBox.Show(
                "PatchGuard could not initialize its local database. The application will close.",
                "PatchGuard startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        var mainWindow = new MainWindow();
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        var homeViewModel = _serviceProvider.GetRequiredService<HomeViewModel>();
        mainViewModel.CurrentViewModel = homeViewModel;
        homeViewModel.OnNavigatedTo();
        mainWindow.DataContext = mainViewModel;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
