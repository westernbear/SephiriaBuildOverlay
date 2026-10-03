using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway : IGameActionGateway, IDisposable
{
    private readonly VersionedCatalog _catalog;
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, Action> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RectTransform> _rectangles = new(StringComparer.Ordinal);
    private RunSnapshot? _lastSnapshot;
    private string? _highlightToken;
    private TaskCompletionSource<bool>? _confirmation;
    private ActionOutcomeObserver? _outcomeObserver;
    private bool _requestPending;
    private float _nextConfirmationPoll;
    private readonly bool _exportRuntimeCatalog;
    private float _nextBridgeWarning;
    private bool _disposed;
    internal readonly RuntimePerformance Performance = new();
    private static readonly RuntimeMemberCache Members = new();
    private readonly Dictionary<string, Type?> _gameTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<CatalogKind, HashSet<string>> _entityKeys = new();
    private readonly List<(MonoBehaviour Component, ScreenKind Screen)> _panels = new();
    private GameObject? _componentOwner;
    private Component[] _playerComponents = Array.Empty<Component>();
    private Component? _fallbackAvatar;
    private float _nextDiscoveryAt;
    private float _nextComponentsAt;
    private readonly Vector3[] _highlightCorners = new Vector3[4];
    internal int DiscoveryPasses { get; private set; }
    internal int ReflectionTypes => Members.TypesInspected;

    public UnityGameGateway(VersionedCatalog catalog, ManualLogSource log, bool exportRuntimeCatalog = false)
    {
        _catalog = catalog;
        _log = log;
        _exportRuntimeCatalog = exportRuntimeCatalog;
        foreach (CatalogKind kind in Enum.GetValues(typeof(CatalogKind)))
            _entityKeys[kind] = new HashSet<string>(catalog.Entries.Where(x => x.Kind == kind && x.GameKey is not null).Select(x => x.GameKey!), StringComparer.Ordinal);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    public RunSnapshot CaptureOnMainThread(bool freshDiscovery = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UnityGameGateway));
        var started = RuntimePerformance.Start();
        if (freshDiscovery || Time.unscaledTime >= _nextDiscoveryAt) DiscoverSceneReferences();
        var playerId = FindLocalPlayerId(out var owned, out var localPlayer);
        PreparePlayerComponents(localPlayer, freshDiscovery);
        var screen = FindActiveScreen(out var panel);
        _actions.Clear(); _rectangles.Clear(); _actionOutcomes.Clear(); _worldCandidateVisuals.Clear();
        _rewardTabletSpecs.Clear();
        var candidates = new List<ScreenCandidate>();
        if (panel is not null)
        {
            try { CapturePanelCandidates(panel, screen, localPlayer, candidates); }
            catch (Exception ex)
            {
                // Schema changes must disable this snapshot's actions, not break the game loop.
                candidates.Clear(); _actions.Clear(); _rectangles.Clear();
                if (Time.unscaledTime >= _nextBridgeWarning)
                {
                    _nextBridgeWarning = Time.unscaledTime + 30;
                    _log.LogWarning("Screen bridge unavailable; actions disabled: " + ex.Message);
                }
            }
        }

        if (panel is null && localPlayer is not null)
            screen = CaptureWorldCandidates(localPlayer, candidates);

        var runId = FindRunId(playerId);
        var inventory = ReadInventory(localPlayer);
        try { CaptureBoard(screen, candidates, runId, playerId, owned); }
        catch (Exception ex)
        {
            _boardVisible = false; CancelGhostCalculation(preserveContinuation: true);
            if (Time.unscaledTime >= _nextBridgeWarning) { _nextBridgeWarning = Time.unscaledTime + 30; _log.LogWarning("Board preview unavailable: " + ex.Message); }
        }
        var money = ReadPlayerInt(localPlayer, "Money", "currentMoney", "money");
        var dice = ReadPlayerInt(localPlayer, "rerollDice", "diceCount", "currentDice");
        var currentWeapon = ReadCurrentWeapon(localPlayer);
        var miracleKeys = GetLocalMiracleKeys(localPlayer);
        var currentMiracle = miracleKeys.FirstOrDefault();
        try { CaptureTabletRewards(screen, candidates); }
        catch (Exception ex)
        {
            ClearTabletRewards();
            if (Time.unscaledTime >= _nextBridgeWarning) { _nextBridgeWarning = Time.unscaledTime + 30; _log.LogWarning("Tablet reward bridge unavailable: " + ex.GetBaseException().Message); }
        }
        var revision = StableRevision(runId, playerId, screen, candidates, inventory, currentWeapon, string.Join(",", miracleKeys.OrderBy(x => x, StringComparer.Ordinal)), money, dice, _boardSignature);
        _lastSnapshot = new RunSnapshot(runId, playerId, revision, screen, candidates, inventory,
            currentWeapon, currentMiracle, money, dice, owned, _requestPending, miracleKeys);
        Performance.SnapshotCompleted(started);
        return _lastSnapshot;
    }

    public RunSnapshot? Tick()
    {
        if (_disposed) return null;
        if (_confirmation is null || Time.unscaledTime < _nextConfirmationPoll) return null;
        _nextConfirmationPoll = Time.unscaledTime + .1f;
        var snapshot = CaptureOnMainThread();
        var outcome = _outcomeObserver?.Observe(snapshot) ?? ObservedActionOutcome.Pending;
        if (outcome == ObservedActionOutcome.Pending && _pendingActionOutcome?.Invoke(snapshot) == true) outcome = ObservedActionOutcome.Succeeded;
        if (_confirmation.Task.IsCompleted || outcome != ObservedActionOutcome.Pending)
        {
            _requestPending = false;
            if (outcome != ObservedActionOutcome.Succeeded) CancelGhostCalculation();
            _confirmation.TrySetResult(outcome == ObservedActionOutcome.Succeeded);
            _confirmation = null;
            _outcomeObserver = null;
            _pendingActionOutcome = null;
        }
        return snapshot;
    }

    // Confirmation deliberately bypasses polling caches for discovery. Mutable
    // ownership, candidates and costs are read afresh even when the UI is hidden.
    public Task<RunSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CaptureOnMainThread(freshDiscovery: true));
    }

    public Task<GameActionReceipt> SendThroughNormalRequestPathAsync(RecommendedAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) throw new ObjectDisposedException(nameof(UnityGameGateway));
        if (!_actions.TryGetValue(action.TargetToken, out var invoke))
            return Task.FromResult(new GameActionReceipt(Guid.NewGuid().ToString("N"), false, "게임 대상이 사라졌습니다."));
        if (_lastSnapshot is null || _requestPending)
            return Task.FromResult(new GameActionReceipt(Guid.NewGuid().ToString("N"), false, "요청 상태를 확인할 수 없습니다."));
        _outcomeObserver = new ActionOutcomeObserver(_lastSnapshot, action);
        _actionOutcomes.TryGetValue(action.TargetToken, out _pendingActionOutcome);
        _confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requestPending = true;
        try
        {
            // Button.onClick is the game's normal UI/request route; no inventory or network state is mutated directly.
            invoke();
            _log.LogInfo($"Confirmed request sent: kind={action.Kind}, target={action.TargetToken}, money={action.MoneyCost}, dice={action.DiceCost}");
            return Task.FromResult(new GameActionReceipt(Guid.NewGuid().ToString("N"), true));
        }
        catch (Exception ex)
        {
            _requestPending = false;
            CancelGhostCalculation();
            _confirmation = null;
            _outcomeObserver = null;
            _log.LogWarning(ex);
            return Task.FromResult(new GameActionReceipt(Guid.NewGuid().ToString("N"), false, ex.Message));
        }
    }

    public async Task<bool> WaitForServerConfirmationAsync(GameActionReceipt receipt, CancellationToken cancellationToken)
    {
        var completion = _confirmation ?? throw new InvalidOperationException("요청 대기 상태가 없습니다.");
        // Cancellation only completes this request; Unity state is cleaned up in Tick.
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled());
        return await completion.Task;
    }

    public void RecordReward(string catalogKey) => _outcomeObserver?.RecordReward(catalogKey);

    public IReadOnlyList<GameEntityDescriptor> DiscoverCatalogEntities()
    {
        var found = new List<GameEntityDescriptor>();
        var objects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
            .Where(x => x != null && RelevantType(x.GetType())).ToArray();
        var weaponParents = ReadWeaponParents(objects);
        // Read each entity once. Do not filter by the expected ID: doing so conceals ID mismatches.
        foreach (var obj in objects)
        {
            CatalogKind? kind = obj.GetType().Name switch
            {
                "ItemEntity" => CatalogKind.Artifact,
                "WeaponEntity" => CatalogKind.Weapon,
                "CostumeEntity" => CatalogKind.Costume,
                _ => IsMiracleEntity(obj.GetType()) ? CatalogKind.Miracle : null
            };
            if (kind is null) continue;
            var key = ReadNamedString(obj, "id");
            var name = ReadNamedString(obj, kind == CatalogKind.Costume ? "aName" : "Name");
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) continue;
            string? parent = null;
            if (kind == CatalogKind.Weapon) weaponParents.TryGetValue(key!, out parent);
            found.Add(new GameEntityDescriptor(key!, kind.Value, name!,
                kind == CatalogKind.Artifact ? ReadNamedString(obj, "rarity") : null,
                kind == CatalogKind.Artifact ? ReadCategoryIds(obj) : null,
                kind == CatalogKind.Weapon ? ComputeWeaponTier(key!, weaponParents) : null,
                parent, kind == CatalogKind.Artifact ? ReadBool(obj, "isDual") : (bool?)null));
        }
        var unique = found.GroupBy(x => $"{x.Kind}|{x.GameKey}|{x.KoreanName}|{x.Rarity}|{x.Category}|{x.Tier}|{x.ParentGameKey}|{x.IsDual}", StringComparer.Ordinal)
            .Select(x => x.First()).ToArray();
        _log.LogInfo($"Catalog snapshot: {unique.Length} entity descriptors");
        if (_exportRuntimeCatalog)
        {
            try
            {
                foreach (var item in objects.Where(x => x.GetType().Name == "ItemEntity" &&
                    _catalog.Entries.Any(entry => entry.Kind == CatalogKind.Artifact && entry.Rarity == "Eternal" && entry.KoreanName == ReadNamedString(x, "Name"))))
                    _log.LogInfo($"Artifact diagnostic: id={ReadNamedString(item, "id")}, name={ReadNamedString(item, "Name")}, rarity={ReadNamedString(item, "rarity")}, isDual={ReadBool(item, "isDual")}, behaviour={ReadNamedString(item, "itemBehaviour")}");
                var diagnosticPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SephiriaBuildOverlay", "catalog-observed-" + Application.version + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
                File.WriteAllText(diagnosticPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { gameVersion = Application.version, entries = unique }, Newtonsoft.Json.Formatting.Indented));
                _log.LogInfo("Observed catalog: " + diagnosticPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.LogWarning("Catalog export failed: " + ex.Message); }
        }
        return unique;
    }

    private static bool IsMiracleEntity(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.Name == "Miracle") return true;
        return false;
    }

    private static Dictionary<string, string?> ReadWeaponParents(IEnumerable<UnityEngine.Object> objects)
    {
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var weapon in objects.Where(x => x.GetType().Name == "WeaponEntity"))
        {
            var parent = ReadNamedString(weapon, "id");
            if (parent is null || ReadNamedObject(weapon, "standardEnhancements") is not IEnumerable enhancements) continue;
            foreach (var enhancement in enhancements)
            {
                if (enhancement is null || !ReadBool(enhancement, "enabled")) continue;
                var child = ReadNamedObject(enhancement, "enhanced");
                var childId = child is null ? null : ReadNamedString(child, "id");
                if (childId is null) continue;
                if (!edges.TryGetValue(childId, out var parents)) edges[childId] = parents = new HashSet<string>(StringComparer.Ordinal);
                parents.Add(parent);
            }
        }
        // A null entry means ambiguous parents, not a root. Tier validation must reject it.
        return edges.ToDictionary(x => x.Key, x => x.Value.Count == 1 ? x.Value.Single() : null, StringComparer.Ordinal);
    }

    private static int? ComputeWeaponTier(string key, IReadOnlyDictionary<string, string?> parents)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var tier = 1;
        while (parents.TryGetValue(key, out var parent))
        {
            if (parent is null || !visited.Add(key)) return null;
            key = parent;
            tier++;
        }
        return tier;
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
        PreparePlayerComponents(player);
        return _playerComponents.Any(component =>
        {
            if (component == null || component.GetType().Name is not ("UnitAvatar" or "PlayerAvatar")) return false;
            var linked = ReadNamedObject(component, "Inventory");
            return ReferenceEquals(linked, inventory);
        });
    }

    public void DrawHighlight()
    {
        if (_highlightToken is null || !_rectangles.TryGetValue(_highlightToken, out var rect) || rect == null) return;
        var corners = _highlightCorners; rect.GetWorldCorners(corners);
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

    public void Dispose() => Dispose(destroyUnityObjects: true);
    public void Dispose(bool destroyUnityObjects)
    {
        if (_disposed) return;
        _disposed = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        _confirmation?.TrySetCanceled();
        _confirmation = null;
        _requestPending = false;
        _actions.Clear(); _rectangles.Clear(); _panels.Clear();
        ClearTabletRewards(); _worldCandidateVisuals.Clear();
        CancelGhostCalculation(); _slotVisuals.Clear(); _itemSprites.Clear();
        _nativeLayer?.Dispose(destroyUnityObjects); _nativeLayer = null; _nativeFont = null;
        _notificationLayer?.Dispose(destroyUnityObjects); _notificationLayer = null; _notificationFont = null;
        _modalCursorLayer?.Dispose(destroyUnityObjects); _modalCursorLayer = null;
        _playerComponents = Array.Empty<Component>(); _componentOwner = null; _fallbackAvatar = null;
    }

    private ScreenKind FindActiveScreen(out GameObject? panel)
    {
        foreach (var hit in _panels)
        {
            var component = hit.Component;
            if (component == null) continue;
            bool? opened = FindMember(component.GetType(), "IsOpened") is null ? null : ReadBool(component, "IsOpened");
            bool? showing = FindMember(component.GetType(), "Showing") is null ? null : ReadBool(component, "Showing");
            if (!NativePanelVisibility.IsVisible(component.gameObject.activeInHierarchy, component.enabled, opened, showing)) continue;
            panel = component.gameObject;
            return hit.Screen;
        }
        panel = null;
        return ScreenKind.None;
    }

    private ScreenKind CaptureWorldCandidates(GameObject localPlayer, List<ScreenCandidate> candidates)
    {
        // Use the game's own nearby-interaction registry. Distance alone would
        // allow interacting through walls or with props outside the native range.
        var checker = _playerComponents.FirstOrDefault(x => x != null && x.GetType().Name == "InteractableChecker");
        if (checker is null)
        {
            var input = ReadStatic("PlayerInputController", "Instance");
            if (input is null || ReadNamedObject(input, "avatar") is not Component avatar || !_playerComponents.Contains(avatar)) return ScreenKind.None;
            checker = input is null ? null : ReadNamedObject(input, "interactableChecker") as Component;
        }
        var registered = checker is null ? null : ReadNamedObject(checker, "interactables") as IEnumerable;
        if (registered is null || !ReadBool(checker!, "canDoInteractive")) return ScreenKind.None;
        if (CaptureRiftShop(registered, localPlayer, candidates)) return ScreenKind.Shop;
        var nearby = registered.Cast<object>().OfType<MonoBehaviour>()
            .Where(x => x != null && x.enabled && x.gameObject.activeInHierarchy &&
                        x.GetType().Name is "MiracleOrb" or "WeaponSpawner")
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
            var entity = ReadNamedObject(component, nearestType == "MiracleOrb" ? "Connected" : "spawnedWeapon");
            var key = entity is null ? null : EntityKey(entity, expected);
            if (key is null) continue;
            var interactive = component.GetType().GetMethod("Interactive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(GameObject) }, null);
            if (interactive is null) continue;
            var selectable = false;
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
            candidates.Add(new ScreenCandidate(token, kind, key, isSelectable: selectable));
            _actions[token] = () => interactive.Invoke(component, new object[] { localPlayer });
        }
        return screen;
    }

    private List<InventoryArtifact> ReadInventory(GameObject? localPlayer)
    {
        var result = new List<InventoryArtifact>();
        if (localPlayer is null) return result;
        var inventory = _playerComponents.Where(x => x != null)
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
            var entity = ReadNamedObject(value, "Entity");
            if (!_entityKeys[CatalogKind.Artifact].Contains(key) && (entity is null || ReadNamedNullableInt(entity, "type") != 6)) continue;
            var xPosition = ReadNamedNullableInt(value, "XIdx", "x") ?? -1;
            var yPosition = ReadNamedNullableInt(value, "YIdx", "y") ?? -1;
            result.Add(new InventoryArtifact(instanceId.Value.ToString(), key, xPosition, yPosition));
        }
        return result.GroupBy(x => x.InstanceId).Select(x => x.First()).ToList();
    }

    private static CatalogKind? ExpectedKind(ScreenKind screen) => screen switch
    {
        ScreenKind.WeaponUpgrade => CatalogKind.Weapon,
        ScreenKind.MiracleChoice => CatalogKind.Miracle,
        ScreenKind.Shop or ScreenKind.ArtifactReward or ScreenKind.Inventory => CatalogKind.Artifact,
        _ => null
    };

    private Type? GameType(string name)
    {
        if (!_gameTypes.TryGetValue(name, out var type))
            _gameTypes[name] = type = HarmonyLib.AccessTools.TypeByName(name);
        return type;
    }

    private object? ReadStatic(string typeName, string name)
    {
        var type = GameType(typeName);
        if (type is null) return null;
        try
        {
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
        }
        catch { return null; }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InvalidateSceneReferences();
    private void OnSceneUnloaded(Scene scene) => InvalidateSceneReferences();
    private void OnActiveSceneChanged(Scene previous, Scene next) => InvalidateSceneReferences();
    private void InvalidateSceneReferences()
    {
        if (_disposed) return;
        _nextDiscoveryAt = 0; _nextComponentsAt = 0;
        _panels.Clear(); _fallbackAvatar = null;
        _actions.Clear(); _rectangles.Clear();
        ClearTabletRewards(); _worldCandidateVisuals.Clear();
        CancelGhostCalculation(); _boardVisible = false;
    }

    private void DiscoverSceneReferences()
    {
        DiscoveryPasses++;
        _nextDiscoveryAt = Time.unscaledTime + 2f;
        _panels.Clear();
        var map = new (string Type, ScreenKind Screen)[]
        {
            ("UI_MiraclePanel", ScreenKind.MiracleChoice),
            ("UI_WeaponEnhancementPanel", ScreenKind.WeaponUpgrade),
            ("UI_SephiriteRewardPanel", ScreenKind.ArtifactReward),
            ("UI_ShopPanel", ScreenKind.Shop),
            ("UI_TabletMixPanel", ScreenKind.TabletBoard),
            ("UI_InventoryViewer", ScreenKind.Inventory),
            ("UI_CharacterStatusPanel", ScreenKind.Inventory)
        };
        // The game already maintains its instantiated UI registry. Reading that
        // bounded table avoids even typed Resources scans (which still cause
        // periodic ~100ms spikes on this Unity version).
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
        var allPanels = manager is null ? null : ReadNamedObject(manager, "allUIBaseObjects") as IEnumerable;
        foreach (var hit in map)
        {
            if (registry?[hit.Type] is MonoBehaviour component && component != null)
                _panels.Add((component, hit.Screen));
            else if (allPanels is not null)
                foreach (var knownPanel in allPanels.OfType<MonoBehaviour>())
                    if (knownPanel != null && knownPanel.GetType().Name == hit.Type) _panels.Add((knownPanel, hit.Screen));
        }
        _fallbackAvatar = null;
    }

    private static Component? FindIdentity(Component avatar) => avatar.GetComponentsInParent<Component>(true)
        .FirstOrDefault(x => x != null && x.GetType().Name == "NetworkIdentity");

    private static bool Owns(Component identity) => ReadBool(identity, "isLocalPlayer") || ReadBool(identity, "isOwned") || ReadBool(identity, "hasAuthority");

    private string FindLocalPlayerId(out bool owned, out GameObject? playerObject)
    {
        var input = ReadStatic("PlayerInputController", "Instance");
        var avatar = input is null ? null : ReadNamedObject(input, "avatar") as Component;
        if (avatar == null || !IsType(avatar.GetType(), "PlayerAvatar")) avatar = _fallbackAvatar;
        playerObject = null; owned = false;
        if (avatar == null || !avatar.gameObject.activeInHierarchy) return "single-player";
        var identity = FindIdentity(avatar);
        // Cached references are never cached authorization. Recheck ownership on
        // every capture and every confirmed action, including after reconnects.
        if (identity != null)
        {
            if (!Owns(identity)) return "unresolved-network-player";
            playerObject = identity.gameObject; owned = true;
            return identity.GetInstanceID().ToString();
        }
        if (Convert.ToBoolean(ReadStatic("Mirror.NetworkClient", "active") ?? false)) return "unresolved-network-player";
        playerObject = avatar.gameObject; owned = true;
        return "local-" + avatar.GetInstanceID();
    }

    private void PreparePlayerComponents(GameObject? player, bool force = false)
    {
        if (player == null)
        {
            _componentOwner = null; _playerComponents = Array.Empty<Component>();
            return;
        }
        if (force || player != _componentOwner || Time.unscaledTime >= _nextComponentsAt)
        {
            _componentOwner = player;
            _playerComponents = player.GetComponentsInChildren<Component>(true);
            _nextComponentsAt = Time.unscaledTime + 2f;
        }
    }

    private string? ReadCurrentWeapon(GameObject? localPlayer)
    {
        if (localPlayer is null) return null;
        foreach (var controller in _playerComponents
                     .Where(x => x != null && x.GetType().Name is "NewWeaponController" or "WeaponController" or "WeaponControllerSimple"))
        {
            var current = ReadNamedObject(controller, "currentWeapon") ?? ReadNamedObject(controller, "CurrentWeapon") ?? ReadNamedObject(controller, "CurrentWeaponInHand");
            var directId = current is null ? null : ReadNamedNullableInt(current, "entityId");
            if (directId.HasValue) return directId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
        foreach (var controller in _playerComponents.Where(x => x != null && x.GetType().Name == "MiracleController"))
        {
            if (ReadNamedObject(controller, "miracles") is not IEnumerable miracles) continue;
            foreach (var metadata in miracles)
            {
                if (metadata is null) continue;
                var id = ReadNamedString(metadata, "id");
                if (!string.IsNullOrWhiteSpace(id) && _entityKeys[CatalogKind.Miracle].Contains(id!)) ids.Add(id!);
            }
        }
        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    private int ReadPlayerInt(GameObject? localPlayer, params string[] names)
    {
        if (localPlayer is null) return 0;
        foreach (var component in _playerComponents)
        {
            if (component == null) continue;
            var value = ReadNamedNullableInt(component, names);
            if (value.HasValue) return Math.Max(0, value.Value);
        }
        return 0;
    }

    private string FindRunId(string playerId)
    {
        // Unity instance IDs change on process restart. The game's saved Seed is
        // restored into DestinySeed by DungeonManager.LoadDungeon; pair it with
        // the selected save slot so saved acquisition counts can be recovered.
        var dungeon = ReadStatic("DungeonManager", "Instance");
        var slot = ReadStatic("SaveManager", "Binded")?.ToString();
        var seed = dungeon is null ? null : ReadNamedNullableInt(dungeon, "DestinySeed");
        if (seed.HasValue && !string.IsNullOrWhiteSpace(slot) && playerId != "single-player" && playerId != "unresolved-network-player")
            return "save-" + slot + "-seed-" + seed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // The local player object normally survives floor scene changes and is recreated for a new run.
        return "player-" + playerId;
    }

    private static long StableRevision(string runId, string playerId, ScreenKind screen, IReadOnlyList<ScreenCandidate> candidates,
        IReadOnlyList<InventoryArtifact> inventory, string? weapon, string? miracle, int money, int dice, string boardSignature)
    {
        unchecked
        {
            long hash = 1469598103934665603L;
            void Add(string? value) { foreach (var c in value ?? string.Empty) { hash ^= c; hash *= 1099511628211L; } }
            Add(runId); Add(playerId); Add(screen.ToString()); Add(weapon); Add(miracle); Add(money.ToString()); Add(dice.ToString());
            Add(boardSignature);
            foreach (var candidate in candidates.OrderBy(x => x.Token)) { Add(candidate.Token); Add(candidate.CatalogKey); Add(candidate.Kind.ToString()); Add(candidate.MoneyCost.ToString()); Add(candidate.DiceCost.ToString()); Add(candidate.IsSelectable.ToString()); Add(candidate.IsFreeReroll.ToString()); Add(candidate.AutomaticActionAllowed.ToString()); Add(candidate.AdditionalCostDescription); Add(candidate.Admission.ToString()); }
            foreach (var item in inventory.OrderBy(x => x.InstanceId)) { Add(item.InstanceId); Add(item.CatalogKey); Add(item.X.ToString()); Add(item.Y.ToString()); }
            return hash;
        }
    }

    private static bool RelevantType(Type type) => ContainsAny(type.Name,
        "artifact", "item", "inventory", "weapon", "miracle", "metadata", "database", "selector", "shop", "costume");

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(x => value.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);

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
            var id = element is string categoryId ? categoryId : ReadNamedString(element, "id");
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!.ToLowerInvariant());
        }
        return string.Join(",", ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
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
        => Members.Find(type, names);
}
