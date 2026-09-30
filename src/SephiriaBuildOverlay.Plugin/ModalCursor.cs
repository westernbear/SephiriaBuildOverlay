using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    // IMGUI windows are drawn over uGUI, including the game's cursor canvas.
    // Reuse the current native sprite above our window; never copy its asset.
    internal bool DrawModalCursor()
    {
        var cursor = ReadStatic("UI_Cursor", "Current");
        if (cursor is null) return false;
        var image = ReadNamedObject(cursor, "image");
        if (image is null || !ReadBool(image, "enabled")) image = ReadNamedObject(cursor, "battleImage");
        if (image is not Component component || !ReadBool(image, "enabled") || !component.gameObject.activeInHierarchy) return false;
        var sprite = ReadNamedObject(image, "overrideSprite") as Sprite ?? ReadNamedObject(image, "sprite") as Sprite;
        var transform = ReadNamedObject(image, "rectTransform") as RectTransform;
        if (sprite == null || transform == null || sprite.texture == null) return false;
        if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None) return false;
        Rect textureRect;
        try { textureRect = sprite.textureRect; } catch { return false; }
        if (Event.current.type != EventType.Repaint) return true;
        var savedMatrix = GUI.matrix; var savedColor = GUI.color; var savedDepth = GUI.depth;
        try
        {
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -1000;
            GUI.color = ReadNamedObject(image, "color") is Color color ? color : Color.white;
            var texture = sprite.texture;
            var uv = new Rect(textureRect.x / texture.width, textureRect.y / texture.height,
                textureRect.width / texture.width, textureRect.height / texture.height);
            GUI.DrawTextureWithTexCoords(ScreenRect(transform), texture, uv, true);
        }
        finally { GUI.matrix = savedMatrix; GUI.color = savedColor; GUI.depth = savedDepth; }
        return true;
    }
}

public sealed partial class SephiriaBuildOverlayPlugin
{
    private readonly ModalCursorVisibility _modalCursorVisibility = new();

    private void UpdateModalCursor()
    {
        var nativeAvailable = false;
        if (_showImport && !_controller.GamepadMode && Application.isFocused)
        {
            try { nativeAvailable = _gateway.DrawModalCursor(); }
            catch { /* Native UI was destroyed/changed. Use the OS fallback. */ }
        }
        var visibility = _modalCursorVisibility.Resolve(_showImport, _controller.GamepadMode, Application.isFocused, nativeAvailable, Cursor.visible);
        if (visibility.HasValue) Cursor.visible = visibility.Value;
    }

    private void ReleaseSystemCursor()
    {
        var visibility = _modalCursorVisibility.Resolve(false, _controller.GamepadMode, Application.isFocused, false, Cursor.visible);
        if (visibility.HasValue) Cursor.visible = visibility.Value;
    }
}
