using System.Windows;

namespace PatchGuard.Services.Performance;

public sealed class SyntheticBenchmarkRunner : ISyntheticBenchmarkRunner
{
    public async Task<double> RunAsync(CancellationToken cancellationToken = default)
    {
        var dispatcher = Application.Current.Dispatcher;
        return await dispatcher.InvokeAsync(async () =>
        {
            var window = new BenchmarkWindow();
            window.Show();
            try
            {
                return await window.RunAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        }).Task.Unwrap();
    }
}
