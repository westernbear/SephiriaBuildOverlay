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
    internal const float ReadingSeconds = 5;
    internal const float WarningSeconds = 8;
    internal const float FadeSeconds = .25f;
    private float _remaining;
    public BubbleNotification? Current { get; private set; }
    public float Opacity => Current is null ? 0 : Math.Min(1, Math.Max(0, _remaining / FadeSeconds));

    public void Enqueue(string text, NotificationKind kind)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = NotificationText.Plain(text);
        if (text.Length == 0) return;
        if (Current?.Text == text && Current.Kind == kind) return;
        // Latest event wins immediately. Notices never serialize game operations.
        Current = new BubbleNotification(text, kind);
        _remaining = kind is NotificationKind.Warning or NotificationKind.Error ? WarningSeconds : ReadingSeconds;
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
    }

    public void Clear() { Current = null; _remaining = 0; }
}
