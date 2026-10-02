using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private readonly NotificationQueue _notifications = new();

    private void Notify(string text, NotificationKind kind = NotificationKind.Information)
    {
        if (!_lifetime.Stopped) _notifications.Enqueue(text, kind);
    }
}

internal sealed partial class UnityGameGateway
{
    private NativeOverlayLayer? _notificationLayer;
    private UnityEngine.Object? _notificationFont;
    private float _nextNotificationFontCheck;
    private Sprite? _notificationPanelSprite;
    private Color _notificationPanelColor = Color.white;

    // Passive native canvas, not an IMGUI window: never owns mouse/pad focus.
    internal void DrawNotification(BubbleNotification? notification, float opacity, float scale)
    {
        if (notification is null) { _notificationLayer?.SetVisible(false); return; }
        if (_notificationFont == null && Time.unscaledTime >= _nextNotificationFontCheck)
        {
            _nextNotificationFontCheck = Time.unscaledTime + 1f;
            var fontType = GameType("TMPro.TMP_FontAsset");
            if (fontType is not null)
            {
                var fonts = Resources.FindObjectsOfTypeAll(fontType);
                _notificationFont = fonts.FirstOrDefault(x => x != null && x.name.IndexOf("PIXEL_SMALL", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? fonts.FirstOrDefault(x => x != null && x.name.IndexOf("Galmuri", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
        var font = _nativeFont != null ? _nativeFont : _notificationFont;
        if (font == null) return;
        if (_notificationLayer is null)
        {
            var textType = GameType("TMPro.TextMeshProUGUI");
            var imageType = GameType("UnityEngine.UI.Image");
            if (textType is null || imageType is null) return;
            _notificationLayer = new NativeOverlayLayer(textType, imageType, "SephiriaBuildOverlay.Notifications", OverlayUiTokens.NotificationSortingOrder);
        }
        var layer = _notificationLayer;
        layer.Begin(font); layer.SetOpacity(opacity);
        var pixels = Math.Max(14, Mathf.Round(OverlayUiTokens.BodyFontSize * scale));
        var inset = 16 * scale;
        var width = Math.Max(1, Math.Min(380 * scale, Screen.width - inset * 2));
        var measured = layer.MeasureLabel("notice", notification.Text, pixels, Math.Max(1, width - inset * 2));
        var height = Math.Min(Screen.height - inset * 2, Math.Max(52 * scale, measured.y + inset * 2));
        // Above the bottom-left native inventory/action hotkeys, not the money HUD.
        var rectangle = new Rect(inset, Math.Max(inset, Screen.height - 72 * scale - height), width, height);
        var accent = notification.Kind switch
        {
            NotificationKind.Success => OverlayTheme.Good,
            NotificationKind.Warning => OverlayTheme.Warning,
            NotificationKind.Error => OverlayTheme.Danger,
            _ => OverlayTheme.Accent
        };
        layer.Box("background", rectangle, _notificationPanelSprite != null ? _notificationPanelColor : OverlayTheme.Surface, _notificationPanelSprite, sliced: true);
        if (_notificationPanelSprite == null)
        {
            layer.Border("outline", rectangle, OverlayTheme.Outline, 3 * scale);
            layer.Border("trim", new Rect(rectangle.x + 3 * scale, rectangle.y + 3 * scale, rectangle.width - 6 * scale, rectangle.height - 6 * scale), OverlayTheme.Border, scale);
        }
        layer.Box("severity", new Rect(rectangle.x + 7 * scale, rectangle.y + 12 * scale, 2 * scale, rectangle.height - 24 * scale), accent);
        layer.Label("notice", new Rect(rectangle.x + inset, rectangle.y + inset, rectangle.width - inset * 2, rectangle.height - inset * 2), notification.Text, OverlayTheme.Text, pixels, wrappedLeft: true);
        layer.End();
    }
}
