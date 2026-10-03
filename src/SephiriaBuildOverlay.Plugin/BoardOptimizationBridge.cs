using System.Collections;
using System.Reflection;
using System.Text;
using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Core.Solvers;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private readonly List<BoardArtifact> _boardArtifacts = new();
    private readonly Dictionary<string, IReadOnlyList<BoardTabletOption>> _tabletOptionCache = new(StringComparer.Ordinal);
    private BoardOptimizationInput? _optimizationInput;
    private BoardOptimizationResult? _optimizationResult;
    private readonly BoardPlanContinuation _boardContinuation = new();
    private RecommendedAction? _optimizationAction;
    private BoardOptimizationStep? _optimizationStep;
    private string? _optimizationModel;
    private string? _optimizationUnavailable;
    private long _optionCaptureStarted;
    private string _optionBuildKey = "";
    private readonly List<BoardTabletOption> _optionBuild = new();
    private int _optionBuildIndex;

    private BoardArtifact CaptureArtifact(object item, object charm, string id, string key, GridPoint position)
    {
        var criteria = ReadNamedObject(charm, "criteria");
        var condition = criteria?.GetType().Name switch
        {
            null => ArtifactCondition.None,
            "CharmActivateCriteria_TopInInventory" => ArtifactCondition.Top,
            "CharmActivateCriteria_BottomInInventory" => ArtifactCondition.Bottom,
            "CharmActivateCriteria_SideEnd" => ArtifactCondition.Side,
            "CharmActivateCriteria_Outlined" => ArtifactCondition.Outline,
            "CharmActivateCriteria_Inside" => ArtifactCondition.Inside,
            "CharmActivateCriteria_BothSideCharm" => ArtifactCondition.BothCharms,
            "CharmActivateCriteria_BothSidesAreEmpty" => ArtifactCondition.BothEmpty,
            "CharmActivateCriteria_NeighborsAreFull" => ArtifactCondition.NeighborsFull,
            "CharmActivateCriteria_Near8MagicBook" => ArtifactCondition.NearMagic,
            "CharmActivateCriteria_FullHP" => ArtifactCondition.External,
            _ => ArtifactCondition.Unknown
        };
        var enabled = true;
        var conditionActive = true;
        if (condition == ArtifactCondition.External)
        {
            var avatar = ReadNamedObject(charm, "Avatar");
            conditionActive = avatar is not null && ReadNamedNullableInt(avatar, "hp") == ReadNamedNullableInt(avatar, "MaxHp");
        }
        if (ReadBool(charm, "isWeaponRelatedCharm"))
        {
            var controller = ReadNamedObject(charm, "WeaponController");
            var weapon = controller is null ? null : ReadNamedObject(controller, "currentWeapon");
            enabled &= weapon is not null && ReadNamedString(weapon, "weaponType") == ReadNamedString(charm, "relatedWeapon");
        }
        var manager = ReadStatic("DungeonManager", "Instance");
        var enchantMethod = manager?.GetType().GetMethod("GetGlobalItemStatValue", new[] { typeof(int), typeof(string) });
        var enchant = 0;
        if (manager is not null && enchantMethod is not null && int.TryParse(id, out var instance))
            int.TryParse(enchantMethod.Invoke(manager, new object[] { instance, "Enchant" })?.ToString(), out enchant);
        var magicType = GameType("Charm_Magic");
        var placementEffect = CapturePlacementEffect(item, charm, id);
        return new BoardArtifact(id, key, position, Math.Max(0, ReadNamedNullableInt(charm, "maxLevel") ?? 0), enchant,
            _placementVerified.Contains(key) && _placementPlan?.Artifacts.Any(x => x.CatalogKey == key && x.Role is Core.Models.TargetRole.Required or Core.Models.TargetRole.Recommended) == true &&
            id != "0" && condition != ArtifactCondition.Unknown, condition, enabled, magicType?.IsInstanceOfType(charm) == true, conditionActive, placementEffect);
    }

    private IReadOnlyList<BoardTabletOption> NativeTabletOptions(object tablet, int width, int height, int storage, GridPoint current, int rotation, bool fixedPlacement, int? rewardInstance = null)
    {
        var id = rewardInstance ?? ReadNamedNullableInt(tablet, "instanceID") ?? throw new InvalidOperationException("석판 ID 누락");
        var query = tablet.GetType().GetMethod("GetQuery")?.Invoke(tablet, new object[] { id }) as string;
        var condition = tablet.GetType().GetMethod("GetConditionQuery")?.Invoke(tablet, new object[] { id }) as string;
        if (query is null || condition is null) throw new InvalidOperationException("석판 쿼리 조회 불가");
        if (query.Length + condition.Length > 32768) throw new InvalidOperationException("너무 큰 석판 효과 쿼리");
        var canRotate = !fixedPlacement && (rewardInstance.HasValue
            ? GameType("DungeonManager")?.GetMethod("IsTabletRotatable", BindingFlags.Static | BindingFlags.Public, null,
                new[] { typeof(int), typeof(bool) }, null)?.Invoke(null, new object[] { id, ReadBool(tablet, "isRotatable") }) is true
            : NativeCanRotate(tablet));
        var key = $"{width}:{height}:{storage}:{canRotate}:{(fixedPlacement ? current.ToString() : "*")}:{(canRotate ? -1 : rotation)}|{query.Length}:{query}|{condition}";
        if (_tabletOptionCache.TryGetValue(key, out var cached)) return cached;
        if (_tabletOptionCache.Count >= 128) _tabletOptionCache.Clear();
        var parse = tablet.GetType().GetMethod("ParseQuery", BindingFlags.Static | BindingFlags.Public) ?? throw new InvalidOperationException("게임 쿼리 파서 누락");
        var positionType = GameType("ItemPosition") ?? throw new InvalidOperationException("좌표 형식 누락");
        var constructor = positionType.GetConstructor(new[] { typeof(int), typeof(int) }) ?? throw new InvalidOperationException("좌표 생성자 누락");
        IEnumerable Parse(string text, GridPoint point, int angle)
        {
            var args = new object?[] { text, width, height, storage, constructor.Invoke(new object[] { point.X, point.Y }), angle, null };
            return parse.Invoke(null, args) as IEnumerable ?? throw new InvalidOperationException("게임 파서 결과 누락");
        }
        if (_optionBuildKey != key) { _optionBuildKey = key; _optionBuild.Clear(); _optionBuildIndex = 0; }
        var points = fixedPlacement ? new[] { current } : Enumerable.Range(0, Math.Min(width * height, storage)).Select(i => new GridPoint(i % width, i / width)).ToArray();
        var angles = canRotate ? new[] { 0, 1, 2, 3 } : new[] { rotation };
        while (_optionBuildIndex < points.Length * angles.Length)
        {
            // Parse on the main thread in bounded slices. No 500-query burst
            // when the inventory first opens; completed maps remain cached.
            if ((System.Diagnostics.Stopwatch.GetTimestamp() - _optionCaptureStarted) * 1000d / System.Diagnostics.Stopwatch.Frequency >= 3)
                throw new InvalidOperationException("석판 효과 맵 준비 중");
            var point = points[_optionBuildIndex / angles.Length]; var angle = angles[_optionBuildIndex % angles.Length];
            var effects = new List<BoardEffect>(); var conditions = new List<BoardCondition>();
            foreach (var addition in Parse(query, point, angle).Cast<object>())
            {
                var p = ReadNativePosition(addition);
                var value = ReadNamedString(addition, "value") ?? throw new InvalidOperationException("효과 값 누락");
                if (int.TryParse(value, out var level)) effects.Add(new BoardEffect(p, BoardEffectKind.Add, level));
                else if (value == "X") effects.Add(new BoardEffect(p, BoardEffectKind.Disable));
                else if (value == "IGNORECRITERIA") effects.Add(new BoardEffect(p, BoardEffectKind.IgnoreCriteria));
                else if (value.StartsWith("MUL/", StringComparison.Ordinal) && int.TryParse(value.Substring(4), out var multiplier)) effects.Add(new BoardEffect(p, BoardEffectKind.Multiply, multiplier));
                else throw new InvalidOperationException("지원하지 않는 석판 효과: " + value);
            }
            foreach (var addition in Parse(condition, point, angle).Cast<object>())
            {
                var value = ReadNamedString(addition, "value");
                conditions.Add(new BoardCondition(ReadNativePosition(addition), value switch
                { "ITEM" => BoardConditionKind.AnyItem, "CHARM" => BoardConditionKind.Charm, "PLACED" => BoardConditionKind.Placed, _ => BoardConditionKind.None }));
            }
            _optionBuild.Add(new BoardTabletOption(point, angle, effects, conditions)); _optionBuildIndex++;
        }
        _tabletOptionCache[key] = _optionBuild.ToArray();
        _optionBuildKey = ""; _optionBuild.Clear(); _optionBuildIndex = 0;
        return _tabletOptionCache[key];
    }

    private static GridPoint ReadNativePosition(object range)
    {
        var p = ReadNamedObject(range, "position") ?? throw new InvalidOperationException("효과 좌표 누락");
        return new GridPoint(ReadNamedNullableInt(p, "x") ?? throw new InvalidOperationException("X 누락"), ReadNamedNullableInt(p, "y") ?? throw new InvalidOperationException("Y 누락"));
    }

    private BoardOptimizationInput CaptureOptimizationInput(int width, int height, int storage)
    {
        _optionCaptureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_slotVisuals.Count > 64) throw new InvalidOperationException("64칸을 초과한 보드는 수동 배치 필요");
        var inventory = _boardInventory!;
        var disable = ReadCellMap(inventory, "disableMatrix"); var ignore = ReadCellMap(inventory, "ignoreCriteriaMatrix");
        var multiplier = ReadCellMap(inventory, "multiplyLevelMatrix");
        var values = _slotLevels.ToDictionary(x => x.Key, x => new[] { multiplier.TryGetValue(x.Key, out var m) && m != 0 ? x.Value / m : x.Value,
            disable.TryGetValue(x.Key, out var d) ? d : 0, ignore.TryGetValue(x.Key, out var i) ? i : 0, multiplier.TryGetValue(x.Key, out var factor) ? factor : 0 });
        var tablets = new List<BoardTablet>();
        var normal = ReadNamedObject(inventory, "CurrentStoneTablets") as IEnumerable ?? throw new InvalidOperationException("석판 목록 누락");
        var engravings = ReadNamedObject(inventory, "engravings") as IEnumerable ?? throw new InvalidOperationException("각인 목록 누락");
        foreach (var pair in normal.Cast<object>().Select(x => (tablet: x, fixedPlacement: false)).Concat(engravings.Cast<object>().Select(x => (tablet: x, fixedPlacement: true))))
        {
            var tablet = pair.tablet;
            var id = ReadNamedString(tablet, "instanceID") ?? throw new InvalidOperationException("석판 ID 누락");
            var key = ReadNamedString(tablet, "entityID") ?? "";
            var position = new GridPoint(ReadNamedNullableInt(tablet, "xIdx") ?? -1, ReadNamedNullableInt(tablet, "yIdx") ?? -1);
            var rotation = ReadNamedNullableInt(tablet, "rotation") ?? -1;
            if (rotation is < 0 or > 3) throw new InvalidOperationException("석판 회전 값 오류");
            var options = NativeTabletOptions(tablet, width, height, storage, position, rotation, pair.fixedPlacement);
            var movable = !pair.fixedPlacement && _boardItems.Any(x => x.InstanceId == id) && _slotVisuals.ContainsKey(position);
            tablets.Add(new BoardTablet(id, key, position, rotation, movable, options));
            if (ReadNamedObject(tablet, "EffectRange") is not IEnumerable range) throw new InvalidOperationException("현재 석판 효과 누락");
            foreach (var effect in range.Cast<object>())
            {
                if (!values.TryGetValue(ReadNativePosition(effect), out var cell)) continue;
                var type = ReadNamedString(effect, "effectType");
                var index = type switch { "IncreaseConstLevel" => 0, "Disable" => 1, "IgnoreCriteria" => 2, "MultiplyConstLevel" => 3, "None" => -1, _ => throw new InvalidOperationException("효과 형식 변경") };
                if (index >= 0) cell[index] = checked(cell[index] - (index is 1 or 2 ? 1 : ReadNamedNullableInt(effect, "levelParam") ?? 0));
            }
        }
        foreach (var artifact in _boardArtifacts) if (values.TryGetValue(artifact.Position, out var cell)) cell[0] = checked(cell[0] - artifact.Enchant);
        var avatar = ReadNamedObject(inventory, "UnitAvatar");
        var stat = avatar?.GetType().GetMethod("GetCustomStatUnsafe", new[] { typeof(string) });
        if (stat?.Invoke(avatar, new object[] { "ARRANGEMENTBONUS" }) is int bonus && bonus > 0) _optimizationUnavailable = "특수 배열 보너스는 수동 확인 필요";
        var input = new BoardOptimizationInput(width, height, storage,
            values.Select(x => new BoardCell(x.Key, x.Value[0], x.Value[1], x.Value[2], x.Value[3])), _boardArtifacts, tablets,
            _boardItems.ToDictionary(x => x.InstanceId, x => x.Position), _placementPlan!.Artifacts.Where(x => _placementVerified.Contains(x.CatalogKey)),
            _optimizationUnavailable, ReadNamedNullableInt(inventory, "globalActiveValue") > 0);
        // The current prediction must exactly reproduce native matrices. A
        // schema/mod effect mismatch disables automatic optimization.
        var nativePrediction = JointBoardPlanner.Cells(input, JointBoardPlanner.Current(input));
        if (nativePrediction.Any(x => !_slotLevels.TryGetValue(x.Position, out var level) || x.Level != level ||
            x.Disabled != (disable.TryGetValue(x.Position, out var d) ? d : 0) || x.Ignore != (ignore.TryGetValue(x.Position, out var i) ? i : 0)))
            throw new InvalidOperationException("현재 보드 효과와 예측이 일치하지 않습니다");
        var combos = CaptureBoardCombos(input);
        return new BoardOptimizationInput(input.Width, input.Height, input.Storage, input.Cells, input.Artifacts, input.Tablets,
            input.Items, input.Goals, input.Unavailable, input.GloballyActive, combos);
    }

    private static string OptimizationInvariant(BoardOptimizationInput input)
    {
        var s = new StringBuilder($"{input.Width}:{input.Height}:{input.Storage}:{input.GloballyActive}:{input.Unavailable}");
        foreach (var cell in input.Cells.OrderBy(x => x.Position.Y).ThenBy(x => x.Position.X)) s.Append('|').Append(cell.Position).Append(':').Append(cell.Level).Append(':').Append(cell.Disabled).Append(':').Append(cell.Ignore).Append(':').Append(cell.Multiplier);
        foreach (var a in input.Artifacts.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            s.Append('|').Append(a.Id).Append(':').Append(a.Key).Append(':').Append(a.Maximum).Append(':').Append(a.Enchant).Append(':').Append(a.Condition).Append(':').Append(a.ExternalActive).Append(':').Append(a.ConditionActive).Append(':').Append(a.Movable).Append(':').Append(a.Magic);
            AppendPlacementInvariant(s, a);
        }
        foreach (var c in input.Combos.Offsets.OrderBy(x => x.Key, StringComparer.Ordinal)) s.Append("|combo:").Append(c.Key).Append(':').Append(c.Value);
        foreach (var c in input.Combos.Goals) s.Append("|comboGoal:").Append(c);
        foreach (var c in input.Combos.ProtectedCategories) s.Append("|comboProtect:").Append(c);
        foreach (var t in input.Tablets.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            s.Append('|').Append(t.Id).Append(':').Append(t.Key).Append(':').Append(t.Movable);
            // Cache-owned arrays are immutable; identity changes on native
            // query/permission/storage changes, not on expected moves.
            s.Append(':').Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t.Options));
        }
        foreach (var id in input.Items.Keys.OrderBy(x => x, StringComparer.Ordinal)) s.Append('|').Append(id);
        foreach (var g in input.Goals.OrderBy(x => x.CatalogKey, StringComparer.Ordinal)) s.Append('|').Append(g.CatalogKey).Append(':').Append(g.Role).Append(':').Append(g.Priority).Append(':').Append(g.DesiredAcquisitions);
        return s.ToString();
    }

    private void CalculateOptimization(BoardOptimizationInput input)
    {
        var invariant = _boardContext + "|" + OptimizationInvariant(input) + "|" + string.Join(";", _boardItems.OrderBy(x => x.InstanceId, StringComparer.Ordinal)
            .Select(x => $"{x.InstanceId}:{x.Key}:{x.MaxLevel}:{x.CanRelocate}"));
        _boardSignature += "|model:" + invariant;
        // The native request can update coordinates before its effect maps.
        // Do not replace a multi-step goal with a solve of this intermediate
        // board while awaiting the outcome of the one confirmed action.
        if (_requestPending) return;
        var current = JointBoardPlanner.Current(input);
        var resumed = _boardContinuation.Resume(invariant, current);
        if (resumed is not null)
        {
            _optimizationResult = resumed;
            _ghostResultSignature = _boardSignature; _ghostTaskSignature = _boardSignature;
        }
        else if (_ghostTaskSignature != _boardSignature)
        {
            CancelGhostCalculation(); _ghostTaskSignature = _boardSignature;
            _ghostCancellation = new CancellationTokenSource(); var token = _ghostCancellation.Token;
            _ghostTask = Task.Run(() => new JointBoardPlanner().Solve(input, token), token);
        }
        _optimizationInput = input;
        _optimizationModel = (_placementPlan?.SourceBuildId.ToString() ?? "inactive") + "|" + invariant;
        if (_ghostTask is { IsCompleted: true })
        {
            if (_ghostTask.Status == TaskStatus.RanToCompletion && _ghostTaskSignature == _boardSignature)
            {
                _optimizationResult = _ghostTask.Result; _ghostResultSignature = _ghostTaskSignature;
                _boardContinuation.Hold(invariant, current, _optimizationResult);
            }
            else if (_ghostTask.IsFaulted)
            {
                var message = "배치 계산 중단: " + _ghostTask.Exception?.GetBaseException().Message;
                _log.LogWarning(message);
                _optimizationResult = new BoardOptimizationResult(JointBoardPlanner.Current(input), default, default, 0, false, message);
                _ghostResultSignature = _ghostTaskSignature;
            }
            _ghostTask = null;
        }
        if (_ghostResultSignature != _boardSignature || _optimizationResult?.Improved != true) return;
        _ghostAssignments = _boardItems.Where(x => _optimizationResult.Layout.Positions.TryGetValue(x.InstanceId, out var p) &&
            (!p.Equals(x.Position) || input.Tablets.Any(t => t.Id == x.InstanceId && _optimizationResult.Layout.Rotations[t.Id] != t.Rotation)))
            .Select(x => new ArtifactAssignment(x.InstanceId, x.Position, _optimizationResult.Layout.Positions[x.InstanceId], x.Position.ManhattanDistance(_optimizationResult.Layout.Positions[x.InstanceId]))).ToArray();
    }

    private void CaptureOptimizationAction(List<ScreenCandidate> candidates)
    {
        if (_optimizationResult?.Improved != true || _optimizationInput is null || _boardInventory is null || _boardPanel == null) return;
        var zone = ReadNamedObject(_boardPanel, "inventoryZone") as RectTransform;
        if (zone == null) return;
        var clip = ScreenRect(zone);
        bool Visible(GridPoint p) => _slotVisuals.TryGetValue(p, out var icon) && icon.gameObject.activeInHierarchy &&
            clip.Contains(ScreenRect((RectTransform)icon.transform).center);
        var step = BoardOptimizationStep.Next(_optimizationInput, _optimizationResult.Layout, (from, to) => Visible(from) && Visible(to));
        if (step is null && BoardOptimizationStep.Next(_optimizationInput, _optimizationResult.Layout) is not null)
            _optimizationUnavailable = "다음 배치 슬롯이 화면 밖에 있습니다 · 인벤토리를 스크롤해 주세요";
        else if (step is null && _ghostAssignments.Count > 0)
            _optimizationUnavailable = "특수 효과를 유지하는 교환 순서가 없어 수동 배치가 필요합니다";
        if (step is null || !_slotVisuals.TryGetValue(step.From, out var sourceIcon) || !_slotVisuals.TryGetValue(step.To, out var targetIcon)) return;
        _optimizationStep = step;
        var item = _boardItems.FirstOrDefault(x => x.InstanceId == step.Id);
        if (item is null) return;
        if (step.Rotation.HasValue)
        {
            var target = new TabletRotationTarget(step.Id, item.Key, step.From, step.Rotation.Value);
            _nativeRotateRequest ??= _boardPanel.GetType().GetMethod("OnTabletRotate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { sourceIcon.GetType() }, null);
            if (_nativeRotateRequest is null || !RotationMatches(target, sourceIcon, true, requirePointer: false)) return;
            var token = "opt:" + target.Token;
            var panel = _boardPanel;
            candidates.Add(new ScreenCandidate(token, CandidateKind.Item, null));
            _actions[token] = () =>
            {
                if (!ReferenceEquals(panel, _boardPanel) || !RotationMatches(target, sourceIcon, true, requirePointer: false)) throw new InvalidOperationException("석판 상태/회전 권한 변경");
                _boardContinuation.Expect(step.Expected);
                _nativeRotateRequest.Invoke(panel, new object[] { sourceIcon });
            };
            _actionOutcomes[token] = _ => RotationMatches(target, sourceIcon, false);
            _rectangles[token] = (RectTransform)sourceIcon.transform;
            var goalAngle = _optimizationResult.Layout.Rotations[step.Id] * 90;
            _optimizationAction = new RecommendedAction(ActionKind.Rotate, token, "필수 활성화·레벨을 우선한 석판 회전", default,
                expectedResult: $"회전 {target.Rotation * 90}° → {target.NextRotation * 90}° · 목표 {goalAngle}° · 소비 없음");
            return;
        }
        var swap = _boardInventory.GetType().GetMethod("Swap", new[] { typeof(sbyte), typeof(sbyte), typeof(sbyte), typeof(sbyte) });
        if (swap is null || !OptimizationMoveMatches(step)) return;
        var inventory = _boardInventory;
        var moveToken = $"opt:move:{step.Id}:{step.From.X}:{step.From.Y}:{step.To.X}:{step.To.Y}:{step.SwappedId ?? "empty"}";
        candidates.Add(new ScreenCandidate(moveToken, CandidateKind.Item, null));
        _actions[moveToken] = () =>
        {
            if (!ReferenceEquals(inventory, _boardInventory) || !OptimizationMoveMatches(step)) throw new InvalidOperationException("이동 대상/점유 상태 변경");
            _boardContinuation.Expect(step.Expected);
            swap.Invoke(inventory, new object[] { checked((sbyte)step.From.X), checked((sbyte)step.From.Y), checked((sbyte)step.To.X), checked((sbyte)step.To.Y) });
        };
        _actionOutcomes[moveToken] = _ => _boardItems.Any(x => x.InstanceId == step.Id && x.Position.Equals(step.To)) &&
            (step.SwappedId is null ? !_boardItems.Any(x => x.Position.Equals(step.From)) : _boardItems.Any(x => x.InstanceId == step.SwappedId && x.Position.Equals(step.From)));
        _rectangles[moveToken] = (RectTransform)targetIcon.transform;
        _optimizationAction = new RecommendedAction(ActionKind.Move, moveToken, "석판·아티팩트 통합 배치 개선", default,
            expectedResult: $"{(step.SwappedId is null ? "이동" : "교환")} {step.From} → {step.To} · 소비 없음");
    }

    private bool OptimizationMoveMatches(BoardOptimizationStep step)
    {
        if (_boardInventory is null || _boardPanel == null || ReadNamedString(_boardPanel, "InventoryMode") != "None") return false;
        var find = _boardInventory.GetType().GetMethod("FindItem", new[] { typeof(sbyte), typeof(sbyte) });
        if (find is null) return false;
        object? At(GridPoint p) => find.Invoke(_boardInventory, new object[] { checked((sbyte)p.X), checked((sbyte)p.Y) });
        var source = At(step.From); var target = At(step.To);
        var sourceExpected = _boardItems.FirstOrDefault(x => x.InstanceId == step.Id);
        var targetExpected = _boardItems.FirstOrDefault(x => x.InstanceId == step.SwappedId);
        var zone = ReadNamedObject(_boardPanel, "inventoryZone") as RectTransform;
        if (zone == null) return false;
        var clip = ScreenRect(zone);
        return sourceExpected is not null && source is not null && ReadNamedString(source, "InstanceID") == step.Id && ReadNamedString(source, "EntityID") == sourceExpected.Key &&
            (step.SwappedId is null ? target is null : targetExpected is not null && target is not null && ReadNamedString(target, "InstanceID") == step.SwappedId && ReadNamedString(target, "EntityID") == targetExpected.Key) &&
            _slotVisuals.TryGetValue(step.From, out var from) && from.gameObject.activeInHierarchy && clip.Contains(ScreenRect((RectTransform)from.transform).center) &&
            _slotVisuals.TryGetValue(step.To, out var to) && to.gameObject.activeInHierarchy && clip.Contains(ScreenRect((RectTransform)to.transform).center);
    }
}
