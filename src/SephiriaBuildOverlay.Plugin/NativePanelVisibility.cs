namespace SephiriaBuildOverlay.Plugin;

internal static class NativePanelVisibility
{
    // UIBase.IsOpened is authoritative. Some panels expose an unused Showing
    // property which stays false even when Open() succeeds (weapon enhancement).
    public static bool IsVisible(bool active, bool enabled, bool? opened, bool? showing) =>
        active && enabled && (opened ?? showing ?? true);
}
