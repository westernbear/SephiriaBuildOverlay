using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private CanvasGroup? _modalNativeCursor;
    private float _modalNativeCursorAlpha;
    private NativeOverlayLayer? _modalCursorLayer;
    private Sprite? _immediateCursorSprite;
    private Rect _immediateCursorBounds;
    private Rect _immediateCursorUv;

    internal bool DrawModalCursor(bool immediate = false)
    {
        _immediateCursorSprite = null;
        var cursor = ReadStatic("UI_Cursor", "Current");
        var image = cursor is null ? null : ReadNamedObject(cursor, "image") as Component;
        var sprite = image is null ? null : ReadNamedObject(image, "sprite") as Sprite;
        if (image == null || sprite == null || image.transform is not RectTransform rect)
        { _modalCursorLayer?.SetVisible(false); return false; }
        var bounds = ScreenRect(rect);
        if (bounds.width <= 0 || bounds.height <= 0 || float.IsNaN(bounds.x) || float.IsNaN(bounds.y))
        { _modalCursorLayer?.SetVisible(false); return false; }
        if (immediate)
        {
            _modalCursorLayer?.SetVisible(false);
            try
            {
                if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None) return false;
                var textureRect = sprite.textureRect;
                if (sprite.texture == null || sprite.texture.width == 0 || sprite.texture.height == 0) return false;
                _immediateCursorSprite = sprite;
                _immediateCursorBounds = bounds;
                _immediateCursorUv = new Rect(textureRect.x / sprite.texture.width, textureRect.y / sprite.texture.height,
                    textureRect.width / sprite.texture.width, textureRect.height / sprite.texture.height);
                return true;
            }
            catch (UnityException) { return false; } // Tight/rotated packing: use the OS pointer.
        }
        var textType = GameType("TMPro.TextMeshProUGUI"); var imageType = GameType("UnityEngine.UI.Image");
        if (textType is null || imageType is null) return false;
        _modalCursorLayer ??= new NativeOverlayLayer(textType, imageType, "SephiriaBuildOverlay.Cursor", OverlayUiTokens.CursorSortingOrder);
        _modalCursorLayer.BeginGraphics();
        _modalCursorLayer.Box("pointer", bounds, Color.white, sprite);
        _modalCursorLayer.End();
        return true;
    }

    internal void DrawImmediateModalCursor()
    {
        if (Event.current.type != EventType.Repaint || _immediateCursorSprite == null) return;
        var matrix = GUI.matrix; var depth = GUI.depth; var color = GUI.color;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = OverlayUiTokens.CursorDepth;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(_immediateCursorBounds, _immediateCursorSprite.texture, _immediateCursorUv, true);
        }
        finally { GUI.matrix = matrix; GUI.depth = depth; GUI.color = color; }
    }

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
        _immediateCursorSprite = null;
        _modalCursorLayer?.SetVisible(false);
        if (_modalNativeCursor != null)
            _modalNativeCursor.alpha = gamepad ? 0f : _modalNativeCursorAlpha;
        _modalNativeCursor = null;
    }
}

public sealed partial class SephiriaBuildOverlayPlugin
{
    private readonly ModalCursorVisibility _modalCursorVisibility = new();

    // Match the panel's rendering surface: native canvas for the small panel,
    // IMGUI for details. Use the OS pointer if the native sprite is unavailable.
    private void LateUpdate()
    {
        if (!_lifetime.Stopped) UpdateModalCursor();
    }

    private void UpdateModalCursor()
    {
        var surface = ModalCursorPresentation.Choose(_showImport, _advancedReview, _controller.GamepadMode, Application.isFocused, true);
        var active = surface is ModalCursorSurface.NativeCanvas or ModalCursorSurface.Immediate;
        var native = false;
        if (active) { _gateway.HideNativeModalCursor(); native = _gateway.DrawModalCursor(surface == ModalCursorSurface.Immediate); }
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
