namespace SephiriaBuildOverlay.Plugin;

internal static class StartingPresetPolicy
{
    // The native lobby omits IsInDungeon. Absence is valid only when the
    // authoritative run flag explicitly says false (never on unknown state).
    public static bool IsLobby(int? inDungeon, bool? runStarted) => runStarted == false && (!inDungeon.HasValue || inDungeon == 0);

    public static bool CanApply(string? importContext, string? currentContext, string sourceVersion, string gameVersion,
        bool localOwner, bool serverActive, bool remotePlayers, bool requestPending, bool editingPreset) =>
        importContext is not null && importContext == currentContext && sourceVersion == "1.0.33" && gameVersion == "1.0.33" &&
        localOwner && serverActive && !remotePlayers && !requestPending && !editingPreset;
}
