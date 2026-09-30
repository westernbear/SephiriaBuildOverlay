using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private GUIStyle? _itemLabel;
    private GUIStyle? _itemWarning;
    private Font? _itemFont;
    public void SetItemFont(Font? font)
    {
        _itemFont = font;
        if (_itemLabel is not null) _itemLabel.font = font;
        if (_itemWarning is not null) _itemWarning.font = font;
    }

    // Passive drawing only: frames never receive input or replace native UI.
    public void DrawItemOverlay(BuildPlan? plan, ActiveBuildState? state, RecommendedAction? action, string confirmKey, string importKey, float scale)
    {
        if (Event.current.type != EventType.Repaint || _lastSnapshot is null || !_lastSnapshot.IsLocalPlayerOwned) return;
        _itemLabel ??= new GUIStyle(GUI.skin.label) { font = _itemFont, alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold, padding = new RectOffset(3, 3, 2, 2) };
        _itemWarning ??= new GUIStyle(_itemLabel);
        _itemLabel.fontSize = Mathf.RoundToInt(_nativeFontPixels * scale);
        _itemWarning.fontSize = Mathf.RoundToInt(_nativeFontPixels * scale);
        var oldColor = GUI.color;
        var oldDepth = GUI.depth;
        GUI.depth = -30;
        // IMGUI supplies top-left screen coordinates even when the legacy
        // UnityEngine.Input backend is disabled by the game's Input System.
        var mouse = Event.current.mousePosition;
        try
        {
            foreach (var candidate in _lastSnapshot.Candidates)
            {
                if (!_rectangles.TryGetValue(candidate.Token, out var visual) || visual == null || !visual.gameObject.activeInHierarchy) continue;
                var rect = ScreenRect(visual);
                if (rect.width < 4 || rect.height < 4) continue;
                var target = plan?.Artifacts.FirstOrDefault(x => x.CatalogKey == candidate.CatalogKey);
                var selected = action?.TargetToken == candidate.Token;
                var otherTarget = candidate.CatalogKey is not null && plan is not null &&
                    (plan.WeaponPath.Contains(candidate.CatalogKey) || plan.MiracleTarget == candidate.CatalogKey);
                var frame = CandidateFramePolicy.Resolve(plan is not null, target?.Role, otherTarget, selected);
                if (frame == CandidateFrameKind.None) continue;
                var color = frame switch
                {
                    CandidateFrameKind.NextAction => new Color(.45f, 1f, .7f),
                    CandidateFrameKind.Required => new Color(1f, .83f, .35f),
                    _ => new Color(.45f, .8f, 1f)
                };
                var thickness = Math.Max(2, Mathf.Round(2 * scale));
                // Draw outside the existing card, leaving its rarity frame and
                // item artwork intact. A second frame distinguishes F8 without
                // relying on color alone; no flashing / per-frame animation.
                var outer = new Rect(rect.x - thickness, rect.y - thickness, rect.width + thickness * 2, rect.height + thickness * 2);
                _nativeLayer?.Border("candidate:" + candidate.Token, outer, color, thickness);
                if (selected)
                {
                    var halo = new Rect(outer.x - thickness - 2, outer.y - thickness - 2, outer.width + (thickness + 2) * 2, outer.height + (thickness + 2) * 2);
                    _nativeLayer?.Border("candidate-next:" + candidate.Token, halo, new Color(color.r, color.g, color.b, .4f), thickness);
                    var key = action!.AutomaticBindingAllowed ? confirmKey : "수동";
                    var badgeWidth = Math.Max(28 * scale, _itemLabel.CalcSize(new GUIContent(key)).x + 8);
                    var badge = new Rect(rect.xMax - badgeWidth, rect.yMax - 22 * scale, badgeWidth, 22 * scale);
                    _nativeLayer?.Box("key-bg:" + candidate.Token, badge, new Color(.025f, .035f, .055f, .9f));
                    _nativeLayer?.Label("key:" + candidate.Token, badge, key, color, Mathf.Round(_nativeFontPixels * scale));
                }
                if (target is not null && rect.Contains(mouse))
                {
                    var acquired = state?.EffectiveAcquisitions(target.CatalogKey) ?? 0;
                    var uncertain = state is not null && state.Artifacts.TryGetValue(target.CatalogKey, out var progress) && progress.IsUncertain;
                    var detail = $"{(target.Role == TargetRole.Required ? "필수" : "추천")} · {(uncertain ? "최소 " : "")}{acquired}/{target.DesiredAcquisitions}";
                    var detailWidth = Math.Min(Screen.width - 4, Math.Max(rect.width, _itemLabel.CalcSize(new GUIContent(detail)).x + 12));
                    var detailRect = new Rect(Mathf.Clamp(rect.center.x - detailWidth / 2, 2, Screen.width - detailWidth - 2), Math.Max(2, rect.y - 28 * scale), detailWidth, 24 * scale);
                    _nativeLayer?.Box("detail-bg:" + candidate.Token, detailRect, new Color(.025f, .035f, .055f, .85f));
                    _nativeLayer?.Label("detail:" + candidate.Token, detailRect, detail, color, Mathf.Round(_nativeFontPixels * scale));
                }
                if (!selected || action is null) continue;
                if (action.MoneyCost == 0 && action.DiceCost == 0 && action.DiceRisk == DiceRisk.None) continue;
                var cost = action.MoneyCost > 0 ? $"돈 {action.MoneyCost}" : action.DiceCost > 0 ? $"주사위 {_lastSnapshot.SharedDice} → {Math.Max(0, _lastSnapshot.SharedDice - action.DiceCost)}" : "소비 없음";
                var risk = action.DiceRisk switch
                {
                    DiceRisk.LastSharedDieSpentBeforeMiracle => "이 행동 후 나무 뿌리 리롤 불가",
                    DiceRisk.SharedDiceSpentBeforeMiracle => "목표 나무 뿌리 획득 전 공유 주사위 소비",
                    _ => null
                };
                var warning = risk is null ? cost : cost + "\n" + risk;
                var warningWidth = Math.Min(Screen.width - 8, Math.Max(rect.width, 240 * scale));
                var warningRect = new Rect(Mathf.Clamp(rect.center.x - warningWidth / 2, 4, Screen.width - warningWidth - 4), rect.yMax + 4, warningWidth, 24 * scale);
                warningRect.height = Math.Max(_itemWarning.CalcHeight(new GUIContent(warning), warningWidth), (risk is null ? 28 : 58) * scale);
                if (action.Kind == ActionKind.Reroll) warningRect.y = rect.y - warningRect.height - 10 * scale;
                warningRect.y = Mathf.Clamp(warningRect.y, 4, Screen.height - warningRect.height - 4);
                _nativeLayer?.Box("warning-bg:" + candidate.Token, warningRect, new Color(.025f, .035f, .055f, .78f));
                _nativeLayer?.Label("warning:" + candidate.Token, warningRect, warning, risk is not null ? new Color(1f, .4f, .35f) : color, Mathf.Round(_nativeFontPixels * scale));
            }
        }
        finally { GUI.color = oldColor; GUI.depth = oldDepth; }
    }

    private Rect ScreenRect(RectTransform rect)
    {
        rect.GetWorldCorners(_highlightCorners);
        var camera = rect.GetComponentInParent<Canvas>()?.worldCamera;
        var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, _highlightCorners[0]);
        var topRight = RectTransformUtility.WorldToScreenPoint(camera, _highlightCorners[2]);
        return new Rect(bottomLeft.x, Screen.height - topRight.y, topRight.x - bottomLeft.x, topRight.y - bottomLeft.y);
    }

    private static void DrawBorder(Rect rect, Color color, float thickness)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
    }
}
