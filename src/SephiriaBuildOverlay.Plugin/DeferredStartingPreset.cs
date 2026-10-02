namespace SephiriaBuildOverlay.Plugin;

internal enum DeferredPresetDecision { Wait, Apply, Cancel }

internal sealed class DeferredStartingPreset
{
    public DeferredStartingPreset(string? context) => Context = context;
    public string? Context { get; private set; }
    private bool _finished;

    public DeferredPresetDecision Observe(string? currentContext, bool unsafeState, bool ready)
    {
        if (_finished) return DeferredPresetDecision.Cancel;
        if (unsafeState || Context is not null && currentContext != Context)
        { _finished = true; return DeferredPresetDecision.Cancel; }
        if (currentContext is null) return DeferredPresetDecision.Wait;
        Context ??= currentContext;
        if (!ready) return DeferredPresetDecision.Wait;
        _finished = true;
        return DeferredPresetDecision.Apply;
    }
}
