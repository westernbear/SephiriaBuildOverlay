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
            if (Environment.GetCommandLineArgs().Contains("--sbo-starting-catalog-smoke", StringComparer.Ordinal))
            {
                try
                {
                    VerifyStartingPresetCatalog();
                    _log.LogInfo("Starting catalog (read-only): " + Newtonsoft.Json.JsonConvert.SerializeObject(ReadStartingPresetCatalog()));
                }
                catch (Exception ex) { _log.LogWarning("Starting catalog unavailable at title: " + ex.GetBaseException().Message); }
            }
            if (Environment.GetCommandLineArgs().Contains("--sbo-native-ui-smoke", StringComparer.Ordinal))
            {
                try
                {
                    // Construct INACTIVE native primitives only. No modal,
                    // EventSystem swap, game input, import or purchase occurs.
                    using var window = CreateBuildWindow(_ => { }, _ => { }, () => { }, () => { })
                        ?? throw new InvalidOperationException("Native UI template unavailable.");
                    window.Render(false, true, "", 1, new ControllerMenu());
                    _log.LogInfo("Native UI construction PASS: " + Newtonsoft.Json.JsonConvert.SerializeObject(window.Diagnostics()));
                    var settingsSurface = ModalCursorPresentation.Choose(true, true, false, true, true);
                    if (settingsSurface != ModalCursorSurface.System) throw new InvalidOperationException("Settings cursor must use the system surface.");
                    var cursor = ReadStatic("UI_Cursor", "Current");
                    var cursorImage = cursor is null ? null : ReadNamedObject(cursor, "image") as Component;
                    var nativeCursor = cursorImage != null && ReadNamedObject(cursorImage, "sprite") is Sprite;
                    _log.LogInfo("Modal cursor contract PASS: settingsSurface=" + settingsSurface +
                        ", nativeSpriteAvailable=" + nativeCursor + ", settingsUsesImguiCursor=False");
                    RestoreNativeModalCursor(false);
                }
                catch (Exception ex) { _log.LogWarning("Native UI construction FAILED: " + ex); }
            }
            _log.LogInfo("Exit smoke: native title QuitGame; no run/avatar or pending action.");
            quit.Invoke(title, null);
            return true;
        }
        return false;
    }
}
