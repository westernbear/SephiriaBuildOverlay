using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Review;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private int _padItem;
    private int _padGoal;
    private int _padChecklist;
    private float _controllerReviewPreviewUntil;
    private bool _controllerReviewPreviewWasOpen;
    private bool _controllerReviewPreviewActive;
    private bool ControllerReviewPreview => _controllerReviewPreviewActive && Time.unscaledTime < _controllerReviewPreviewUntil;
    private object PreviewControllerReview()
    {
        if (_importing || _executing) return new { previewShown = false, gameActionsAllowed = false, durationSeconds = 0 };
        if (!_controllerReviewPreviewActive) _controllerReviewPreviewWasOpen = _showImport;
        _controllerReviewPreviewActive = true; _controllerReviewPreviewUntil = Time.unscaledTime + 10f;
        _showImport = true; _controllerMenu.Reset();
        return new { previewShown = true, gameActionsAllowed = false, durationSeconds = 10 };
    }

    // Paged, bounded controls instead of a mouse-only long scroll view. A
    // focused button registers a callback, invoked in Update, never by drawing.
    private void PadButton(string id, string text, Action action, bool enabled = true)
    {
        var prior = GUI.enabled;
        GUI.enabled = prior && enabled && !_importing && !_executing && !ControllerReviewPreview;
        var focused = _controllerMenu.IsSelected(id);
        if (GUILayout.Button((focused ? "▶ " : "") + text,
            focused ? _theme!.Selected : GUI.skin.button, GUILayout.MinHeight(30))) action();
        if (Event.current.type == EventType.Repaint && GUI.enabled)
            _controllerMenu.Add(id, () => { if (_showImport && _controller.GamepadMode && !_importing && !_executing) action(); });
        GUI.enabled = prior;
    }

    private void DrawControllerReview()
    {
        GUILayout.Label($"패드  방향키 이동 · {_controller.SubmitLabel} 확인 · {_controller.CancelLabel} 닫기", _theme!.Small);
        if (ControllerReviewPreview) GUILayout.Label("읽기 전용 패드 UI 미리보기 · 입력/게임 행동 비활성", _theme.WarningText);
        if (!_controllerModalGateReady)
        {
            GUILayout.Label($"네이티브 UI 입력 분리에 실패했습니다. 패드 검토 조작은 비활성화됩니다. {_importKey.Value}로 닫으세요.", _theme.WarningText);
            return;
        }
        if (_controller.DeviceId is null) GUILayout.Label("로컬 패드 연결을 확인하세요. 모호한 장치 입력은 실행하지 않습니다.", _theme.WarningText);
        GUILayout.BeginHorizontal();
        PadButton("review-tab", "빌드 검토", () => { _guideTab = 0; _controllerMenu.Reset(); });
        PadButton("progress-tab", "획득 횟수 보정", () => { _guideTab = 1; _controllerMenu.Reset(); }, _plan is not null);
        PadButton("checklist-tab", "체크리스트", () => { _guideTab = 2; _controllerMenu.Reset(); }, _plan is not null);
        GUILayout.EndHorizontal();
        GUILayout.Label("로비 가져오기: 시작 프리셋 포함 · 구매/영구 소비/저장 슬롯 덮어쓰기 없음", _theme.Small);
        GUILayout.Label(_status, _theme.WarningText, GUILayout.Height(70));
        if (_guideTab == 1 && _plan is not null && _state is not null) { DrawPadProgress(); return; }
        if (_guideTab == 2 && _plan is not null) { DrawPadChecklist(); return; }

        GUILayout.BeginHorizontal();
        PadButton("paste-build", "클립보드의 빌드 링크 붙여넣기", () => _locatorText = GUIUtility.systemCopyBuffer.Trim());
        PadButton("import-build", _importing ? "가져오는 중…" : "빌드 가져오기", () => { _controllerMenu.Reset(); _ = ImportAsync(); });
        GUILayout.EndHorizontal();
        GUILayout.Label(string.IsNullOrWhiteSpace(_locatorText) ? "URL/UUID를 복사한 뒤 붙여넣기를 선택하세요. 직접 입력은 키보드 모드에서 가능합니다." : _locatorText, _theme.Small);
        if (_review is null) { GUILayout.FlexibleSpace(); return; }
        var review = _review;
        var classified = review.Sections.Count(x => x.Role != TargetRole.Unclassified);
        GUILayout.Label(review.Build.Title, _theme.Heading);
        GUILayout.Label($"구역 {classified}/{review.Sections.Count} 분류 · 게임 {Application.version} / 빌드 {review.Build.GameVersion}", _theme.Small);
        if (review.Sections.Count == 0) { GUILayout.FlexibleSpace(); return; }
        _selectedSection = Math.Max(0, Math.Min(_selectedSection, review.Sections.Count - 1));
        var section = review.Sections[_selectedSection];
        GUILayout.BeginHorizontal();
        PadButton("previous-section", "이전 구역", () => ChangePadSection(-1));
        GUILayout.Label($"{_selectedSection + 1}/{review.Sections.Count}  {section.Source.Label}", _theme.Heading);
        PadButton("next-section", "다음 구역", () => ChangePadSection(1));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (var role in new[] { TargetRole.Required, TargetRole.Recommended, TargetRole.Excluded })
        {
            var value = role;
            PadButton("section-role-" + role, (section.Role == role ? "● " : "") + RoleLabel(role), () => section.Role = value);
        }
        PadButton("section-priority-minus", $"구역 우선 − ({section.Priority})", () => section.Priority--);
        PadButton("section-priority-plus", "구역 우선 +", () => section.Priority++);
        GUILayout.EndHorizontal();
        if (section.Items.Count > 0)
        {
            _padItem = Math.Max(0, Math.Min(_padItem, section.Items.Count - 1));
            var item = section.Items[_padItem];
            var slug = item.ManualCatalogKey ?? item.Source.Slug;
            review.Bindings.TryGetValue(slug, out var binding);
            GUILayout.BeginVertical(_theme.Card);
            GUILayout.BeginHorizontal();
            PadButton("previous-item", "이전 항목", () => { _padItem = (_padItem + section.Items.Count - 1) % section.Items.Count; _controllerMenu.Invalidate(); });
            GUILayout.Label($"{_padItem + 1}/{section.Items.Count}  {_catalog.FindBySlug(slug)?.KoreanName ?? slug}");
            PadButton("next-item", "다음 항목", () => { _padItem = (_padItem + 1) % section.Items.Count; _controllerMenu.Invalidate(); });
            GUILayout.EndHorizontal();
            GUILayout.Label(BindingLabel(binding?.Status) + " · " + (binding?.Explanation ?? "매핑 재검증 필요"), _theme.Small, GUILayout.Height(42));
            GUILayout.BeginHorizontal();
            PadButton("item-role", "항목 " + RoleLabel(item.RoleOverride ?? section.Role), () => item.RoleOverride = NextRole(item.RoleOverride ?? section.Role));
            PadButton("item-inherit", "구역 분류 상속", () => item.RoleOverride = null);
            PadButton("item-count-minus", $"필요 − ({item.DesiredAcquisitions})", () => item.DesiredAcquisitions = Math.Max(1, item.DesiredAcquisitions - 1));
            PadButton("item-count-plus", "필요 +", () => item.DesiredAcquisitions++);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            PadButton("item-priority-minus", $"우선 − ({item.PriorityOverride ?? section.Priority})", () => item.PriorityOverride = (item.PriorityOverride ?? section.Priority) - 1);
            PadButton("item-priority-plus", "우선 +", () => item.PriorityOverride = (item.PriorityOverride ?? section.Priority) + 1);
            PadButton("paste-mapping", "클립보드 slug로 매핑 수정", () =>
            {
                var key = GUIUtility.systemCopyBuffer.Trim();
                if (key.Length is > 0 and <= 128 && _catalog.FindBySlug(key) is not null) item.ManualCatalogKey = key;
                else _status = "클립보드에서 유효한 카탈로그 slug를 찾지 못했습니다.";
            });
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        PadButton("verify", "매핑 재검증", () => review.VerifyBindings(_gateway.DiscoverCatalogEntities()));
        PadButton("activate", "검토 완료 · 활성화", ActivateReviewedBuild);
        PadButton("close", "닫기", () => { _showImport = false; _controllerMenu.Reset(); });
        GUILayout.EndHorizontal();
    }

    private void ChangePadSection(int delta)
    {
        if (_review is null || _review.Sections.Count == 0) return;
        _selectedSection = (_selectedSection + delta + _review.Sections.Count) % _review.Sections.Count;
        _padItem = 0; _controllerMenu.Invalidate();
    }

    private void DrawPadProgress()
    {
        var targets = _plan!.Artifacts.OrderBy(x => x.Role == TargetRole.Required ? 0 : 1).ThenBy(x => x.Priority).ToArray();
        if (targets.Length == 0) { GUILayout.Label("아티팩트 목표 없음"); return; }
        _padGoal = Math.Max(0, Math.Min(_padGoal, targets.Length - 1));
        var target = targets[_padGoal];
        GUILayout.BeginHorizontal();
        PadButton("previous-goal", "이전 목표", () => _padGoal = (_padGoal + targets.Length - 1) % targets.Length);
        PadButton("next-goal", "다음 목표", () => _padGoal = (_padGoal + 1) % targets.Length);
        GUILayout.EndHorizontal();
        GUILayout.Label($"{_padGoal + 1}/{targets.Length}  {EntityName(target.CatalogKey)}", _theme!.Heading);
        var count = _state!.EffectiveAcquisitions(target.CatalogKey);
        GUILayout.Label($"{RoleLabel(target.Role)} · 획득 {count}/{target.DesiredAcquisitions}");
        if (_state.Artifacts.TryGetValue(target.CatalogKey, out var progress) && progress.IsUncertain)
            GUILayout.Label($"최소 {count}회 / 기록 불확실", _theme.WarningText);
        GUILayout.BeginHorizontal();
        PadButton("goal-minus", "획득 횟수 −", () => AdjustProgress(target.CatalogKey, -1));
        PadButton("goal-plus", "획득 횟수 +", () => AdjustProgress(target.CatalogKey, 1));
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
    }

    private void DrawPadChecklist()
    {
        var lines = _plan!.TalentAllocation.Select(x => $"{TalentLabel(x.Key)}  {x.Value}").Concat(_plan.Checklist.Select(x => "□ " + x)).ToArray();
        var pages = Math.Max(1, (lines.Length + 7) / 8);
        _padChecklist = Math.Max(0, Math.Min(_padChecklist, pages - 1));
        GUILayout.BeginHorizontal();
        PadButton("previous-checklist", "이전 페이지", () => _padChecklist = (_padChecklist + pages - 1) % pages);
        GUILayout.Label($"{_padChecklist + 1}/{pages}");
        PadButton("next-checklist", "다음 페이지", () => _padChecklist = (_padChecklist + 1) % pages);
        GUILayout.EndHorizontal();
        foreach (var line in lines.Skip(_padChecklist * 8).Take(8)) GUILayout.Label(line);
        GUILayout.Label("영구 특성 포인트는 자동으로 사용하지 않습니다.", _theme!.Small);
        GUILayout.FlexibleSpace();
    }
}
