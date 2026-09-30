namespace SephiriaBuildOverlay.Core.Runtime;

public sealed class BackgroundPlanner<TResult> : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _currentCancellation;
    private long _generation;
    private bool _disposed;

    public event Action<TResult>? ResultReady;
    public event Action<Exception>? CalculationFailed;

    public long Submit<TSnapshot>(TSnapshot immutableSnapshot, Func<TSnapshot, CancellationToken, TResult> calculate)
    {
        CancellationTokenSource cancellation;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BackgroundPlanner<TResult>));
            _currentCancellation?.Cancel();
            _currentCancellation?.Dispose();
            _currentCancellation = cancellation = new CancellationTokenSource();
            token = cancellation.Token;
            generation = ++_generation;
        }

        _ = Task.Run(() => calculate(immutableSnapshot, token), token).ContinueWith(task =>
        {
            if (task.IsCanceled) return;
            lock (_gate)
            {
                if (_disposed || generation != _generation) return; // stale result
                // Publish before a newer Submit can be accepted. Otherwise a generation
                // could become stale between the check and the callback.
                if (task.IsFaulted)
                    CalculationFailed?.Invoke(task.Exception!.GetBaseException());
                else
                    ResultReady?.Invoke(task.Result);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return generation;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _currentCancellation?.Cancel();
            _currentCancellation?.Dispose();
            _currentCancellation = null;
        }
    }
}
