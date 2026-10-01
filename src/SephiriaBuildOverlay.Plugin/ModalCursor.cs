using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private CanvasGroup? _modalNativeCursor;
    private float _modalNativeCursorAlpha;

    internal void HideNativeModalCursor()
    {
        var cursor = ReadStatic("UI_Cursor", "Current");
        var group = cursor is null ? null : ReadNamedObject(cursor, "group") as CanvasGroup;
        if (group == null) return;
        if (_modalNativeCursor != group)
        {
            RestoreNativeModalCursor(false);
            _modalNativeCursor = group;
            _modalNativeCursorAlpha = group.alpha;
        }
        group.alpha = 0f;
    }

    internal void RestoreNativeModalCursor(bool gamepad)
    {
        if (_modalNativeCursor != null)
            _modalNativeCursor.alpha = gamepad ? 0f : _modalNativeCursorAlpha;
        _modalNativeCursor = null;
    }
}

public sealed partial class SephiriaBuildOverlayPlugin
{
    private readonly ModalCursorVisibility _modalCursorVisibility = new();

    // The system cursor is composited above the entire game window; it cannot
    // be covered by an IMGUI panel or the native uGUI canvas.
    private void LateUpdate()
    {
        if (!_lifetime.Stopped) UpdateModalCursor();
    }

    private void UpdateModalCursor()
    {
        var active = _showImport && !_controller.GamepadMode && Application.isFocused;
        if (active) _gateway.HideNativeModalCursor();
        else _gateway.RestoreNativeModalCursor(_controller.GamepadMode);
        var visibility = _modalCursorVisibility.Resolve(_showImport, _controller.GamepadMode, Application.isFocused, Cursor.visible);
        if (visibility.HasValue) Cursor.visible = visibility.Value;
    }

    private void ReleaseSystemCursor()
    {
        _gateway?.RestoreNativeModalCursor(_controller.GamepadMode);
        var visibility = _modalCursorVisibility.Resolve(false, _controller.GamepadMode, Application.isFocused, Cursor.visible);
        if (visibility.HasValue) Cursor.visible = visibility.Value;
    }
}
