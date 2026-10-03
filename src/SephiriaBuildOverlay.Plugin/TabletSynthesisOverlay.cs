using System.Collections;
using System.Reflection;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private bool _tabletMixMode;
    private string _synthesisSignature = "";
    private readonly List<TabletSynthesisRecipe> _synthesisSpecs = new();
    private readonly List<TabletSynthesisRecipe> _synthesisRecipes = new();
    private int _synthesisSpecIndex;
    private Task<TabletSynthesisSuggestion?>? _synthesisTask;
    private CancellationTokenSource? _synthesisCancellation;
    private TabletSynthesisSuggestion? _synthesisSuggestion;
    private RectTransform? _synthesisButton;
    private int _synthesisCost;
    private string _synthesisStatus = "합성 추천 계산 중";
    private readonly Dictionary<string, (string Id, int Rotation)> _synthesisFrames = new(StringComparer.Ordinal);

    private void ClearSynthesis()
    {
        var cancellation = _synthesisCancellation; var task = _synthesisTask;
        cancellation?.Cancel();
        if (task is not null) _ = task.ContinueWith(t => { _ = t.Exception; cancellation?.Dispose(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else cancellation?.Dispose();
        _synthesisCancellation = null; _synthesisTask = null; _synthesisSuggestion = null;
        _synthesisSignature = ""; _synthesisSpecs.Clear(); _synthesisRecipes.Clear(); _synthesisSpecIndex = 0;
        _synthesisFrames.Clear(); _synthesisButton = null;
    }

    private void CaptureSynthesis(BoardOptimizationInput input, List<ScreenCandidate> candidates, IDictionary? registry)
    {
        _synthesisFrames.Clear();
        var panel = registry?["UI_TabletMixPanel"] as Component;
        var mixer = panel is null ? null : ReadNamedObject(panel, "connectedTabletMix");
        var avatar = panel is null ? null : ReadNamedObject(panel, "playerAvatar");
        var used = mixer is null ? null : ReadNamedObject(mixer, "LocalUsed") as bool?;
        var cost = mixer is null ? null : ReadNamedNullableInt(mixer, "mixCost");
        if (panel == null || !panel.gameObject.activeInHierarchy || !ReadBool(panel, "IsOpened") || avatar is null ||
            !ReferenceEquals(ReadNamedObject(avatar, "Inventory"), _boardInventory) || used != false || cost is null or < 0)
        { ClearSynthesis(); _synthesisStatus = "합성 장치 또는 로컬 플레이어 확인 필요"; return; }
        _synthesisCost = cost.Value;
        _synthesisButton = (ReadNamedObject(panel, "mixButton") as Component)?.transform as RectTransform;
        (string? Id, int Rotation) Selected(string name)
        {
            var icon = ReadNamedObject(panel, name); var item = icon is null ? null : ReadNamedObject(icon, "Item");
            var key = item is null ? null : ReadNamedNullableInt(item, "entityID");
            return key is null or < 0 ? (null, 0) : (ReadNamedString(item!, "instanceID"), ReadNamedNullableInt(icon!, "rotation") ?? -1);
        }
        var firstSelected = Selected("itemIcon1"); var secondSelected = Selected("itemIcon2");
        var signature = _boardContext + "|" + _boardSignature + "|" + OptimizationInvariant(input) + "|mix:" +
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mixer!) + ":" + cost + ":" + firstSelected + ":" + secondSelected;
        var canMix = _boardInventory?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).SingleOrDefault(m =>
            m.Name == "CanMixTablet" && m.GetParameters().Length == 7 && m.GetParameters().Take(4).All(p => p.ParameterType == typeof(int)));
        var find = _boardInventory?.GetType().GetMethod("FindItemByInstanceID", new[] { typeof(int) });
        if (canMix is null || find is null) { ClearSynthesis(); _synthesisStatus = "게임 합성 조건 조회 불가"; return; }
        if (_synthesisSignature != signature)
        {
            ClearSynthesis(); _synthesisSignature = signature;
            _synthesisButton = (ReadNamedObject(panel, "mixButton") as Component)?.transform as RectTransform;
            _synthesisStatus = "합성 추천 계산 중";
            var materials = new List<(BoardTablet Tablet, bool CanRotate)>();
            foreach (var t in input.Tablets.Where(t => t.Movable && t.Key != "2101").OrderBy(t =>
                t.Id == firstSelected.Id || t.Id == secondSelected.Id ? 0 : 1).ThenBy(t => t.Id, StringComparer.Ordinal).Take(16))
            {
                if (!int.TryParse(t.Id, out var id)) continue;
                var item = find.Invoke(_boardInventory, new object[] { id });
                var entity = item is null ? null : ReadNamedObject(item, "Entity");
                var tablet = item is null ? null : ReadNamedObject(item, "StoneTablet");
                // The native panel refuses unthrowable items and custom tablets.
                if (entity is null || tablet is null || ReadNamedObject(entity, "cannotThrow") is not false ||
                    ReadNamedNullableInt(entity, "type") != 6 || !ReferenceEquals(ReadNamedObject(tablet, "Inventory"), _boardInventory)) continue;
                materials.Add((t, NativeCanRotate(tablet)));
            }
            for (var i = 0; i < materials.Count; i++)
            for (var j = i + 1; j < materials.Count; j++)
            {
                var a = materials[i]; var b = materials[j];
                var selected = new[] { firstSelected, secondSelected }.Where(x => x.Id is not null).ToArray();
                if (selected.Any(x => x.Id != a.Tablet.Id && x.Id != b.Tablet.Id)) continue;
                IEnumerable<int> Angles(BoardTablet t, bool rotatable)
                {
                    var chosen = selected.FirstOrDefault(x => x.Id == t.Id);
                    if (chosen.Id is not null) return chosen.Rotation is >= 0 and <= 3 ? new[] { chosen.Rotation } : Array.Empty<int>();
                    return rotatable ? Enumerable.Range(0, 4).OrderBy(r => (r - t.Rotation + 4) % 4) : new[] { t.Rotation };
                }
                foreach (var ra in Angles(a.Tablet, a.CanRotate))
                foreach (var rb in Angles(b.Tablet, b.CanRotate))
                    _synthesisSpecs.Add(new TabletSynthesisRecipe(a.Tablet.Id, ra, b.Tablet.Id, rb, a.CanRotate && b.CanRotate));
            }
        }
        // Native read-only eligibility is sliced too; never iterate thousands
        // of reflected queries in a single frame or call MixTablet here.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_synthesisSpecIndex < _synthesisSpecs.Count && _synthesisRecipes.Count < 48)
        {
            if ((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency >= 2) return;
            var spec = _synthesisSpecs[_synthesisSpecIndex++];
            var args = new object?[] { int.Parse(spec.First), spec.FirstRotation, int.Parse(spec.Second), spec.SecondRotation, mixer, null, null };
            if (canMix.Invoke(_boardInventory, args) is true) _synthesisRecipes.Add(spec);
        }
        if (_synthesisTask is null && _synthesisSuggestion is null && _synthesisStatus == "합성 추천 계산 중")
        {
            _synthesisCancellation = new CancellationTokenSource(); var token = _synthesisCancellation.Token;
            var recipes = _synthesisRecipes.ToArray();
            _synthesisTask = Task.Run(() => TabletSynthesisPlanner.Recommend(input, recipes, token), token);
        }
        if (_synthesisTask is { IsCompleted: true })
        {
            if (_synthesisTask.Status == TaskStatus.RanToCompletion) _synthesisSuggestion = _synthesisTask.Result;
            else if (_synthesisTask.IsFaulted) _log.LogWarning("합성 추천 중단: " + _synthesisTask.Exception?.GetBaseException().Message);
            _synthesisTask = null; _synthesisCancellation?.Dispose(); _synthesisCancellation = null;
            _synthesisStatus = _synthesisSuggestion is null ? "빌드 효과를 유지하는 합성 조합을 찾지 못했습니다" : "합성 추천 · 게임에서 수동 확인";
        }
        if (_synthesisSuggestion is null) return;
        foreach (var material in new[] { (_synthesisSuggestion.Recipe.First, _synthesisSuggestion.Recipe.FirstRotation),
            (_synthesisSuggestion.Recipe.Second, _synthesisSuggestion.Recipe.SecondRotation) })
        {
            var item = _boardItems.FirstOrDefault(x => x.InstanceId == material.Item1);
            if (item is null || !_slotVisuals.TryGetValue(item.Position, out var icon) || icon.transform is not RectTransform rect) continue;
            var token = "mix:item:" + item.InstanceId;
            candidates.Add(new ScreenCandidate(token, CandidateKind.Item, null)); _rectangles[token] = rect;
            _synthesisFrames[token] = (item.InstanceId, material.Item2);
        }
    }

    private void DrawTabletSynthesisOverlay(float scale, Vector2 mouse, GameObject? focused)
    {
        if (!_tabletMixMode || _synthesisSuggestion is null || _lastSnapshot?.Screen != ScreenKind.TabletBoard) return;
        var color = new Color(.9f, .55f, 1f); var pixels = Mathf.Round(_nativeFontPixels * scale);
        var clip = _boardPanel is null ? null : ReadNamedObject(_boardPanel, "inventoryZone") as RectTransform;
        if (clip == null) return;
        var clipRect = ScreenRect(clip);
        foreach (var pair in _synthesisFrames)
        {
            if (!_rectangles.TryGetValue(pair.Key, out var visual) || visual == null || !visual.gameObject.activeInHierarchy) continue;
            var rect = ScreenRect(visual); if (!clipRect.Contains(rect.center)) continue;
            _nativeLayer?.Border("synthesis-contrast:" + pair.Key, new Rect(rect.x - 5, rect.y - 5, rect.width + 10, rect.height + 10), new Color(.025f, .015f, .04f, .95f), 5 * scale);
            _nativeLayer?.Border("synthesis:" + pair.Key, new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), color, 3 * scale);
            var badge = new Rect(rect.x, rect.yMax - 22 * scale, Math.Max(rect.width, 75 * scale), 22 * scale);
            _nativeLayer?.Box("synthesis-bg:" + pair.Key, badge, new Color(.035f, .015f, .055f, .92f));
            _nativeLayer?.Label("synthesis-label:" + pair.Key, badge, "합성 · " + pair.Value.Rotation * 90 + "°", color, pixels);
            var hover = _controllerMode ? focused != null && (focused.transform == visual.transform || focused.transform.IsChildOf(visual.transform)) : rect.Contains(mouse);
            if (!hover) continue;
            var detail = _synthesisSuggestion.Reason;
            var size = _nativeLayer?.MeasureLabel("synthesis-detail:" + pair.Key, detail, pixels) ?? new Vector2(360 * scale, 24 * scale);
            var width = Math.Min(Screen.width - 8, size.x + 12 * scale);
            var detailRect = new Rect(Mathf.Clamp(rect.center.x - width / 2, 4, Screen.width - width - 4), Math.Max(4, rect.y - size.y - 8), width, size.y + 4);
            _nativeLayer?.Box("synthesis-detail-bg:" + pair.Key, detailRect, new Color(.035f, .015f, .055f, .92f));
            _nativeLayer?.Label("synthesis-detail:" + pair.Key, detailRect, detail, color, pixels);
        }
        if (_synthesisButton == null || !_synthesisButton.gameObject.activeInHierarchy) return;
        var buttonRect = ScreenRect(_synthesisButton);
        var affordable = _lastSnapshot.Money >= _synthesisCost;
        var text = "합성 추천 · 돈 " + _synthesisCost + (affordable ? " · 수동 확인" : " · 재화 부족") +
            (_synthesisSuggestion.LosesRotation ? "\n합성 후 회전 불가" : "");
        var height = (_synthesisSuggestion.LosesRotation ? 44 : 24) * scale;
        var labelRect = new Rect(Mathf.Clamp(buttonRect.center.x - 150 * scale, 4, Math.Max(4, Screen.width - 300 * scale - 4)),
            Math.Max(4, buttonRect.y - height - 4), Math.Min(Screen.width - 8, 300 * scale), height);
        _nativeLayer?.Box("synthesis-cost-bg", labelRect, new Color(.035f, .015f, .055f, .92f));
        _nativeLayer?.Label("synthesis-cost", labelRect, text, affordable && !_synthesisSuggestion.LosesRotation ? color : new Color(1f, .45f, .35f), pixels);
    }
}
