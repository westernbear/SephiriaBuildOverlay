namespace SephiriaBuildOverlay.Plugin;

internal sealed class ModalCursorVisibility
{
    private bool _leased;
    private bool _previous;
    public bool? Resolve(bool panel, bool gamepad, bool focused, bool currentVisibility, bool nativeOverlay = false)
    {
        if (panel && !gamepad && focused)
        {
            if (!_leased) { _previous = currentVisibility; _leased = true; }
            return !nativeOverlay;
        }
        if (!_leased) return null;
        _leased = false;
        // Native focus-loss code deliberately exposes the system pointer.
        return focused ? gamepad ? false : _previous : null;
    }
}
