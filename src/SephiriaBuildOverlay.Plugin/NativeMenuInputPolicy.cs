namespace SephiriaBuildOverlay.Plugin;

// CallbackContext menu handlers bypass InputAction.IsPressed/WasPressed gates.
// Keep movement/aim cancellation callbacks untouched to avoid sticky controls.
internal static class NativeMenuInputPolicy
{
    public static readonly IReadOnlyList<string> Handlers = new[] {
        "HandleOnSubmit", "HandleOnClick", "HandleCloseControl", "HandleChat",
        "HandleOnOpenCharacterStatusPanel", "HandleOpenStatsPanel", "HandleOpenPassivePanel",
        "HandleOpenPresetPanel", "HandleOpenLevelUpPanel", "HandleOnOpenDevCommandPanel", "HandleOnOpenMapPanel"
    };
    public static bool Allows(bool quitting, bool modalCapturing) => !quitting && !modalCapturing;
}
