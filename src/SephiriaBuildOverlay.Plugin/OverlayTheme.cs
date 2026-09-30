using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

// Independent IMGUI design tokens. No third-party mod code or assets are used.
internal sealed class OverlayTheme : IDisposable
{
    public static readonly Color Text = new(0.94f, 0.96f, 1f);
    public static readonly Color Muted = new(0.65f, 0.72f, 0.83f);
    public static readonly Color Accent = new(0.43f, 0.83f, 1f);
    public static readonly Color Good = new(0.49f, 0.91f, 0.66f);
    public static readonly Color Warning = new(1f, 0.79f, 0.39f);
    public static readonly Color Danger = new(1f, 0.44f, 0.48f);
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
        var font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x =>
            x != null && x.name.IndexOf("Galmuri", StringComparison.OrdinalIgnoreCase) >= 0);
        if (font == null)
        {
            _ownedFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 16);
            font = _ownedFont;
        }
        Skin.font = font;
        var panel = Texture(new Color(0.065f, 0.08f, 0.14f, 0.98f));
        var card = Texture(new Color(0.105f, 0.13f, 0.21f, 1f));
        var button = Texture(new Color(0.16f, 0.20f, 0.30f, 1f));
        var hover = Texture(new Color(0.22f, 0.30f, 0.43f, 1f));
        var selected = Texture(new Color(0.13f, 0.34f, 0.47f, 1f));
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

    public void Dispose()
    {
        foreach (var texture in _textures) UnityEngine.Object.Destroy(texture);
        if (_ownedFont != null) UnityEngine.Object.Destroy(_ownedFont);
        UnityEngine.Object.Destroy(Skin);
    }
}
