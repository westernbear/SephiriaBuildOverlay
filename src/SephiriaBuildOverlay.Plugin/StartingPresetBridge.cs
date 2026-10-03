using System.Collections;
using System.Reflection;
using HarmonyLib;
using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    internal string? StartingPresetContext()
    {
        try { return ReadStartingPresetContext(); }
        catch { return null; } // A destroyed/changed native object is not permission.
    }

    private string? ReadStartingPresetContext()
    {
        if (_disposed) return null;
        var id = FindLocalPlayerId(out var owned, out var player);
        var dungeon = ReadStatic("DungeonManager", "Instance") as Component;
        var profile = ReadStatic("SaveManager", "Binded");
        var connections = GameType("Mirror.NetworkServer")?.GetField("connections", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (!Convert.ToBoolean(ReadStatic("Mirror.NetworkServer", "active") ?? false) || connections is null || ReadNamedNullableInt(connections, "Count") != 1) return null;
        if (!owned || player == null || dungeon == null || string.IsNullOrWhiteSpace(profile?.ToString())) return null;
        var environment = ReadNamedObject(dungeon, "dungeonEnvironment") as IEnumerable;
        if (environment is null) return null;
        int? inDungeon = null;
        foreach (var pair in environment)
            if (pair is not null && ReadNamedString(pair, "Key") == "IsInDungeon")
            {
                inDungeon = ReadNamedNullableInt(pair, "Value");
                if (!inDungeon.HasValue) return null;
            }
        return StartingPresetPolicy.IsLobby(inDungeon, ReadNamedObject(dungeon, "isRunStarted") as bool?)
            ? id + ":" + dungeon.GetInstanceID() + ":" + profile + ":" + FindRunId(id) : null;
    }

    internal async Task<StartingPresetResult> ApplyStartingPresetAsync(ImportedBuild build, string? importContext, CancellationToken cancellationToken)
    {
        if (importContext is null) return new StartingPresetResult("시작 프리셋: 던전 밖에서 새로 가져올 때만 적용합니다.");
        object? panel = null;
        string? backup = null;
        var changed = false;
        var ownsPending = false;
        try
        {
            // Version metadata does not gate presets. Revalidate the actual
            // native API, local lobby context and unlocked options instead.
            var id = FindLocalPlayerId(out var owned, out var player);
            var server = Convert.ToBoolean(ReadStatic("Mirror.NetworkServer", "active") ?? false);
            var connections = GameType("Mirror.NetworkServer")?.GetField("connections", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            // An unknown connection count is not evidence of single-player.
            var remotePlayers = connections is null || (ReadNamedNullableInt(connections, "Count") ?? int.MaxValue) != 1;
            var type = GameType("UI_PresetPanel") ?? throw new InvalidOperationException("네이티브 프리셋 패널을 찾을 수 없습니다.");
            if (player == null) throw new InvalidOperationException("로컬 플레이어가 없습니다.");
            var avatar = player.GetComponent(GameType("PlayerAvatar")!);
            var matches = Resources.FindObjectsOfTypeAll(type).OfType<Component>()
                .Where(x => x != null && x.gameObject.scene.IsValid() && ReferenceEquals(ReadNamedObject(x, "playerAvatar"), avatar)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("연결된 프리셋 패널이 없거나 모호합니다.");
            panel = matches[0];
            var rejection = StartingPresetPolicy.RejectionReason(importContext, StartingPresetContext(),
                owned, server, remotePlayers, _requestPending, ReadBool(panel, "isEditingCurrentPreset") || ReadBool(panel, "IsOpened"));
            if (rejection is not null)
            {
                _log.LogWarning("Starting preset refused: " + rejection);
                return new StartingPresetResult("시작 프리셋 미적용: " + rejection, warning: rejection);
            }
            var storage = ReadNamedObject(panel, "playerLocalDataStorage") ?? throw new InvalidOperationException("플레이어 저장소가 없습니다.");
            var spawner = ReadNamedObject(panel, "playerSpawner") ?? throw new InvalidOperationException("플레이어 스포너가 없습니다.");
            if (ReadNamedObject(panel, "baseWeaponDatas") is not IEnumerable weapons || !weapons.Cast<object>().Any())
                throw new InvalidOperationException("시작 무기 카탈로그가 준비되지 않았습니다.");
            backup = (string)NativeCall(panel, "BuildCompactPresetData", "")!;
            var fallbackWarnings = new List<string>();
            var preset = !string.IsNullOrWhiteSpace(build.NativePresetCode)
                ? NativePreset.Decode(build.NativePresetCode!)
                : StartingPresetFallback.Create(build, backup, _catalog, DiscoverCatalogEntities(), VerifiedPassiveId, fallbackWarnings);
            StartingPresetFallback.ApplyFruitSkewer(preset, build.FruitSkewer);
            if (!string.IsNullOrWhiteSpace(build.NativePresetCode))
                StartingPresetFallback.ApplyWeapon(preset, build.WeaponSlug, _catalog, DiscoverCatalogEntities(), fallbackWarnings);
            var requested = preset.Compact();
            var locked = new StartingPresetWarnings();
            foreach (var data in weapons.Cast<object>())
            {
                var weapon = ReadNamedObject(data, "weaponEntity");
                if (weapon is null || ReadNamedNullableInt(weapon, "id") != preset.Weapon) continue;
                var unlockKey = ReadNamedString(data, "unlockKey");
                locked.Record("무기", preset.Weapon.ToString(), ReadNamedString(weapon, "aName"), true,
                    string.IsNullOrEmpty(unlockKey) || Convert.ToBoolean(StaticCall("SwitchManager", "GetDestinySwitch", unlockKey, false)));
            }
            var randomWeapon = ReadNamedObject(panel, "randomWeaponEntity");
            if (randomWeapon is not null && ReadNamedNullableInt(randomWeapon, "id") == preset.Weapon)
            {
                var allWeaponsUnlocked = weapons.Cast<object>().All(data =>
                {
                    var unlockKey = ReadNamedString(data, "unlockKey");
                    return string.IsNullOrEmpty(unlockKey) || Convert.ToBoolean(StaticCall("SwitchManager", "GetDestinySwitch", unlockKey, false));
                });
                locked.Record("무기", preset.Weapon.ToString(), ReadNamedString(randomWeapon, "aName"), true, allWeaponsUnlocked);
            }
            var costumeArgs = new object?[] { preset.Costume, preset.Skin };
            NativeCall(panel, "ValidateAndCorrectCostume", costumeArgs);
            var costume = (string)costumeArgs[0]!;
            var skinId = (string)costumeArgs[1]!;
            var requestedCostume = StaticCall("CostumeDatabase", "FindCostumeByID", preset.Costume);
            locked.Record("의상", preset.Costume, requestedCostume is null ? null : ReadNamedString(requestedCostume, "aName"),
                requestedCostume is not null, costume == preset.Costume);
            if (preset.Skin.Length > 0)
            {
                var requestedSkin = StaticCall("CostumeDatabase", "GetCostumeSkinByID", preset.Skin);
                var unlockType = requestedSkin is null ? null : ReadNamedString(requestedSkin, "unlockType");
                var skinMatches = requestedSkin is not null && ReadNamedString(requestedSkin, "relatedCostumeID") == costume;
                var saveData = ReadStatic("SaveManager", "Current");
                bool? purchased = saveData is null ? null : NativeCall(saveData, "GetBool", "SkinPurchased_" + preset.Skin, false) as bool?;
                var selectable = StartingOptionEligibility.SkinSelectable(requestedSkin is not null, skinMatches, unlockType, purchased);
                locked.Record("스킨", preset.Skin, requestedSkin is null ? null : ReadNamedString(requestedSkin, "aName"), requestedSkin is not null,
                    selectable);
                // Keep an owned Locked skin, but never purchase or manufacture
                // ownership for an unavailable/unknown/mismatched skin.
                skinId = selectable is true ? preset.Skin : "";
            }
            preset.SetValidatedCostume(costume, skinId);
            var passiveEntities = StaticCall("PassiveDatabase", "GetAll") as IEnumerable ?? throw new InvalidOperationException("특성 목록이 없습니다.");
            var passiveLimits = new Dictionary<ulong, int>();
            foreach (var entity in passiveEntities)
            {
                if (entity is null) continue;
                var passiveId = Convert.ToUInt64(ReadNamedObject(entity, "id"));
                var unlocked = ReadBool(entity, "isDefault") || Convert.ToBoolean(StaticCall("SwitchManager", "GetDestinySwitch", $"Passive_{passiveId}_Unlocked", false));
                if (preset.Passives.Any(x => x.Id == passiveId && x.Points > 0))
                    locked.Record("특성", passiveId.ToString(), ReadNamedString(entity, "aName"), true, unlocked);
                if (unlocked) passiveLimits.Add(passiveId, ReadNamedNullableInt(entity, "maxLevel") ?? 0);
            }
            // A shared preset's F list contains the author's journal favorites,
            // not just this build's artifacts or its starting pocket loadout.
            // IsCharmDiscovered uses a separate WitchHat_*_Found flag for some
            // items. It is NOT a general unlock/ownership predicate.
            foreach (var artifactId in preset.Favorites.ToArray())
            {
                var entity = StaticCall("ItemDatabase", "FindItemById", artifactId);
                if (entity is null || ReadNamedString(entity, "type") != "Charm" || ReadNamedString(entity, "activeType") is "Disabled" or "Hidden" or "TestOnly") continue;
                var discovered = NativeCall(panel, "IsCharmDiscovered", artifactId) as bool?;
                if (discovered is false)
                {
                    preset.Favorites.Remove(artifactId);
                    locked.RecordUnavailableFavorite(artifactId.ToString(), ReadNamedString(entity, "Name"), discovered);
                }
            }
            preset.LimitPassives(passiveLimits, ReadNamedNullableInt(avatar, "maxPassivePoint") ?? 0);
            // Resolve every required method before the first mutation.
            foreach (var name in new[] { "BuildCompactPresetData", "TryApplyCompactPresetData", "UpdateCurrentPlayer" })
                if (AccessTools.Method(type, name) is null) throw new MissingMethodException(type.Name, name);
            var save = AccessTools.Method(GameType("SaveManager"), "Save", new[] { typeof(bool), typeof(bool) })
                ?? throw new MissingMethodException("SaveManager.Save");
            if (StartingPresetContext() != importContext || FindLocalPlayerId(out owned, out _) != id || !owned)
                throw new InvalidOperationException("적용 직전 로컬 런 상태가 변경되었습니다.");
            cancellationToken.ThrowIfCancellationRequested();
            _requestPending = true;
            ownsPending = true;
            changed = true;
            ApplyCompact(panel, preset.Compact(includeLoadout: false));
            NativeCall(panel, "UpdateCurrentPlayer", null, false);
            // Host commands are queued through LocalConnectionToClient.Update,
            // not immediate. Observe the server-applied stats before capacity.
            await WaitForStartingStats(avatar!, preset, passiveEntities, importContext, cancellationToken);
            await WaitForStartingWeapon(panel, storage, avatar!, importContext, cancellationToken);
            // Native preset UI also waits 0.2s for perk/capacity refresh. Keep
            // temporary loadout empty until that refresh has completed.
            await Task.Delay(200, cancellationToken);
            if (_disposed || StartingPresetContext() != importContext) throw new InvalidOperationException("시작 프리셋 용량 갱신 중 상태가 변경되었습니다.");
            var unlockedCharms = (ReadNamedObject(spawner, "unlockedCharms") as IEnumerable)?.Cast<object>().Select(Convert.ToInt32).ToHashSet()
                ?? throw new InvalidOperationException("주머니 해금 목록이 없습니다.");
            var categories = (StaticCall("ItemDatabase", "GetAllItemCategory") as IEnumerable)?.Cast<object>()
                .Select(x => ReadNamedString(x, "id")!).Where(x => x is not null).ToHashSet(StringComparer.Ordinal)
                ?? throw new InvalidOperationException("과일 카테고리 목록이 없습니다.");
            int? PocketCost(int entityId)
            {
                var entity = StaticCall("ItemDatabase", "FindItemById", entityId);
                if (entity is null || ReadNamedString(entity, "type") != "Charm" || ReadNamedString(entity, "rarity") == "Eternal" ||
                    ReadBool(entity, "cannotBeReward") || ReadBool(entity, "isDual") || ReadNamedString(entity, "activeType") is "Disabled" or "Hidden" or "TestOnly") return null;
                var unlocked = unlockedCharms.Contains(entityId);
                locked.Record("시작 아티팩트", entityId.ToString(), ReadNamedString(entity, "Name"), true, unlocked);
                if (!unlocked) return null;
                var prefab = ReadNamedObject(entity, "resourcePrefab") as GameObject;
                var charmType = GameType("Charm_Basic");
                var charm = prefab != null && charmType is not null ? prefab.GetComponent(charmType) : null;
                if (charm is null) return null;
                if (ReadBool(charm, "isWeaponRelatedCharm"))
                {
                    // Same compatibility predicate as UI_DimensionPocketPanel.
                    var weaponType = GameType("WeaponControllerSimple");
                    var controller = weaponType is null ? null : player.GetComponent(weaponType);
                    var weapon = controller is null ? null : ReadNamedObject(controller, "currentWeapon");
                    var family = weapon is null ? null : ReadNamedString(weapon, "weaponType");
                    if (family is null || family != ReadNamedString(charm, "relatedWeapon")) return null;
                }
                return Convert.ToInt32(StaticCall("UI_DimensionPocketPanel", "GetCapacity", ReadNamedObject(entity, "rarity")));
            }
            var inventory = ReadNamedObject(avatar!, "Inventory") ?? throw new InvalidOperationException("인벤토리가 없습니다.");
            var fruitCapacity = Const("fruitSkewerDefaultCount") + Convert.ToInt32(NativeCall(avatar!, "GetCustomStatUnsafe", "FRUITCOUNT"));
            preset.LimitLoadout(PocketCost, ReadNamedNullableInt(inventory, "dimensionPocket") ?? 0, categories, fruitCapacity,
                Const("fruitSkewerPlusLimitCount"), Const("fruitSkewerMinusLimitCount"),
                entityId => ReadNamedString(StaticCall("ItemDatabase", "FindItemById", entityId)!, "rarity") == "Common");
            if (StartingPresetContext() != importContext) throw new InvalidOperationException("던전 상태가 변경되었습니다.");
            var message = ApplyCompact(panel, preset.Compact());
            NativeCall(panel, "UpdateCurrentPlayer", null, false);
            await WaitForStartingStats(avatar!, preset, passiveEntities, importContext, cancellationToken);
            var weaponWarning = await WaitForStartingWeapon(panel, storage, avatar!, importContext, cancellationToken);
            await WaitForStartingFruit(storage, preset, importContext, cancellationToken);
            var applied = NativePreset.ParseCompact((string)NativeCall(panel, "BuildCompactPresetData", "")!);
            locked.RemoveAppliedOptions(applied);
            save.Invoke(null, new object[] { true, false });
            _log.LogInfo("Starting preset applied through native import/update; source=" + (string.IsNullOrWhiteSpace(build.NativePresetCode) ? "wiki-fields" : "preset-code") + "; permanent purchases excluded.");
            _log.LogInfo("Starting fruit skewer confirmed: source=" + (build.FruitSkewer is null ? "native-preset" : "wiki-fields") +
                ", adaptive=" + preset.Adaptive + ", fruits=" + string.Join(";", preset.Fruits.Select(x => x.Category + ":" + x.Value)));
            foreach (var warning in fallbackWarnings) _log.LogWarning("Starting preset: " + warning);
            foreach (var item in locked.Items) _log.LogWarning("Starting preset locked option excluded: " + item);
            foreach (var item in locked.FavoriteItems) _log.LogWarning("Starting preset favorite unavailable in journal (build target retained): " + item);
            var adjusted = StartingPresetComparison.ChangedSections(NativePreset.ParseCompact(requested), applied);
            var limited = adjusted.Count > 0 ? " 조정된 항목: " + string.Join(" · ", adjusted) + "." : "";
            var resultWarning = locked.Bubble ?? (adjusted.Count > 0 ? "현재 포인트·용량·선택 조건에 맞춰 조정했습니다.\n" + string.Join(" · ", adjusted) :
                fallbackWarnings.Count > 0 ? "일부 시작 옵션을 매핑하지 못했습니다. 현재 설정을 확인하세요." : null);
            if (weaponWarning is not null) resultWarning = resultWarning is null ? weaponWarning : resultWarning + "\n" + weaponWarning;
            return new StartingPresetResult("시작 프리셋 적용 완료 (현재 설정만 변경, 저장 슬롯 유지)." + limited + "\n" + message,
                applied: true, warning: resultWarning);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Starting preset failed: " + ex);
            if (_disposed || cancellationToken.IsCancellationRequested)
                return new StartingPresetResult("시작 프리셋 적용이 종료/취소되었습니다.", warning: "시작 세팅 적용이 중단됐습니다. 현재 설정을 확인하세요.");
            if (changed && panel is not null && backup is not null && StartingPresetContext() == importContext)
            {
                try
                {
                    ApplyCompact(panel, backup);
                    NativeCall(panel, "UpdateCurrentPlayer", null, false);
                    var previous = NativePreset.ParseCompact(backup);
                    var entities = StaticCall("PassiveDatabase", "GetAll") as IEnumerable ?? throw new InvalidOperationException("특성 목록이 없습니다.");
                    var avatar = ReadNamedObject(panel, "playerAvatar") ?? throw new InvalidOperationException("플레이어가 없습니다.");
                    await WaitForStartingStats(avatar, previous, entities, importContext!, cancellationToken);
                    var storage = ReadNamedObject(panel, "playerLocalDataStorage") ?? throw new InvalidOperationException("플레이어 저장소가 없습니다.");
                    await WaitForStartingWeapon(panel, storage, avatar, importContext!, cancellationToken);
                    await WaitForStartingFruit(storage, previous, importContext!, cancellationToken);
                }
                catch (Exception restoreError) { _log.LogError("Starting preset rollback failed: " + restoreError); return new StartingPresetResult("시작 프리셋 실패 및 복원 실패", warning: "시작 세팅 복원 실패. 네이티브 프리셋에서 현재 설정을 확인하세요."); }
            }
            else if (changed) return new StartingPresetResult("시작 프리셋 일부 적용 후 런/플레이어 변경", warning: "시작 세팅 일부 적용 후 중단됐습니다. 현재 설정을 확인하세요.");
            return new StartingPresetResult("시작 프리셋 미적용/복원: " + ex.GetBaseException().Message, warning: "시작 세팅을 적용하지 못했습니다. 기존 설정을 유지합니다.");
        }
        finally { if (ownsPending && !_disposed) _requestPending = false; }
    }

    private ulong? VerifiedPassiveId(string slug)
    {
        var expected = StartingPassiveMapping.Expected(slug);
        var entities = StaticCall("PassiveDatabase", "GetAll") as IEnumerable;
        var matches = entities?.Cast<object>().Where(x => Convert.ToUInt64(ReadNamedObject(x, "id")) == expected.Item1).ToArray();
        if (expected.Item1 == 0 || matches?.Length != 1) return null;
        var localized = ReadNamedObject(matches[0], "aName");
        return localized is not null && StartingPassiveMapping.Matches(slug, expected.Id, ReadNamedString(localized, "key"), ReadNamedString(matches[0], "aName"))
            ? expected.Item1 : null;
    }

    private async Task WaitForStartingStats(object avatar, NativePreset preset, IEnumerable entities, string context, CancellationToken token)
    {
        var deadline = Time.unscaledTime + 5f;
        // Allow the host's queued transport to flush even for identical settings.
        do
        {
            await Task.Delay(50, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || StartingPresetContext() != context) throw new InvalidOperationException("시작 프리셋 적용 중 런/플레이어가 변경되었습니다.");
            var complete = true;
            foreach (var entity in entities)
            {
                if (entity is null) continue;
                var id = Convert.ToUInt64(ReadNamedObject(entity, "id"));
                var expected = preset.Passives.FirstOrDefault(x => x.Id == id).Points;
                if (Convert.ToInt32(NativeCall(avatar, "GetPassiveStat", id)) != expected) complete = false;
            }
            if (complete) return;
        } while (Time.unscaledTime < deadline);
        throw new TimeoutException("시작 프리셋 특성 요청 결과가 5초 안에 확인되지 않았습니다.");
    }

    private async Task<string?> WaitForStartingWeapon(object panel, object storage, object avatar, string context, CancellationToken token)
    {
        // Native import may replace an unavailable selection, and a costume
        // may force its own weapon. Verify those accepted rules, not a grant.
        var accepted = NativePreset.ParseCompact((string)NativeCall(panel, "BuildCompactPresetData", "")!);
        var costume = StaticCall("CostumeDatabase", "FindCostumeByID", accepted.Costume)
            ?? throw new InvalidOperationException("적용된 의상을 확인할 수 없습니다.");
        var forcedWeapon = ReadNamedObject(costume, "defaultWeapon");
        var forcedId = forcedWeapon is null ? null : ReadNamedNullableInt(forcedWeapon, "id");
        if (forcedWeapon is not null && !forcedId.HasValue) throw new InvalidOperationException("의상의 고정 무기를 확인할 수 없습니다.");
        var controllerType = GameType("WeaponControllerSimple") ?? throw new InvalidOperationException("무기 컨트롤러가 없습니다.");
        var deadline = Time.unscaledTime + 5f;
        do
        {
            await Task.Delay(50, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || StartingPresetContext() != context) throw new InvalidOperationException("시작 무기 적용 중 런/플레이어가 변경되었습니다.");
            var controller = (avatar as Component)?.GetComponent(controllerType) ?? ReadNamedObject(avatar, "weaponController");
            var weapon = controller is null ? null : ReadNamedObject(controller, "currentWeapon");
            var equipped = weapon is null ? null : ReadNamedNullableInt(weapon, "entityId");
            if (!StartingWeaponState.Matches(accepted.Weapon, forcedId, ReadNamedNullableInt(storage, "defaultWeapon"), equipped)) continue;
            _log.LogInfo("Starting weapon confirmed: selected=" + accepted.Weapon + ", equipped=" + equipped + ", costumeFixed=" + forcedId);
            return forcedId.HasValue && forcedId != accepted.Weapon ? "선택한 의상의 고정 시작 무기가 적용됩니다." : null;
        } while (Time.unscaledTime < deadline);
        throw new TimeoutException("시작 무기 장착 결과가 5초 안에 확인되지 않았습니다.");
    }

    private async Task WaitForStartingFruit(object storage, NativePreset preset, string context, CancellationToken token)
    {
        var deadline = Time.unscaledTime + 5f;
        do
        {
            await Task.Delay(50, token);
            token.ThrowIfCancellationRequested();
            if (_disposed || StartingPresetContext() != context) throw new InvalidOperationException("과일꼬치 적용 중 런/플레이어가 변경되었습니다.");
            List<(string Category, int Value)>? fruits = null;
            if (ReadNamedObject(storage, "fruitSkewerBonus") is IEnumerable entries)
            {
                fruits = new();
                foreach (var entry in entries)
                {
                    var category = entry is null ? null : ReadNamedString(entry, "categoryName");
                    var value = entry is null ? null : ReadNamedNullableInt(entry, "weight");
                    if (string.IsNullOrWhiteSpace(category) || !value.HasValue || fruits.Count >= 512) { fruits = null; break; }
                    fruits.Add((category!, value.Value));
                }
            }
            if (StartingFruitState.Matches(preset, ReadNamedNullableInt(storage, "adaptiveItemDropBonus"), fruits)) return;
        } while (Time.unscaledTime < deadline);
        throw new TimeoutException("과일꼬치 적용 결과가 5초 안에 확인되지 않았습니다.");
    }

    private static object? NativeCall(object instance, string method, params object?[] args) =>
        (AccessTools.Method(instance.GetType(), method) ?? throw new MissingMethodException(instance.GetType().Name, method)).Invoke(instance, args);
    private object? StaticCall(string type, string method, params object?[] args) =>
        (AccessTools.Method(GameType(type), method) ?? throw new MissingMethodException(type, method)).Invoke(null, args);
    private int Const(string name) => Convert.ToInt32(StaticCall("KeywordDatabase", "GetConstValue", name, true));
    private static string ApplyCompact(object panel, string compact)
    {
        // -1 is the native 'no saved slot' sentinel. Never overwrite/create an
        // actual Preset_0... slot or change Preset_SelectedSlot / SlotExists.
        var args = new object?[] { -1, compact, "" };
        if (NativeCall(panel, "TryApplyCompactPresetData", args) is not true) throw new InvalidOperationException(args[2]?.ToString());
        return NotificationText.Plain(args[2]?.ToString());
    }
}
