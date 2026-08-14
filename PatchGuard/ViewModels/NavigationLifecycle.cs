namespace PatchGuard.ViewModels;

internal sealed class NavigationLifecycle
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private int _activeConsumers;
    private bool _retired;
    private bool _cancellationCompleted;
    private bool _disposed;

    public bool TryAcquire(out NavigationLifecycleLease? lease)
    {
        lock (_gate)
        {
            if (_retired)
            {
                lease = null;
                return false;
            }

            _activeConsumers++;
            lease = new NavigationLifecycleLease(this, _source.Token);
            return true;
        }
    }

    public void Retire()
    {
        lock (_gate)
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
        }

        try
        {
            _source.Cancel();
        }
        finally
        {
            CompleteCancellation();
        }
    }

    private void CompleteCancellation()
    {
        var dispose = false;
        lock (_gate)
        {
            _cancellationCompleted = true;
            if (_activeConsumers == 0 && !_disposed)
            {
                _disposed = true;
                dispose = true;
            }
        }

        if (dispose)
        {
            _source.Dispose();
        }
    }

    internal void Release()
    {
        var dispose = false;
        lock (_gate)
        {
            _activeConsumers--;
            if (_activeConsumers == 0 &&
                _retired &&
                _cancellationCompleted &&
                !_disposed)
            {
                _disposed = true;
                dispose = true;
            }
        }

        if (dispose)
        {
            _source.Dispose();
        }
    }
}

internal sealed class NavigationLifecycleLease : IDisposable
{
    private NavigationLifecycle? _owner;

    internal NavigationLifecycleLease(
        NavigationLifecycle owner,
        CancellationToken cancellationToken)
    {
        _owner = owner;
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _owner, null)?.Release();
    }
}

internal static class ActiveTaskTracker
{
    public static Task Retain(Task current, Task next) =>
        current.IsCompleted ? next : Task.WhenAll(current, next);
}
