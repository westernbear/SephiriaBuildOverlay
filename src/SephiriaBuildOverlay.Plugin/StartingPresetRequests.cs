using System.Collections;
using System.Reflection;
using SephiriaBuildOverlay.Core.Models;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private ImportedBuild? _pendingStartingBuild;
    private DeferredStartingPreset? _pendingStartingGate;
    private float _nextStartingPresetAt;
    private float _startingLobbyWaitUntil;

    private StartingPresetResult QueueStartingPreset(ImportedBuild build, string? context, bool fromTitle)
    {
        _pendingStartingBuild = null; _pendingStartingGate = null;
        if (context is null && !fromTitle)
            return new StartingPresetResult("시작 세팅: 싱글플레이 로비에서만 적용합니다.");
        _pendingStartingBuild = build;
        _pendingStartingGate = new DeferredStartingPreset(context);
        _startingLobbyWaitUntil = context is null ? 0 : Time.unscaledTime + 10f;
        _nextStartingPresetAt = Time.unscaledTime;
        return new StartingPresetResult("로비 준비 후 시작 세팅을 자동 적용합니다.");
    }

    private void TickStartingPreset()
    {
        if (_lifetime.Stopped || _importing || _executing || _pendingStartingBuild is null ||
            _pendingStartingGate is null || Time.unscaledTime < _nextStartingPresetAt) return;
        _nextStartingPresetAt = Time.unscaledTime + .5f;
        var context = _gateway.StartingPresetContext();
        if (context is not null && _startingLobbyWaitUntil == 0) _startingLobbyWaitUntil = Time.unscaledTime + 10f;
        DeferredPresetDecision decision;
        try
        {
            decision = _pendingStartingGate.Observe(context, _gateway.StartingPresetUnsafeState(),
                context is not null && _gateway.StartingPresetReady());
        }
        catch (Exception ex) { Logger.LogWarning("Starting preset readiness failed: " + ex.Message); decision = DeferredPresetDecision.Cancel; }
        if (decision == DeferredPresetDecision.Wait && (_startingLobbyWaitUntil == 0 || Time.unscaledTime < _startingLobbyWaitUntil)) return;
        var build = _pendingStartingBuild;
        var gate = _pendingStartingGate;
        _pendingStartingBuild = null; _pendingStartingGate = null;
        if (decision != DeferredPresetDecision.Apply)
        {
            Logger.LogInfo("Deferred starting preset cancelled: lobby changed or native panel not ready.");
            Notify("시작 세팅을 적용하지 못했습니다. 로비에서 빌드를 다시 불러오세요.", NotificationKind.Warning);
            return;
        }
        _ = ApplyQueuedStartingPresetAsync(build, gate.Context!);
    }

    private async Task ApplyQueuedStartingPresetAsync(ImportedBuild build, string context)
    {
        _importing = true; _confirmRequested = false;
        try
        {
            var result = await _gateway.ApplyStartingPresetAsync(build, context, _lifetime.Token);
            if (_lifetime.Stopped) return;
            _status += "\n" + result.Detail;
            Notify(result.Applied ? "시작 세팅을 적용했습니다." : "시작 세팅을 적용하지 못했습니다.",
                result.Applied ? NotificationKind.Success : NotificationKind.Warning);
            if (result.Warning is not null) Notify(result.Warning, NotificationKind.Warning);
            _nextSnapshotAt = 0;
        }
        catch (OperationCanceledException) when (_lifetime.Stopped) { }
        catch (Exception ex) { if (!_lifetime.Stopped) { Logger.LogWarning(ex); Notify("시작 세팅 적용이 중단됐습니다.", NotificationKind.Warning); } }
        finally { _importing = false; }
    }
}

internal sealed partial class UnityGameGateway
{
    internal bool CanDeferStartingPresetFromTitle()
    {
        try { return ReadCanDeferStartingPresetFromTitle(); }
        catch { return false; }
    }

    private bool ReadCanDeferStartingPresetFromTitle()
    {
        FindLocalPlayerId(out _, out var player);
        if (_disposed || player != null || Convert.ToBoolean(ReadStatic("Mirror.NetworkServer", "active") ?? false)) return false;
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
        return new[] { "UI_TitleLobby", "UI_FakeTitleLobby" }.Any(name => registry?[name] is Component title &&
            title != null && title.gameObject.activeInHierarchy && ReadBool(title, "IsOpened"));
    }

    internal bool StartingPresetUnsafeState()
    {
        if (_disposed) return true;
        FindLocalPlayerId(out var owned, out var player);
        var dungeon = ReadStatic("DungeonManager", "Instance");
        if (dungeon is not null && ReadNamedObject(dungeon, "isRunStarted") is true) return true;
        var environment = dungeon is null ? null : ReadNamedObject(dungeon, "dungeonEnvironment") as IEnumerable;
        if (environment is not null)
            foreach (var pair in environment)
                if (pair is not null && ReadNamedString(pair, "Key") == "IsInDungeon" && ReadNamedNullableInt(pair, "Value") != 0) return true;
        if (player == null) return false;
        var connections = GameType("Mirror.NetworkServer")?.GetField("connections", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return !owned || !Convert.ToBoolean(ReadStatic("Mirror.NetworkServer", "active") ?? false) ||
            connections is null || ReadNamedNullableInt(connections, "Count") != 1;
    }

    internal bool StartingPresetReady()
    {
        FindLocalPlayerId(out var owned, out var player);
        var type = GameType("UI_PresetPanel");
        var avatarType = GameType("PlayerAvatar");
        if (_disposed || _requestPending || !owned || player == null || type is null || avatarType is null) return false;
        var avatar = player.GetComponent(avatarType);
        var matches = Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where(x => x != null &&
            x.gameObject.scene.IsValid() && ReferenceEquals(ReadNamedObject(x, "playerAvatar"), avatar)).ToArray();
        return matches.Length == 1 && ReadNamedObject(matches[0], "playerLocalDataStorage") is not null &&
            ReadNamedObject(matches[0], "playerSpawner") is not null &&
            !ReadBool(matches[0], "isEditingCurrentPreset") && !ReadBool(matches[0], "IsOpened") &&
            ReadNamedObject(matches[0], "baseWeaponDatas") is IEnumerable weapons && weapons.Cast<object>().Any();
    }
}
