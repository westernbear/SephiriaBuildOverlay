namespace SephiriaBuildOverlay.Plugin;

internal enum NotificationKind { Information, Success, Warning, Error }

internal sealed class BubbleNotification
{
    public BubbleNotification(string text, NotificationKind kind) { Text = text; Kind = kind; }
    public string Text { get; }
    public NotificationKind Kind { get; }
}

// Pure presentation state: no game action, Unity object, wall-clock or input API.
internal sealed class NotificationQueue
{
    internal const int MaximumPending = 4;
    internal const float ReadingSeconds = 5;
    internal const float WarningSeconds = 8;
    internal const float FadeSeconds = .25f;
    private readonly List<BubbleNotification> _pending = new();
    private float _remaining;
    public BubbleNotification? Current { get; private set; }
    public int PendingCount => _pending.Count;
    public float Opacity => Current is null ? 0 : Math.Min(1, Math.Max(0, _remaining / FadeSeconds));

    public void Enqueue(string text, NotificationKind kind)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (Current?.Text == text && Current.Kind == kind || _pending.Any(x => x.Text == text && x.Kind == kind)) return;
        if (_pending.Count == MaximumPending)
        {
            var replace = _pending.FindIndex(x => x.Kind is NotificationKind.Information or NotificationKind.Success);
            if (replace < 0)
            {
                if (kind is NotificationKind.Information or NotificationKind.Success) return;
                replace = 0;
            }
            _pending.RemoveAt(replace);
        }
        _pending.Add(new BubbleNotification(text, kind));
    }

    public void Advance(float seconds, bool focused)
    {
        if (!focused || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) return;
        if (Current is not null)
        {
            _remaining -= seconds;
            if (_remaining > 0) return;
            Current = null;
        }
        if (_pending.Count == 0) return;
        Current = _pending[0]; _pending.RemoveAt(0);
        _remaining = Current.Kind is NotificationKind.Warning or NotificationKind.Error ? WarningSeconds : ReadingSeconds;
    }

    public void Clear() { _pending.Clear(); Current = null; _remaining = 0; }
}
