namespace SephiriaBuildOverlay.Plugin;

internal static class StartingPresetPolicy
{
    // The native lobby omits IsInDungeon. Absence is valid only when the
    // authoritative run flag explicitly says false (never on unknown state).
    public static bool IsLobby(int? inDungeon, bool? runStarted) => runStarted == false && (!inDungeon.HasValue || inDungeon == 0);

    public static bool CanApply(string? importContext, string? currentContext, string sourceVersion, string gameVersion,
        bool localOwner, bool serverActive, bool remotePlayers, bool requestPending, bool editingPreset) =>
        // Version strings are informational. Native API, ownership, unlocks,
        // resource limits and context are verified separately at application.
        RejectionReason(importContext, currentContext, localOwner, serverActive, remotePlayers, requestPending, editingPreset) is null;

    public static string? RejectionReason(string? importContext, string? currentContext,
        bool localOwner, bool serverActive, bool remotePlayers, bool requestPending, bool editingPreset)
    {
        if (importContext is null || currentContext is null || importContext != currentContext)
            return "로비 또는 런 상태가 바뀌었습니다. 로비에서 빌드를 다시 불러오세요.";
        if (!localOwner) return "로컬 플레이어를 확인할 수 없습니다.";
        // Connection count and server authority are not local ownership gates.
        if (requestPending) return "게임 응답을 기다리는 중입니다. 완료 후 다시 불러오세요.";
        if (editingPreset) return "게임 프리셋 창이 열려 있거나 편집 중입니다. 창을 닫고 다시 불러오세요.";
        return null;
    }
}
