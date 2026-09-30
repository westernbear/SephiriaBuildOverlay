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
    private string _ghostResultSignature = "";
    private Task<IReadOnlyList<ArtifactAssignment>>? _ghostTask;
    private string _ghostTaskSignature = "";
    private CancellationTokenSource? _ghostCancellation;
    private IReadOnlyList<ArtifactAssignment> _ghostAssignments = Array.Empty<ArtifactAssignment>();
    private bool _boardConditional;
    private bool _boardVisible;
    private object? _boardInventory;
    private Component? _boardPanel;
    private string? _nextMoveToken;
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

    private void CancelGhostCalculation()
    {
        _ghostCancellation?.Cancel(); _ghostCancellation?.Dispose(); _ghostCancellation = null;
        _ghostTask = null; _ghostAssignments = Array.Empty<ArtifactAssignment>();
        _ghostResultSignature = ""; _ghostTaskSignature = "";
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

    private void CaptureBoard(ScreenKind screen, List<ScreenCandidate> candidates)
    {
        _slotVisuals.Clear(); _itemSprites.Clear(); _slotLevels.Clear(); _disabledSlots.Clear(); _boardItems.Clear();
        _boardVisible = false; _nextMoveToken = null; _pointerRotation = null; _boardSignature = "";
        _boardInventory = LocalInventory();
        var manager = ReadStatic("UIManager", "Instance");
        var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
        _boardPanel = registry?["UI_CharacterStatusPanel"] as Component;
        if (_boardPanel == null || !_boardPanel.gameObject.activeInHierarchy || !ReadBool(_boardPanel, "IsOpened") || _boardInventory is null) return;
        if (ReadNamedObject(_boardPanel, "PlayerAvatar") is not Component avatar || !ReferenceEquals(ReadNamedObject(avatar, "Inventory"), _boardInventory)) return;
        _boardVisible = true;
        if (ReadNamedObject(_boardPanel, "itemIcons") is not IEnumerable icons || ReadNamedObject(_boardInventory, "inventoryMatrix") is not IEnumerable contents) return;
        var width = ReadNamedNullableInt(_boardInventory, "Width") ?? 0;
        var height = ReadNamedNullableInt(_boardInventory, "Height") ?? 0;
        var storage = ReadNamedNullableInt(_boardInventory, "CurrentInventoryStorage") ?? 0;
        if (width < 1 || height < 1 || width > 32 || height > 32 || storage < 1) return;
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
            _boardItems.Add(new GhostItem(itemId, key, new GridPoint(x.Value, y.Value), charm is null ? 0 : Math.Max(0, ReadNamedNullableInt(charm, "maxLevel") ?? 0),
                charm is not null && criteria is null && _placementVerified.Contains(key)));
            var itemEntity = ReadNamedObject(item, "Entity");
            if (itemEntity is not null && ReadNamedObject(itemEntity, "icon") is Sprite sprite && sprite != null) _itemSprites[itemId] = sprite;
        }
        var tablets = ReadNamedObject(_boardInventory, "CurrentStoneTablets") as IEnumerable;
        _boardConditional = tablets is null || tablets.Cast<object>().Any(x => !string.IsNullOrWhiteSpace(ReadNamedString(x, "conditionQuery")));
        var signature = new StringBuilder(_placementPlan?.SourceBuildId.ToString() ?? "inactive");
        signature.Append('|').Append(_boardConditional);
        foreach (var slot in slots.OrderBy(x => x.Position.Y).ThenBy(x => x.Position.X))
            signature.Append('|').Append(slot.Position.X).Append(',').Append(slot.Position.Y).Append(':').Append(slot.Level).Append(':').Append(slot.Disabled);
        foreach (var item in _boardItems.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            signature.Append('|').Append(item.InstanceId).Append(':').Append(item.Key).Append(':').Append(item.Position.X).Append(',').Append(item.Position.Y).Append(':').Append(item.MaxLevel).Append(':').Append(item.CanRelocate);
        AppendTabletState(signature, tablets);
        _boardSignature = signature.ToString();
        if (_placementPlan is null) return;
        CapturePointerRotation(screen, candidates);
        if (_ghostTask is { IsCompleted: true })
        {
            if (_ghostTask.Status == TaskStatus.RanToCompletion && _ghostTaskSignature == _boardSignature)
            {
                _ghostAssignments = _ghostTask.Result; _ghostResultSignature = _ghostTaskSignature;
            }
            else if (_ghostTask.IsFaulted) _log.LogWarning("Placement preview failed: " + _ghostTask.Exception?.GetBaseException().Message);
            _ghostTask = null;
        }
        if (_ghostTaskSignature != _boardSignature)
        {
            CancelGhostCalculation();
            _ghostTaskSignature = _boardSignature;
            _ghostCancellation = new CancellationTokenSource();
            var token = _ghostCancellation.Token;
            var inputItems = _boardItems.ToArray(); var inputSlots = slots.ToArray();
            var goals = _placementPlan.Artifacts.Where(x => _placementVerified.Contains(x.CatalogKey)).ToArray();
            var conditional = _boardConditional;
            // No Unity object, native entity or request delegate enters this job.
            _ghostTask = Task.Run(() => new SlotGhostPlanner().Solve(inputItems, inputSlots, goals, conditional, token), token);
        }
        if (screen != ScreenKind.Inventory || _ghostResultSignature != _boardSignature) return;
        foreach (var assignment in _ghostAssignments)
        {
            var source = _boardItems.FirstOrDefault(x => x.InstanceId == assignment.InstanceId);
            if (source is null || !_slotVisuals.TryGetValue(assignment.To, out var visual) || visual == null) continue;
            // Move only into an empty slot. Cycles and occupied destinations
            // remain ghost/manual guidance until safe swap sequencing is added.
            if (_boardItems.Any(x => x.Position.Equals(assignment.To))) continue;
            var method = _boardInventory.GetType().GetMethod("Swap", BindingFlags.Instance | BindingFlags.Public);
            if (method is null || !CanMoveIntoEmptySlot(source, assignment.To)) continue;
            var token = "move:" + source.InstanceId + ":" + assignment.To.X + ":" + assignment.To.Y;
            candidates.Add(new ScreenCandidate(token, CandidateKind.Item, source.Key));
            var inventory = _boardInventory;
            _actions[token] = () => method.Invoke(inventory, new object[] { checked((sbyte)assignment.From.X), checked((sbyte)assignment.From.Y), checked((sbyte)assignment.To.X), checked((sbyte)assignment.To.Y) });
            _rectangles[token] = visual.transform as RectTransform ?? visual.GetComponent<RectTransform>();
            _actionOutcomes[token] = snapshot => snapshot.Inventory.Any(x => x.InstanceId == assignment.InstanceId && x.X == assignment.To.X && x.Y == assignment.To.Y);
            _nextMoveToken ??= token;
        }
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
        if (_pointerRotation is { } rotation && snapshot.IsLocalPlayerOwned && !snapshot.ServerRequestPending)
            return new Recommendation(new RecommendedAction(ActionKind.Rotate, rotation.Token,
                "포인터 아래 석판을 네이티브 방향으로 한 번 회전 (사용자 지정·최적화 아님)", snapshot.Identity,
                expectedResult: $"회전 {rotation.Rotation * 90}° → {rotation.NextRotation * 90}° · 소비 없음"),
                "포인터 아래 석판 한 번 회전");
        if (_nextMoveToken is null || !snapshot.IsLocalPlayerOwned || snapshot.ServerRequestPending || _ghostResultSignature != _boardSignature)
            return new Recommendation(null, "배치 고스트는 안내용입니다. 조건부 효과와 점유 칸은 수동으로 확인하세요.");
        return new Recommendation(new RecommendedAction(ActionKind.Move, _nextMoveToken, "현재 석판 기준 더 높은 레벨의 빈 슬롯으로 이동", snapshot.Identity), "고스트 슬롯으로 한 아이템 이동");
    }

    public void DrawPlacementGhosts(float opacity, float scale)
    {
        if (Event.current.type != EventType.Repaint || !_boardVisible || _boardPanel == null || !IsGhostPreview && _ghostResultSignature != _boardSignature) return;
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
                var manual = _boardItems.Any(x => x.Position.Equals(assignment.To)) ? " · 수동" : "";
                _nativeLayer?.Label("ghost-label:" + assignment.InstanceId, new Rect(rect.x, rect.yMax - 25 * scale, rect.width, 25 * scale), IsGhostPreview ? "표시 검증\n비실행" : "목표 배치" + manual, new Color(.55f, 1f, .8f), Mathf.Round(_nativeFontPixels * scale));
            }
        }
        finally { GUI.color = previous; }
    }
}
