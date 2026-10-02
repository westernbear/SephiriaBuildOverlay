using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

// Independent IMGUI design tokens. No third-party mod code or assets are used.
internal sealed class OverlayTheme : IDisposable
{
    private static Color Rgb(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    public static readonly Color Surface = Rgb(OverlayUiTokens.Surface);
    public static readonly Color Inset = Rgb(OverlayUiTokens.Inset);
    public static readonly Color Button = Rgb(OverlayUiTokens.Button);
    public static readonly Color Outline = Rgb(OverlayUiTokens.Outline);
    public static readonly Color Border = Rgb(OverlayUiTokens.Border);
    public static readonly Color Text = Rgb(OverlayUiTokens.Text);
    public static readonly Color Muted = Rgb(OverlayUiTokens.Muted);
    public static readonly Color Accent = Rgb(OverlayUiTokens.Accent);
    public static readonly Color Good = Rgb(OverlayUiTokens.Good);
    public static readonly Color Warning = Rgb(OverlayUiTokens.Warning);
    public static readonly Color Danger = Rgb(OverlayUiTokens.Danger);
    private readonly List<Texture2D> _textures = new();
    private readonly Font? _ownedFont;
    public GUISkin Skin { get; }
    public GUIStyle Heading { get; }
    public GUIStyle Small { get; }
    public GUIStyle Selected { get; }
    public GUIStyle Primary { get; }
    public GUIStyle Card { get; }
    public GUIStyle WarningText { get; }
    public GUIStyle CompactButton { get; }

    public OverlayTheme(GUISkin original)
    {
        Skin = UnityEngine.Object.Instantiate(original);
        // TMP's source Font asset is not a usable legacy IMGUI atlas in this
        // game: assigning it makes every settings label disappear. Native
        // controls keep the actual Galmuri TMP asset; the legacy settings use
        // an installed dynamic font, with a Korean-capable Windows fallback.
        var family = UiFontFamily.Select(Font.GetOSInstalledFontNames());
        if (family is not null) _ownedFont = Font.CreateDynamicFontFromOSFont(family, 16);
        var font = _ownedFont != null ? _ownedFont : original.font;
        Debug.Log("Sephiria Build Overlay settings font: " + (family ?? "default"));
        Skin.font = font;
        var panel = Texture(Surface);
        var card = Texture(Rgb(OverlayUiTokens.Inset));
        var button = Texture(Rgb(OverlayUiTokens.Button));
        var hover = Texture(Rgb(OverlayUiTokens.Hover));
        var selected = Texture(Rgb(OverlayUiTokens.Selected));
        foreach (var style in new[] { Skin.label, Skin.button, Skin.textField, Skin.box, Skin.window, Skin.toggle })
        {
            style.font = font;
            style.fontSize = 15;
            style.normal.textColor = Text;
            style.hover.textColor = Text;
            style.active.textColor = Text;
            style.focused.textColor = Text;
        }
        Skin.label.wordWrap = true;
        Skin.label.margin = new RectOffset(0, 0, 3, 3);
        Skin.button.padding = new RectOffset(10, 10, 6, 6);
        Skin.button.wordWrap = true;
        Skin.button.margin = new RectOffset(3, 3, 3, 3);
        Skin.button.normal.background = button;
        Skin.button.hover.background = hover;
        Skin.button.active.background = selected;
        Skin.button.focused.background = hover;
        Skin.textField.normal.background = card;
        Skin.textField.focused.background = button;
        Skin.textField.padding = new RectOffset(10, 10, 7, 7);
        Skin.textField.margin = new RectOffset(3, 3, 3, 3);
        Skin.window.normal.background = panel;
        Skin.window.onNormal.background = panel;
        Skin.window.border = new RectOffset(0, 0, 0, 0);
        Skin.window.padding = new RectOffset(16, 16, 52, 14);
        Card = new GUIStyle(Skin.box) { padding = new RectOffset(12, 12, 9, 9), margin = new RectOffset(2, 2, 4, 4) };
        Card.normal.background = card;
        Card.border = new RectOffset(0, 0, 0, 0);
        Heading = new GUIStyle(Skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
        Heading.normal.textColor = Accent;
        Small = new GUIStyle(Skin.label) { fontSize = 12 };
        Small.normal.textColor = Muted;
        Selected = new GUIStyle(Skin.button);
        Selected.normal.background = selected;
        Selected.normal.textColor = Accent;
        Primary = new GUIStyle(Selected) { fontStyle = FontStyle.Bold };
        CompactButton = new GUIStyle(Skin.button) { wordWrap = false, fixedHeight = 30, padding = new RectOffset(2, 2, 3, 3) };
        WarningText = new GUIStyle(Skin.label);
        WarningText.normal.textColor = Warning;
    }

    private Texture2D Texture(Color color)
    {
        var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        _textures.Add(texture);
        return texture;
    }

    public void Dispose() => Dispose(true);
    public void Dispose(bool destroyUnityObjects)
    {
        if (destroyUnityObjects)
        {
            foreach (var texture in _textures) UnityEngine.Object.Destroy(texture);
            if (_ownedFont != null) UnityEngine.Object.Destroy(_ownedFont);
            UnityEngine.Object.Destroy(Skin);
        }
        _textures.Clear();
    }
}
