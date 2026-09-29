using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class UnityGameGateway : IGameActionGateway, IDisposable
{
    private readonly VersionedCatalog _catalog;
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, Action> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RectTransform> _rectangles = new(StringComparer.Ordinal);
    private RunSnapshot? _lastSnapshot;
    private string? _highlightToken;
    private TaskCompletionSource<bool>? _confirmation;
    private long _requestedRevision;
    private bool _requestPending;

    public UnityGameGateway(VersionedCatalog catalog, ManualLogSource log)
    {
        _catalog = catalog;
        _log = log;
    }

    public RunSnapshot CaptureOnMainThread()
    {
        var playerId = FindLocalPlayerId(out var owned, out var localPlayer);
        var screen = FindActiveScreen(out var panel);
        _actions.Clear(); _rectangles.Clear();
        var candidates = new List<ScreenCandidate>();
        if (panel is not null)
        {
            foreach (var button in panel.GetComponentsInChildren<Component>(true)
                         .Where(x => x != null && x.GetType().FullName == "UnityEngine.UI.Button" && x.gameObject.activeInHierarchy))
            {
                var token = button.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
                var text = CollectVisibleText(button.gameObject);
                var gameKey = FindCatalogKey(button.gameObject, ExpectedKind(screen));
                var kind = ClassifyCandidate(screen, gameKey, text, button.name);
                if (kind is null) continue;
                var moneyCost = ReadNamedInt(button.gameObject, "price", "moneyCost", "costGold");
                var diceCost = ReadNamedInt(button.gameObject, "diceCost", "rerollCost");
                var free = kind == CandidateKind.Reroll && (diceCost == 0 || ContainsAny(text + button.name, "무료", "free"));
                candidates.Add(new ScreenCandidate(token, kind.Value, gameKey, moneyCost, diceCost, free, ReadBool(button, "interactable")));
                _actions[token] = () => InvokeButton(button);
                if (button.transform is RectTransform rect) _rectangles[token] = rect;
            }
        }

        if (candidates.Count == 0 && localPlayer is not null)
            screen = CaptureWorldCandidates(localPlayer, candidates);

        var runId = FindRunId(playerId);
        var inventory = ReadInventory(localPlayer);
        var money = ReadPlayerInt(localPlayer, "Money", "currentMoney", "money");
        var dice = ReadPlayerInt(localPlayer, "rerollDice", "diceCount", "currentDice");
        var currentWeapon = ReadCurrentWeapon(localPlayer);
        var currentMiracle = GetLocalMiracleKeys(localPlayer).FirstOrDefault();
        var revision = StableRevision(runId, playerId, screen, candidates, inventory, currentWeapon, currentMiracle, money, dice);
        _lastSnapshot = new RunSnapshot(runId, playerId, revision, screen, candidates, inventory,
            currentWeapon, currentMiracle, money, dice, owned, _requestPending);
        return _lastSnapshot;
    }

    public void Tick()
    {
        if (_confirmation is null) return;
        var snapshot = CaptureOnMainThread();
        if (snapshot.Revision != _requestedRevision)
        {
            _requestPending = false;
            _confirmation.TrySetResult(true);
            _confirmation = null;
        }
    }

    public Task<RunSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(CaptureOnMainThread());

    public Task<GameActionReceipt> SendThroughNormalRequestPathAsync(RecommendedAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_actions.TryGetValue(action.TargetToken, out var invoke))
            return Task.FromResult(new GameActionReceipt(Guid.NewGuid().ToString("N"), false, "게임 대상이 사라졌습니다."));
        _requestedRevision = _lastSnapshot?.Revision ?? 0;
        _requestPending = true;
        try
        {
            // Button.onClick is the game's normal UI/request route; no inventory or network state is mutated directly.
            invoke();
            return Task.FromResult(new GameActionReceipt(Guid.NewGuid().ToString("N"), true));
        }
        catch (Exception ex)
        {
            _requestPending = false;
            _log.LogWarning(ex);
            return Task.FromResult(new GameActionReceipt(Guid.NewGuid().ToString("N"), false, ex.Message));
        }
    }

    public Task<bool> WaitForServerConfirmationAsync(GameActionReceipt receipt, CancellationToken cancellationToken)
    {
        _confirmation = new TaskCompletionSource<bool>();
        cancellationToken.Register(() =>
        {
            _requestPending = false;
            _confirmation?.TrySetCanceled();
            _confirmation = null;
        });
        return _confirmation.Task;
    }

    public IReadOnlyList<GameEntityDescriptor> DiscoverCatalogEntities()
    {
        var found = new List<GameEntityDescriptor>();
        var objects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
            .Where(x => x != null && RelevantType(x.GetType())).ToArray();
        foreach (var entry in _catalog.Entries)
        {
            var matches = objects.Where(obj => ObjectContainsExactString(obj, entry.GameKey) && ObjectContainsExactString(obj, entry.KoreanName)).Take(2).ToArray();
            foreach (var match in matches)
            {
                var actualCategory = entry.Kind == CatalogKind.Artifact ? ReadCategoryIds(match) : null;
                var actualTier = entry.Kind == CatalogKind.Weapon ? ComputeWeaponTier(match, objects) : ReadNamedNullableInt(match, "tier", "weaponTier");
                found.Add(new GameEntityDescriptor(
                    entry.GameKey,
                    entry.Kind,
                    entry.KoreanName,
                    ReadNamedString(match, "rarity", "tierName"),
                    actualCategory,
                    actualTier,
                    entry.Kind == CatalogKind.Weapon ? ReadParentKey(match) : null));
            }
        }
        return found;
    }

    public void SetHighlight(string? targetToken) => _highlightToken = targetToken;

    public IReadOnlyList<string> GetLocalMiracleKeys()
    {
        FindLocalPlayerId(out _, out var player);
        return GetLocalMiracleKeys(player);
    }

    public bool IsLocalInventory(object inventory)
    {
        FindLocalPlayerId(out _, out var player);
        if (player is null) return false;
        return player.GetComponentsInChildren<Component>(true).Any(component =>
        {
            if (component.GetType().Name is not ("UnitAvatar" or "PlayerAvatar")) return false;
            var linked = ReadNamedObject(component, "Inventory");
            return ReferenceEquals(linked, inventory);
        });
    }

    public void DrawHighlight()
    {
        if (_highlightToken is null || !_rectangles.TryGetValue(_highlightToken, out var rect) || rect == null) return;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var camera = rect.GetComponentInParent<Canvas>()?.worldCamera;
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        var screenRect = new Rect(bottomLeft.x, Screen.height - topRight.y, topRight.x - bottomLeft.x, topRight.y - bottomLeft.y);
        var old = GUI.color; GUI.color = Color.yellow;
        GUI.Box(new Rect(screenRect.x - 3, screenRect.y - 3, screenRect.width + 6, 3), GUIContent.none);
        GUI.Box(new Rect(screenRect.x - 3, screenRect.yMax, screenRect.width + 6, 3), GUIContent.none);
        GUI.Box(new Rect(screenRect.x - 3, screenRect.y, 3, screenRect.height), GUIContent.none);
        GUI.Box(new Rect(screenRect.xMax, screenRect.y, 3, screenRect.height), GUIContent.none);
        GUI.color = old;
    }

    public void Dispose()
    {
        _confirmation?.TrySetCanceled();
        _confirmation = null;
    }

    private static ScreenKind FindActiveScreen(out GameObject? panel)
    {
        var map = new (string Type, ScreenKind Screen)[]
        {
            ("UI_MiraclePanel", ScreenKind.MiracleChoice),
            ("UI_WeaponEnhancementPanel", ScreenKind.WeaponUpgrade),
            ("UI_ShopPanel", ScreenKind.Shop),
            ("UI_TabletMixPanel", ScreenKind.TabletBoard),
            ("UI_InventoryViewer", ScreenKind.Inventory),
            ("MiracleSelector", ScreenKind.MiracleChoice),
            ("InventorySelector", ScreenKind.ArtifactReward)
        };
        foreach (var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (component == null || !component.gameObject.activeInHierarchy || !component.enabled) continue;
            var hit = map.FirstOrDefault(x => x.Type == component.GetType().Name);
            if (hit.Type is not null) { panel = component.gameObject; return hit.Screen; }
        }
        panel = null;
        return ScreenKind.None;
    }

    private string? FindCatalogKey(GameObject gameObject, CatalogKind? expectedKind)
    {
        var strings = EnumerateNearbyValues(gameObject).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var matches = _catalog.Entries.Where(x => (expectedKind is null || x.Kind == expectedKind) &&
            (strings.Contains(x.GameKey) || strings.Contains(x.Slug)) && strings.Contains(x.KoreanName))
            .Select(x => x.GameKey).Distinct().Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private ScreenKind CaptureWorldCandidates(GameObject localPlayer, List<ScreenCandidate> candidates)
    {
        var nearby = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
            .Where(x => x != null && x.enabled && x.gameObject.activeInHierarchy &&
                        x.GetType().Name is "MiracleOrb" or "WeaponSpawner" or "InventoryShop" or "DroppedInventoryAccess")
            .Select(x => new { Component = x, Distance = (x.transform.position - localPlayer.transform.position).sqrMagnitude })
            .Where(x => x.Distance <= 225f)
            .OrderBy(x => x.Distance)
            .Take(12)
            .ToArray();
        if (nearby.Length == 0) return ScreenKind.None;

        var nearestType = nearby[0].Component.GetType().Name;
        var screen = nearestType switch
        {
            "MiracleOrb" => ScreenKind.MiracleChoice,
            "WeaponSpawner" => ScreenKind.WeaponUpgrade,
            "InventoryShop" => ScreenKind.Shop,
            "DroppedInventoryAccess" => ScreenKind.ArtifactReward,
            _ => ScreenKind.None
        };
        var expected = ExpectedKind(screen);
        foreach (var candidate in nearby.Where(x => x.Component.GetType().Name == nearestType))
        {
            var component = candidate.Component;
            var key = FindCatalogKey(component.gameObject, expected);
            if (key is null) continue;
            var interactive = component.GetType().GetMethod("Interactive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(GameObject) }, null);
            if (interactive is null) continue;
            var selectable = true;
            var canInteract = component.GetType().GetMethod("IsInteractable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(GameObject) }, null);
            try { if (canInteract is not null) selectable = Convert.ToBoolean(canInteract.Invoke(component, new object[] { localPlayer })); }
            catch { selectable = false; }
            var token = "world:" + component.GetInstanceID();
            var kind = screen switch
            {
                ScreenKind.MiracleChoice => CandidateKind.Miracle,
                ScreenKind.WeaponUpgrade => CandidateKind.Weapon,
                _ => CandidateKind.Artifact
            };
            var price = ReadNamedNullableInt(component, "price", "cost") ?? 0;
            candidates.Add(new ScreenCandidate(token, kind, key, moneyCost: price, isSelectable: selectable));
            _actions[token] = () => interactive.Invoke(component, new object[] { localPlayer });
        }
        return screen;
    }

    private List<InventoryArtifact> ReadInventory(GameObject? localPlayer)
    {
        var result = new List<InventoryArtifact>();
        if (localPlayer is null) return result;
        var inventory = localPlayer.GetComponentsInChildren<Component>(true)
            .Select(x => ReadNamedObject(x, "Inventory"))
            .FirstOrDefault(x => x?.GetType().Name == "GridInventory");
        var matrix = inventory is null ? null : ReadNamedObject(inventory, "inventoryMatrix") as IEnumerable;
        if (matrix is null) return result;
        foreach (var pair in matrix)
        {
            if (pair is null) continue;
            var value = ReadNamedObject(pair, "Value");
            if (value is null) continue;
            var entityId = ReadNamedNullableInt(value, "EntityID", "entityID", "IEntityID");
            var instanceId = ReadNamedNullableInt(value, "InstanceID", "instanceID", "IUID");
            if (!entityId.HasValue || !instanceId.HasValue) continue;
            var key = entityId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_catalog.Entries.All(x => x.Kind != CatalogKind.Artifact || x.GameKey != key)) continue;
            var xPosition = ReadNamedNullableInt(value, "XIdx", "x") ?? -1;
            var yPosition = ReadNamedNullableInt(value, "YIdx", "y") ?? -1;
            result.Add(new InventoryArtifact(instanceId.Value.ToString(), key, xPosition, yPosition));
        }
        return result.GroupBy(x => x.InstanceId).Select(x => x.First()).ToList();
    }

    private static CandidateKind? ClassifyCandidate(ScreenKind screen, string? key, string text, string objectName)
    {
        var searchable = text + " " + objectName;
        if (ContainsAny(searchable, "reroll", "re-roll", "리롤", "주사위")) return CandidateKind.Reroll;
        if (ContainsAny(searchable, "abandon", "convert", "포기", "변환")) return CandidateKind.AbandonOrConvert;
        if (key is null) return null;
        return screen switch
        {
            ScreenKind.WeaponUpgrade => CandidateKind.Weapon,
            ScreenKind.MiracleChoice => CandidateKind.Miracle,
            ScreenKind.Shop => CandidateKind.Artifact,
            ScreenKind.ArtifactReward => CandidateKind.Artifact,
            _ => null
        };
    }

    private static CatalogKind? ExpectedKind(ScreenKind screen) => screen switch
    {
        ScreenKind.WeaponUpgrade => CatalogKind.Weapon,
        ScreenKind.MiracleChoice => CatalogKind.Miracle,
        ScreenKind.Shop or ScreenKind.ArtifactReward or ScreenKind.Inventory => CatalogKind.Artifact,
        _ => null
    };

    private static string FindLocalPlayerId(out bool owned, out GameObject? playerObject)
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (component == null || component.GetType().Name != "NetworkIdentity") continue;
            if (ReadBool(component, "isLocalPlayer", "isOwned", "hasAuthority"))
            {
                owned = true;
                playerObject = component.gameObject;
                return component.GetInstanceID().ToString();
            }
        }
        // No network identity is normal in single-player.
        var player = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
            .FirstOrDefault(x => x != null && x.gameObject.activeInHierarchy && x.GetType().Name == "PlayerAvatar");
        owned = true;
        playerObject = player?.gameObject;
        return player is null ? "single-player" : "local-" + player.GetInstanceID();
    }

    private string? ReadCurrentWeapon(GameObject? localPlayer)
    {
        if (localPlayer is null) return null;
        foreach (var controller in localPlayer.GetComponentsInChildren<Component>(true)
                     .Where(x => x.GetType().Name is "NewWeaponController" or "WeaponController"))
        {
            var current = ReadNamedObject(controller, "CurrentWeapon") ?? ReadNamedObject(controller, "CurrentWeaponInHand");
            var entity = current is null ? null : ReadNamedObject(current, "Entity");
            var id = entity is null ? ReadNamedNullableInt(controller, "currentEquippedWeaponID") : ReadNamedNullableInt(entity, "id");
            if (id.HasValue) return id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    private IReadOnlyList<string> GetLocalMiracleKeys(GameObject? localPlayer)
    {
        if (localPlayer is null) return Array.Empty<string>();
        var ids = new List<string>();
        foreach (var controller in localPlayer.GetComponentsInChildren<Component>(true).Where(x => x.GetType().Name == "MiracleController"))
        {
            if (ReadNamedObject(controller, "miracles") is not IEnumerable miracles) continue;
            foreach (var metadata in miracles)
            {
                if (metadata is null) continue;
                var id = ReadNamedString(metadata, "id");
                if (!string.IsNullOrWhiteSpace(id) && _catalog.Entries.Any(x => x.Kind == CatalogKind.Miracle && x.GameKey == id)) ids.Add(id!);
            }
        }
        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static int ReadPlayerInt(GameObject? localPlayer, params string[] names)
    {
        if (localPlayer is null) return 0;
        foreach (var component in localPlayer.GetComponentsInChildren<Component>(true))
        {
            var value = ReadNamedNullableInt(component, names);
            if (value.HasValue) return Math.Max(0, value.Value);
        }
        return 0;
    }

    private static string FindRunId(string playerId)
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (component == null || !ContainsAny(component.GetType().Name, "run", "stage", "gameManager")) continue;
            var id = ReadNamedString(component, "runId", "sessionId", "gameId");
            if (!string.IsNullOrWhiteSpace(id)) return id!;
        }
        // The local player object normally survives floor scene changes and is recreated for a new run.
        return "player-" + playerId;
    }

    private static long StableRevision(string runId, string playerId, ScreenKind screen, IReadOnlyList<ScreenCandidate> candidates,
        IReadOnlyList<InventoryArtifact> inventory, string? weapon, string? miracle, int money, int dice)
    {
        unchecked
        {
            long hash = 1469598103934665603L;
            void Add(string? value) { foreach (var c in value ?? string.Empty) { hash ^= c; hash *= 1099511628211L; } }
            Add(runId); Add(playerId); Add(screen.ToString()); Add(weapon); Add(miracle); Add(money.ToString()); Add(dice.ToString());
            foreach (var candidate in candidates.OrderBy(x => x.Token)) { Add(candidate.Token); Add(candidate.CatalogKey); Add(candidate.MoneyCost.ToString()); Add(candidate.DiceCost.ToString()); }
            foreach (var item in inventory.OrderBy(x => x.InstanceId)) { Add(item.InstanceId); Add(item.CatalogKey); }
            return hash;
        }
    }

    private static string CollectVisibleText(GameObject gameObject)
    {
        var values = new List<string>();
        foreach (var component in gameObject.GetComponentsInChildren<Component>(true))
        {
            var property = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (property?.PropertyType == typeof(string))
            {
                try { if (property.GetValue(component) is string text) values.Add(text); } catch { }
            }
        }
        return string.Join(" ", values);
    }

    private static IEnumerable<object?> EnumerateNearbyValues(GameObject gameObject)
    {
        foreach (var component in gameObject.GetComponentsInParent<Component>(true).Take(12))
        foreach (var value in ReadNestedSimpleMembers(component, 2, new HashSet<object>(ReferenceEqualityComparer.Instance))) yield return value;
        foreach (var component in gameObject.GetComponentsInChildren<Component>(true).Take(30))
        foreach (var value in ReadNestedSimpleMembers(component, 2, new HashSet<object>(ReferenceEqualityComparer.Instance))) yield return value;
    }

    private static IEnumerable<object?> ReadNestedSimpleMembers(object obj, int depth, HashSet<object> visited)
    {
        if (obj is null || !visited.Add(obj)) yield break;
        foreach (var value in ReadSimpleMembers(obj)) yield return value;
        if (depth <= 0) yield break;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in obj.GetType().GetFields(flags))
        {
            object? child; try { child = field.GetValue(obj); } catch { continue; }
            if (child is null || IsSimple(field.FieldType) || child is GameObject || child is Transform) continue;
            if (child is IEnumerable enumerable && child is not string)
            {
                var count = 0;
                foreach (var element in enumerable)
                {
                    if (element is not null)
                        foreach (var value in ReadNestedSimpleMembers(element, depth - 1, visited)) yield return value;
                    if (++count >= 20) break;
                }
            }
            else if (child is UnityEngine.Object || field.FieldType.Namespace is null ||
                     !field.FieldType.Namespace.StartsWith("System", StringComparison.Ordinal))
            {
                foreach (var value in ReadNestedSimpleMembers(child, depth - 1, visited)) yield return value;
            }
        }
    }

    private static IEnumerable<object?> ReadSimpleMembers(object obj)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in obj.GetType().GetFields(flags))
        {
            if (!IsSimple(field.FieldType)) continue;
            object? value = null; try { value = field.GetValue(obj); } catch { }
            yield return value?.ToString();
        }
        foreach (var property in obj.GetType().GetProperties(flags).Where(x => x.GetIndexParameters().Length == 0 && x.CanRead && IsSimple(x.PropertyType)))
        {
            object? value = null; try { value = property.GetValue(obj); } catch { }
            yield return value?.ToString();
        }
    }

    private static bool ObjectContainsExactString(object obj, string expected) =>
        !string.IsNullOrEmpty(expected) && ReadSimpleMembers(obj).OfType<string>().Any(x => x == expected);

    private static bool RelevantType(Type type) => ContainsAny(type.Name,
        "artifact", "item", "inventory", "weapon", "miracle", "metadata", "database", "selector", "shop");

    private static bool IsSimple(Type type) => type == typeof(string) || type.IsEnum || type.IsPrimitive;

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(x => value.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);

    private static int ReadNamedInt(GameObject gameObject, params string[] names)
    {
        foreach (var component in gameObject.GetComponentsInParent<Component>(true).Take(12))
        {
            var value = ReadNamedNullableInt(component, names);
            if (value.HasValue) return Math.Max(0, value.Value);
        }
        return 0;
    }

    private static int? ReadNamedNullableInt(object obj, params string[] names)
    {
        var member = FindMember(obj.GetType(), names);
        try
        {
            var value = member switch { FieldInfo f => f.GetValue(obj), PropertyInfo p => p.GetValue(obj), _ => null };
            return value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return null; }
    }

    private static string? ReadNamedString(object obj, params string[] names)
    {
        var member = FindMember(obj.GetType(), names);
        try { return member switch { FieldInfo f => f.GetValue(obj)?.ToString(), PropertyInfo p => p.GetValue(obj)?.ToString(), _ => null }; }
        catch { return null; }
    }

    private static object? ReadNamedObject(object obj, params string[] names)
    {
        var member = FindMember(obj.GetType(), names);
        try { return member switch { FieldInfo f => f.GetValue(obj), PropertyInfo p => p.GetValue(obj), _ => null }; }
        catch { return null; }
    }

    private string? ReadParentKey(object obj)
    {
        var value = ReadNamedString(obj, "enhanceFromId", "parent", "parentWeapon", "previousWeapon");
        if (value is null) return null;
        return _catalog.Entries.FirstOrDefault(x => x.GameKey == value || x.Slug == value)?.GameKey;
    }

    private static string? ReadCategoryIds(object obj)
    {
        var member = FindMember(obj.GetType(), "categories");
        object? value;
        try { value = member switch { FieldInfo f => f.GetValue(obj), PropertyInfo p => p.GetValue(obj), _ => null }; }
        catch { return null; }
        if (value is not IEnumerable sequence) return null;
        var ids = new List<string>();
        foreach (var element in sequence)
        {
            if (element is null) continue;
            var id = ReadNamedString(element, "id");
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!);
        }
        return string.Join(",", ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
    }

    private static int? ComputeWeaponTier(object weapon, IReadOnlyList<UnityEngine.Object> objects)
    {
        var tier = 1;
        var current = weapon;
        var visited = new HashSet<int>();
        while (true)
        {
            var id = ReadNamedNullableInt(current, "id");
            if (id.HasValue && !visited.Add(id.Value)) return null;
            var parent = ReadNamedNullableInt(current, "enhanceFromId");
            if (!parent.HasValue || parent.Value <= 0) return tier;
            var next = objects.FirstOrDefault(x => x != null && x.GetType().Name == "WeaponEntity" && ReadNamedNullableInt(x, "id") == parent.Value);
            if (next is null) return null;
            tier++;
            current = next;
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static bool ReadBool(object obj, params string[] names)
    {
        var member = FindMember(obj.GetType(), names);
        try { return member switch { FieldInfo f => Convert.ToBoolean(f.GetValue(obj)), PropertyInfo p => Convert.ToBoolean(p.GetValue(obj)), _ => false }; }
        catch { return false; }
    }

    private static void InvokeButton(Component button)
    {
        var onClick = button.GetType().GetProperty("onClick", BindingFlags.Instance | BindingFlags.Public)?.GetValue(button);
        var invoke = onClick?.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (invoke is null) throw new MissingMethodException("UnityEngine.UI.Button.onClick.Invoke");
        invoke.Invoke(onClick, null);
    }

    private static MemberInfo? FindMember(Type type, params string[] names)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return type.GetMembers(flags).FirstOrDefault(x => names.Any(name => string.Equals(name, x.Name, StringComparison.OrdinalIgnoreCase)));
    }
}
