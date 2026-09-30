using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private readonly bool _titleExitSmoke = TitleExitSmokePolicy.Enabled(Environment.GetCommandLineArgs());
    private bool _titleExitSmokeFinished;
    private float _nextTitleExitSmoke;
    private void TickTitleExitSmoke()
    {
        if (!_titleExitSmoke || _titleExitSmokeFinished || Time.unscaledTime < 15f || Time.unscaledTime < _nextTitleExitSmoke) return;
        _nextTitleExitSmoke = Time.unscaledTime + 1f;
        if (_gateway.TryTitleExitSmoke()) _titleExitSmokeFinished = true;
        else if (Time.unscaledTime > 90f)
        {
            _titleExitSmokeFinished = true;
            Logger.LogWarning("Exit smoke aborted: no safe title state. Game left running; no forced termination.");
        }
    }
}

internal sealed partial class UnityGameGateway
{
    public bool TryTitleExitSmoke()
    {
        if (_disposed) return false;
        FindLocalPlayerId(out _, out var player);
        // Bounded title reference, no invocation of arbitrary runtime commands.
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as System.Collections.IDictionary;
        foreach (var name in new[] { "UI_TitleLobby", "UI_FakeTitleLobby" })
        {
            var title = registry?[name] as Component;
            if (title == null || !TitleExitSmokePolicy.CanQuit(true, Time.unscaledTime,
                title.gameObject.activeInHierarchy && ReadBool(title, "IsOpened"), player != null, _requestPending)) continue;
            var quit = title.GetType().GetMethod("QuitGame", Type.EmptyTypes);
            if (quit is null) continue;
            _log.LogInfo("Exit smoke: native title QuitGame; no run/avatar or pending action.");
            quit.Invoke(title, null);
            return true;
        }
        return false;
    }
}
