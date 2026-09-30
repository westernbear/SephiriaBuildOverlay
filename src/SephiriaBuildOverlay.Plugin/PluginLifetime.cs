namespace SephiriaBuildOverlay.Plugin;

// All async operations share this token; terminal callbacks are idempotent.
internal sealed class PluginLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    public CancellationToken Token { get; }
    public bool Stopped { get; private set; }
    private bool _disposed;
    public PluginLifetime() => Token = _cancellation.Token;
    public bool Stop()
    {
        if (Stopped) return false;
        Stopped = true;
        _cancellation.Cancel();
        return true;
    }
    public void Dispose()
    {
        Stop();
        if (_disposed) return;
        _disposed = true;
        _cancellation.Dispose();
    }
}
