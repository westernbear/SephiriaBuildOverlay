namespace SephiriaBuildOverlay.Plugin;

internal static class StartingPresetPolicy
{
    public static bool CanApply(string? importContext, string? currentContext, string sourceVersion, string gameVersion,
        bool localOwner, bool serverActive, bool remotePlayers, bool requestPending, bool editingPreset) =>
        importContext is not null && importContext == currentContext && sourceVersion == "1.0.33" && gameVersion == "1.0.33" &&
        localOwner && serverActive && !remotePlayers && !requestPending && !editingPreset;
}
