namespace SephiriaBuildOverlay.Plugin;

// Corner drag changes only this panel's scale, not item overlays or game UI.
internal sealed class PanelResizeState
{
    public const float MinimumScale = .75f;
    public const float MaximumScale = 1.75f;
    private int? _pointer;
    private float _startX, _startY, _startScale, _uiScale, _fit;
    public PanelResizeState(float scale) => Scale = Finite(scale) ? Math.Max(MinimumScale, Math.Min(MaximumScale, scale)) : 1;
    public float Scale { get; private set; }
    public int? ActivePointer => _pointer;
    public bool Begin(int pointer, float x, float y, float displayedScale, float uiScale, float fit)
    {
        if (_pointer.HasValue || !Finite(x) || !Finite(y) || !Finite(displayedScale) || !Finite(uiScale) || uiScale <= 0 || !Finite(fit) || fit <= 0) return false;
        _pointer = pointer; _startX = x; _startY = y; _startScale = displayedScale / uiScale; _uiScale = uiScale; _fit = fit / uiScale;
        Scale = _startScale; return true;
    }
    public bool Drag(int pointer, float x, float y)
    {
        if (_pointer != pointer || !Finite(x) || !Finite(y)) return false;
        var halfWidth = OverlayUiTokens.CompactWidth / 2; var halfHeight = OverlayUiTokens.CompactHeight / 2;
        var delta = ((x - _startX) * halfWidth - (y - _startY) * halfHeight) / (halfWidth * halfWidth + halfHeight * halfHeight) / _uiScale;
        var upper = Math.Max(MinimumScale, Math.Min(MaximumScale, _fit));
        Scale = Math.Max(MinimumScale, Math.Min(upper, _startScale + delta)); return true;
    }
    public bool End(int pointer) { if (_pointer != pointer) return false; _pointer = null; return true; }
    public void Cancel() => _pointer = null;
    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
}
