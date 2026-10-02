namespace SephiriaBuildOverlay.Plugin;

// Pixel-menu tokens, shared by the small import panel and passive notifications.
// Authored colors/geometry only: no game or third-party assets are embedded.
internal static class OverlayUiTokens
{
    public const int Surface = 0x302530;
    public const int Inset = 0x231c29;
    public const int Button = 0x493541;
    public const int Hover = 0x604852;
    public const int Selected = 0x584452;
    public const int Outline = 0x17131e;
    public const int Border = 0xaa8d95;
    public const int Text = 0xf8f1e3;
    public const int Muted = 0xcebdc2;
    public const int Accent = 0xf4d69a;
    public const int Good = 0xa8e0bc;
    public const int Warning = 0xffd398;
    public const int Danger = 0xffa7a0;
    public const float CompactWidth = 420;
    public const float CompactHeight = 144;
    public const float AdvancedWidth = 760;
    public const float AdvancedHeight = 560;
    public const int WindowId = 761331;
    public const int WindowDepth = -20;
    public const int OverlaySortingOrder = 32000;
    public const int NotificationSortingOrder = 32001;
    public const int BodyFontSize = 16;
    public const int SmallFontSize = 13;
    public const int HeadingFontSize = 18;
    public const int ControlHeight = 36;
    public const int EdgeMargin = 16;
}

internal readonly struct ControlBounds
{
    public ControlBounds(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
}

internal static class CompactBuildLayout
{
    public static ControlBounds Heading => new(16, 10, 250, 28);
    public static ControlBounds Settings => new(298, 10, 50, 28);
    public static ControlBounds Close => new(354, 10, 50, 28);
    public static ControlBounds ActiveTitle => new(16, 102, 360, 28);
    public static ControlBounds Resize => new(388, 112, 28, 28);
    public static ControlBounds Input(bool gamepad) => new(16, 54, gamepad ? 184 : 272, OverlayUiTokens.ControlHeight);
    public static ControlBounds Load(bool gamepad) => new(gamepad ? 210 : 298, 54, gamepad ? 194 : 106, OverlayUiTokens.ControlHeight);
}
