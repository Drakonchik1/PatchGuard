using System.Windows;
using System.Windows.Threading;

namespace PatchGuard.Tests;

internal static class StaTestHost
{
    private static readonly Lazy<Dispatcher> Dispatcher = new(CreateDispatcher);

    public static void Run(Action action)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.Value.BeginInvoke(() =>
        {
            try
            {
                action();
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        completion.Task.GetAwaiter().GetResult();
    }

    private static Dispatcher CreateDispatcher()
    {
        Dispatcher? dispatcher = null;
        Exception? initializationError = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                var application = new App
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                application.InitializeComponent();
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            }
            catch (Exception exception)
            {
                initializationError = exception;
            }
            finally
            {
                ready.Set();
            }

            if (initializationError is null)
            {
                System.Windows.Threading.Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "PatchGuard WPF test dispatcher"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (initializationError is not null)
        {
            throw new InvalidOperationException("Could not initialize the WPF test host.", initializationError);
        }

        return dispatcher ?? throw new InvalidOperationException("WPF dispatcher was not created.");
    }
}
