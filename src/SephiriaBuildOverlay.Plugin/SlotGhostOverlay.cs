using System.Collections;
using System.Reflection;
using System.Text;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Core.Solvers;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private BuildPlan? _placementPlan;
    private HashSet<string> _placementVerified = new(StringComparer.Ordinal);
    private readonly Dictionary<GridPoint, Component> _slotVisuals = new();
    private readonly Dictionary<string, Sprite> _itemSprites = new(StringComparer.Ordinal);
    private readonly Dictionary<GridPoint, int> _slotLevels = new();
    private readonly HashSet<GridPoint> _disabledSlots = new();
    private readonly Dictionary<string, Func<RunSnapshot, bool>> _actionOutcomes = new(StringComparer.Ordinal);
    private Func<RunSnapshot, bool>? _pendingActionOutcome;
    private readonly List<GhostItem> _boardItems = new();
    private string _boardSignature = "";
    private string _boardContext = "";
    private string _ghostResultSignature = "";
    private Task<BoardOptimizationResult>? _ghostTask;
    private string _ghostTaskSignature = "";
    private CancellationTokenSource? _ghostCancellation;
    private IReadOnlyList<ArtifactAssignment> _ghostAssignments = Array.Empty<ArtifactAssignment>();
    private bool _boardConditional;
    private bool _boardVisible;
    private object? _boardInventory;
    private (int Width, int Height, int Storage)? _rewardBoardDimensions;
    private Component? _boardPanel;
    internal int GhostCount => _ghostResultSignature == _boardSignature ? _ghostAssignments.Count : 0;
    private float _ghostPreviewUntil;
    private IReadOnlyList<ArtifactAssignment> _ghostPreview = Array.Empty<ArtifactAssignment>();
    public bool IsGhostPreview => Time.unscaledTime < _ghostPreviewUntil;
    public bool PreviewGhostRendering()
    {
        if (!_boardVisible || _lastSnapshot?.IsLocalPlayerOwned != true || _requestPending) return false;
        var item = _boardItems.FirstOrDefault(x => _itemSprites.ContainsKey(x.InstanceId));
        var empty = _slotVisuals.Keys.Where(x => !_boardItems.Any(i => i.Position.Equals(x))).OrderBy(x => x.Y).ThenBy(x => x.X).ToArray();
        if (item is null || empty.Length == 0) return false;
        _ghostPreview = new[] { new ArtifactAssignment(item.InstanceId, item.Position, empty[0], 0) };
        _ghostPreviewUntil = Time.unscaledTime + 10f;
        return true;
    }

    public void SetPlacementPlan(BuildPlan? plan, IReadOnlyDictionary<string, CatalogBinding>? bindings = null)
    {
        CancelGhostCalculation();
        _placementPlan = plan;
        _placementVerified = new HashSet<string>(bindings?.Where(x => x.Value.AllowsAutomaticAction).Select(x => x.Key) ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    private void CancelGhostCalculation(bool preserveContinuation = false)
    {
        var cancellation = _ghostCancellation;
        var task = _ghostTask;
        cancellation?.Cancel();
        if (task is not null)
            _ = task.ContinueWith(completed => { _ = completed.Exception; cancellation?.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else cancellation?.Dispose();
        _ghostCancellation = null;
        _ghostTask = null; _ghostAssignments = Array.Empty<ArtifactAssignment>();
        _ghostResultSignature = ""; _ghostTaskSignature = "";
        _optimizationInput = null; _optimizationResult = null; _optimizationAction = null; _optimizationStep = null; _optimizationModel = null;
        if (!preserveContinuation) _boardContinuation.Clear();
        ClearSynthesis();
    }

    private static Dictionary<GridPoint, int> ReadCellMap(object inventory, string name)
    {
        var result = new Dictionary<GridPoint, int>();
        if (ReadNamedObject(inventory, name) is not IEnumerable sequence) return result;
        foreach (var pair in sequence)
        {
            if (pair is null) continue;
            var position = ReadNamedObject(pair, "Key");
            var value = ReadNamedNullableInt(pair, "Value");
            var x = position is null ? null : ReadNamedNullableInt(position, "x");
            var y = position is null ? null : ReadNamedNullableInt(position, "y");
            if (x.HasValue && y.HasValue && value.HasValue) result[new GridPoint(x.Value, y.Value)] = value.Value;
        }
        return result;
    }

    private void CaptureBoard(ScreenKind screen, List<ScreenCandidate> candidates, string runId, string playerId, bool owned)
    {
        _enchantMode = false; _tabletMixMode = false; _enchantRanks.Clear(); _enchantArtifacts.Clear();
        _slotVisuals.Clear(); _itemSprites.Clear(); _slotLevels.Clear(); _disabledSlots.Clear(); _boardItems.Clear();
        _boardVisible = false; _pointerRotation = null; _boardSignature = "";
        _rewardBoardDimensions = null;
        _boardArtifacts.Clear(); _observedArtifactCategories.Clear(); _optimizationUnavailable = null; _optimizationAction = null; _optimizationStep = null;
        _boardContext = $"{runId}:{playerId}:{owned}";
        _boardInventory = LocalInventory();
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
        _boardPanel = registry?["UI_CharacterStatusPanel"] as Component;
        var readOnlyReward = screen is ScreenKind.ArtifactReward or ScreenKind.Shop && _rewardTabletSpecs.Count > 0 && _placementPlan is not null;
        if (_boardPanel == null || _boardInventory is null) { CancelGhostCalculation(); return; }
        _boardVisible = _boardPanel.gameObject.activeInHierarchy && ReadBool(_boardPanel, "IsOpened");
        if (!_boardVisible && !readOnlyReward) { CancelGhostCalculation(); return; }
        if (_boardVisible && (ReadNamedObject(_boardPanel, "PlayerAvatar") is not Component avatar || !ReferenceEquals(ReadNamedObject(avatar, "Inventory"), _boardInventory))) { CancelGhostCalculation(); return; }
        _enchantMode = owned && screen == ScreenKind.Inventory && ReadNamedObject(_boardPanel, "InventoryMode")?.ToString() == "Enchant";
        _tabletMixMode = owned && screen == ScreenKind.TabletBoard && ReadNamedString(_boardPanel, "InventoryMode") == "TabletMix";
        if (ReadNamedObject(_boardPanel, "itemIcons") is not IEnumerable icons || ReadNamedObject(_boardInventory, "inventoryMatrix") is not IEnumerable contents) return;
        var width = ReadNamedNullableInt(_boardInventory, "Width") ?? 0;
        var height = ReadNamedNullableInt(_boardInventory, "Height") ?? 0;
        var storage = ReadNamedNullableInt(_boardInventory, "CurrentInventoryStorage") ?? 0;
        if (width < 1 || height < 1 || width > 32 || height > 32 || storage < 1) return;
        if (readOnlyReward) _rewardBoardDimensions = (width, height, storage);
        var levels = ReadCellMap(_boardInventory, "levelMatrix");
        var disabled = ReadCellMap(_boardInventory, "disableMatrix");
        var slots = new List<GhostSlot>();
        foreach (var icon in icons.OfType<Component>())
        {
            if (icon == null || !ReferenceEquals(ReadNamedObject(icon, "Inventory"), _boardInventory)) continue;
            var x = ReadNamedNullableInt(icon, "X"); var y = ReadNamedNullableInt(icon, "Y");
            if (!x.HasValue || !y.HasValue || x < 0 || y < 0 || x >= width || y >= height || y * width + x >= storage) continue;
            var position = new GridPoint(x.Value, y.Value);
            _slotVisuals[position] = icon;
            levels.TryGetValue(position, out var level); disabled.TryGetValue(position, out var disable);
            _slotLevels[position] = level;
            if (disable > 0) _disabledSlots.Add(position);
            slots.Add(new GhostSlot(position, level, disable > 0));
        }
        // Closed inventory: read immutable board cells, never open UI or attach
        // action rectangles merely to score a tablet reward.
        if (readOnlyReward && !_boardVisible)
        {
            slots.Clear(); _slotLevels.Clear(); _disabledSlots.Clear();
            for (var i = 0; i < Math.Min(width * height, storage); i++)
            {
                var p = new GridPoint(i % width, i / width); levels.TryGetValue(p, out var level); disabled.TryGetValue(p, out var disable);
                _slotLevels[p] = level; if (disable > 0) _disabledSlots.Add(p); slots.Add(new GhostSlot(p, level, disable > 0));
            }
        }
        foreach (var pair in contents)
        {
            if (pair is null || ReadNamedObject(pair, "Value") is not object item) continue;
            var x = ReadNamedNullableInt(item, "XIdx"); var y = ReadNamedNullableInt(item, "YIdx");
            var id = ReadNamedNullableInt(item, "InstanceID"); var entity = ReadNamedNullableInt(item, "EntityID");
            if (!x.HasValue || !y.HasValue || !id.HasValue || !entity.HasValue || x < 0 || y < 0 || x >= width || y >= height) continue;
            var charm = ReadNamedObject(item, "Charm");
            var criteria = charm is null ? null : ReadNamedObject(charm, "criteria");
            var key = entity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var itemId = id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_enchantMode && charm is not null) CaptureEnchantArtifact(charm, itemId, key);
            _boardItems.Add(new GhostItem(itemId, key, new GridPoint(x.Value, y.Value), charm is null ? 0 : Math.Max(0, ReadNamedNullableInt(charm, "maxLevel") ?? 0),
                charm is not null && criteria is null && _placementVerified.Contains(key)));
            if (charm is not null) _boardArtifacts.Add(CaptureArtifact(item, charm, itemId, key, new GridPoint(x.Value, y.Value)));
            var itemEntity = ReadNamedObject(item, "Entity");
            if (itemEntity is not null && ReadNamedObject(itemEntity, "icon") is Sprite sprite && sprite != null) _itemSprites[itemId] = sprite;
        }
        var tablets = ReadNamedObject(_boardInventory, "CurrentStoneTablets") as IEnumerable;
        _boardConditional = tablets is null || tablets.Cast<object>().Any(x => !string.IsNullOrWhiteSpace(ReadNamedString(x, "conditionQuery")));
        var signature = new StringBuilder(_placementPlan?.SourceBuildId.ToString() ?? "inactive");
        signature.Append('|').Append(ReadNamedObject(_boardPanel, "InventoryMode")?.ToString());
        signature.Append('|').Append(_boardConditional);
        foreach (var slot in slots.OrderBy(x => x.Position.Y).ThenBy(x => x.Position.X))
            signature.Append('|').Append(slot.Position.X).Append(',').Append(slot.Position.Y).Append(':').Append(slot.Level).Append(':').Append(slot.Disabled);
        foreach (var item in _boardItems.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            signature.Append('|').Append(item.InstanceId).Append(':').Append(item.Key).Append(':').Append(item.Position.X).Append(',').Append(item.Position.Y).Append(':').Append(item.MaxLevel).Append(':').Append(item.CanRelocate);
        AppendTabletState(signature, tablets);
        _boardSignature = signature.ToString();
        if (_placementPlan is null || !owned) { CancelGhostCalculation(); return; }
        if (screen == ScreenKind.Inventory)
            foreach (var item in _boardItems.Where(x => _placementPlan.Artifacts.Any(g => g.CatalogKey == x.Key)))
                if (_slotVisuals.TryGetValue(item.Position, out var icon) && icon.transform is RectTransform rect)
                { var token = "board:item:" + item.InstanceId; candidates.Add(new ScreenCandidate(token, CandidateKind.Item, item.Key)); _rectangles[token] = rect; }
        // Enchant is a manual, consumptive native flow. Do not calculate or
        // retain Move/Rotate actions/ghosts while it is active.
        if (_enchantMode)
        {
            foreach (var rank in EnchantPriority.Rank(_enchantArtifacts, _placementPlan.Artifacts))
                _enchantRanks["board:item:" + rank.Artifact.Id] = rank;
            CancelGhostCalculation();
            return;
        }
        CapturePointerRotation(screen, candidates);
        try
        {
            var input = CaptureOptimizationInput(width, height, storage);
            if (_tabletMixMode)
            {
                if (_ghostTask is not null || _optimizationResult is not null) CancelGhostCalculation();
                CaptureSynthesis(input, candidates, registry); return;
            }
            ClearSynthesis();
            if (screen is ScreenKind.ArtifactReward or ScreenKind.Shop && !PlacementBatchEnabled)
            {
                // Reward comparison has its own baseline solve. A hidden 6000-
                // trial ghost solve only competes with it for CPU and can never
                // dispatch an inventory move on this screen.
                if (_ghostTask is not null || _optimizationResult is not null) CancelGhostCalculation();
                _optimizationInput = input;
                return;
            }
            CalculateOptimization(input);
        }
        catch (Exception ex) { CancelGhostCalculation(preserveContinuation: true); _optimizationUnavailable = ex.GetBaseException().Message; }
        if (!_boardVisible || _requestPending || screen != ScreenKind.Inventory && !(PlacementBatchEnabled && screen == ScreenKind.ArtifactReward) || _ghostResultSignature != _boardSignature) return;
        CaptureOptimizationAction(candidates);
    }

    private bool CanMoveIntoEmptySlot(GhostItem item, GridPoint destination)
    {
        if (_boardInventory is null || _boardPanel == null) return false;
        var find = _boardInventory.GetType().GetMethod("FindItem", new[] { typeof(sbyte), typeof(sbyte) });
        if (find is null) return false;
        try
        {
            var source = find.Invoke(_boardInventory, new object[] { checked((sbyte)item.Position.X), checked((sbyte)item.Position.Y) });
            var target = find.Invoke(_boardInventory, new object[] { checked((sbyte)destination.X), checked((sbyte)destination.Y) });
            // CanAddItemAtPositionForSwap checks an ADD across inventories and
            // rejects unique effects already owned at the source. Normal Swap
            // moves the existing whole instance; it does not acquire a copy.
            return SlotMoveGuard.CanMove(item, source is null ? null : ReadNamedString(source, "InstanceID"),
                source is null ? null : ReadNamedString(source, "EntityID"), target is null,
                ReadNamedObject(_boardPanel, "InventoryMode")?.ToString() == "None",
                _slotVisuals.ContainsKey(item.Position), _slotVisuals.ContainsKey(destination), _disabledSlots.Contains(destination));
        }
        catch { return false; }
    }

    public Recommendation RecommendPlacement(RunSnapshot snapshot)
    {
        if (_tabletMixMode) return new Recommendation(null, _synthesisStatus);
        if (_enchantMode) return new Recommendation(null, "인챈트 우선순위 표시 · 강화는 게임에서 수동 확인");
        if (_pointerRotation is { } rotation && snapshot.IsLocalPlayerOwned && !snapshot.ServerRequestPending)
            return new Recommendation(new RecommendedAction(ActionKind.Rotate, rotation.Token,
                "포인터 아래 석판을 네이티브 방향으로 한 번 회전 (사용자 지정·최적화 아님)", snapshot.Identity,
                expectedResult: $"회전 {rotation.Rotation * 90}° → {rotation.NextRotation * 90}° · 소비 없음"),
                "포인터 아래 석판 한 번 회전 (Shift)");
        if (_optimizationAction is { } optimized && snapshot.IsLocalPlayerOwned && !snapshot.ServerRequestPending)
            return new Recommendation(new RecommendedAction(optimized.Kind, optimized.TargetToken, optimized.Reason, snapshot.Identity,
                expectedResult: optimized.ExpectedResult), _optimizationResult?.ProvenOptimal == true ? "석판·아티팩트 통합 배치 (전체 탐색 완료)" : "석판·아티팩트 통합 배치 개선 (제한 탐색)");
        return new Recommendation(null, _optimizationUnavailable ?? _optimizationResult?.Unavailable ??
            (_ghostTask is not null ? "석판·아티팩트 배치를 계산 중입니다." : "현재 탐색에서 더 나은 배치를 찾지 못했습니다."));
    }

    public void DrawPlacementGhosts(float opacity, float scale)
    {
        if (Event.current.type != EventType.Repaint || _enchantMode || _tabletMixMode || !_boardVisible || _lastSnapshot?.IsLocalPlayerOwned != true || _boardPanel == null || !IsGhostPreview && _ghostResultSignature != _boardSignature) return;
        var zone = ReadNamedObject(_boardPanel, "inventoryZone") as RectTransform;
        if (zone == null) return;
        var clip = ScreenRect(zone);
        var previous = GUI.color;
        try
        {
            foreach (var assignment in IsGhostPreview ? _ghostPreview : _ghostAssignments)
            {
                if (!_slotVisuals.TryGetValue(assignment.To, out var icon) || icon == null || !icon.gameObject.activeInHierarchy) continue;
                var rect = ScreenRect((RectTransform)icon.transform);
                if (!clip.Contains(rect.center)) continue;
                var inner = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8);
                _nativeLayer?.Box("ghost-bg:" + assignment.InstanceId, inner, new Color(.25f, .9f, .75f, opacity * .25f));
                if (_itemSprites.TryGetValue(assignment.InstanceId, out var sprite) && sprite != null)
                    _nativeLayer?.Box("ghost-icon:" + assignment.InstanceId, inner, new Color(1f, 1f, 1f, opacity), sprite);
                _nativeLayer?.Border("ghost-border:" + assignment.InstanceId, rect, new Color(.3f, 1f, .75f, opacity + .15f));
                _itemLabel ??= new GUIStyle(GUI.skin.label) { font = _itemFont, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _itemLabel.fontSize = Mathf.RoundToInt(11 * scale); _itemLabel.normal.textColor = new Color(.55f, 1f, .8f);
                GUI.color = Color.white;
                var tablet = _optimizationInput?.Tablets.FirstOrDefault(x => x.Id == assignment.InstanceId);
                var detail = tablet is not null && _optimizationResult is not null ? $"{_optimizationResult.Layout.Rotations[tablet.Id] * 90}°" : "";
                _nativeLayer?.Label("ghost-label:" + assignment.InstanceId, new Rect(rect.x, rect.yMax - 25 * scale, rect.width, 25 * scale), IsGhostPreview ? "표시 검증\n비실행" : detail, new Color(.55f, 1f, .8f), Mathf.Round(_nativeFontPixels * scale));
            }
        }
        finally { GUI.color = previous; }
    }
}
