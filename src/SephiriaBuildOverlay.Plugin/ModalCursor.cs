using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private CanvasGroup? _modalNativeCursor;
    private float _modalNativeCursorAlpha;
    private NativeOverlayLayer? _modalCursorLayer;
    internal bool DrawModalCursor()
    {
        var cursor = ReadStatic("UI_Cursor", "Current");
        var image = cursor is null ? null : ReadNamedObject(cursor, "image") as Component;
        var sprite = image is null ? null : ReadNamedObject(image, "sprite") as Sprite;
        if (image == null || sprite == null || image.transform is not RectTransform rect)
        { _modalCursorLayer?.SetVisible(false); return false; }
        var bounds = ScreenRect(rect);
        if (bounds.width <= 0 || bounds.height <= 0 || float.IsNaN(bounds.x) || float.IsNaN(bounds.y))
        { _modalCursorLayer?.SetVisible(false); return false; }
        var textType = GameType("TMPro.TextMeshProUGUI"); var imageType = GameType("UnityEngine.UI.Image");
        if (textType is null || imageType is null) return false;
        _modalCursorLayer ??= new NativeOverlayLayer(textType, imageType, "SephiriaBuildOverlay.Cursor", OverlayUiTokens.CursorSortingOrder);
        _modalCursorLayer.BeginGraphics();
        _modalCursorLayer.Box("pointer", bounds, Color.white, sprite);
        _modalCursorLayer.End();
        return true;
    }

    internal void HideNativeModalCursor()
    {
        // Also hide our compact-panel clone before showing the system pointer.
        _modalCursorLayer?.SetVisible(false);
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
        _modalCursorLayer?.SetVisible(false);
        if (_modalNativeCursor != null)
            _modalNativeCursor.alpha = gamepad ? 0f : _modalNativeCursorAlpha;
        _modalNativeCursor = null;
    }
}

public sealed partial class SephiriaBuildOverlayPlugin
{
    private readonly ModalCursorVisibility _modalCursorVisibility = new();

    // The compact native panel keeps the game's cursor sprite. Settings use
    // the system pointer, which cannot be covered by an IMGUI window.
    private void LateUpdate()
    {
        if (!_lifetime.Stopped) UpdateModalCursor();
    }

    private void UpdateModalCursor()
    {
        var surface = ModalCursorPresentation.Choose(_showImport, _advancedReview, _controller.GamepadMode, Application.isFocused, true);
        var active = surface is ModalCursorSurface.NativeCanvas or ModalCursorSurface.System;
        var native = false;
        if (active)
        {
            _gateway.HideNativeModalCursor();
            if (surface == ModalCursorSurface.NativeCanvas) native = _gateway.DrawModalCursor();
        }
        else _gateway.RestoreNativeModalCursor(_controller.GamepadMode);
        var visibility = _modalCursorVisibility.Resolve(_showImport, _controller.GamepadMode, Application.isFocused, Cursor.visible, native);
        if (visibility.HasValue) Cursor.visible = visibility.Value;
    }

    private void ReleaseSystemCursor()
    {
        _gateway?.RestoreNativeModalCursor(_controller.GamepadMode);
        var visibility = _modalCursorVisibility.Resolve(false, _controller.GamepadMode, Application.isFocused, Cursor.visible);
        if (visibility.HasValue) Cursor.visible = visibility.Value;
    }
}
