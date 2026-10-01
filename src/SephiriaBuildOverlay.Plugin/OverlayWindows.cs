using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Review;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private OverlayTheme? _theme;
    private int _selectedSection;
    private int _guideTab;
    private Vector2 _sectionScroll;
    private Vector2 _guideScroll;
    private Vector2 _importStatusScroll;
    private bool _showDetails;
    private bool _onlyUnresolved;
    private bool _advancedReview;

    private void ToggleAdvancedReview()
    {
        _advancedReview = !_advancedReview;
        _guideTab = 0;
        _controllerMenu.Reset();
        _importRect.width = _advancedReview ? 920 : 650;
        _importRect.height = _advancedReview ? 650 : 450;
    }

    private void DrawQuickBuildSummary()
    {
        GUILayout.Label("링크 하나로 모든 항목을 추천 목표로 적용합니다.", _theme!.Small);
        GUILayout.Label("로비에서는 해금된 시작 세팅도 적용 · 구매/영구 소비 없음", _theme.Small);
        _importStatusScroll = GUILayout.BeginScrollView(_importStatusScroll, GUILayout.Height(70));
        GUILayout.Label(_status, _theme.WarningText);
        GUILayout.EndScrollView();
        if (_review is not null)
        {
            GUILayout.Label(_review.Build.Title, _theme.Heading);
            var unresolved = _review.Bindings.Values.Count(x => !x.AllowsAutomaticAction);
            GUILayout.Label(_plan is null ? "가이드 비활성 · 위 오류를 확인하세요." : $"목표 {_plan.Artifacts.Count}종 · {_plan.Artifacts.Sum(x => x.DesiredAcquisitions)}회", _theme.Small);
            if (unresolved > 0) GUILayout.Label($"미검증 매핑 {unresolved}개 · 해당 자동 행동은 실행하지 않습니다.", _theme.WarningText);
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{ImportPrompt} 창 닫기  ·  {OverlayPrompt} 안내 표시  ·  {ConfirmPrompt} 한 동작 확인", _theme.Small);
    }

    private void DrawQuickImport()
    {
        GUILayout.BeginHorizontal();
        // TextField may consume Return itself. Capture it before drawing the
        // single-line field, then submit only if that field owns keyboard focus.
        var submit = Event.current.type == EventType.KeyDown && Event.current.keyCode is KeyCode.Return or KeyCode.KeypadEnter
            && GUI.GetNameOfFocusedControl() == "quick-build-link";
        GUI.SetNextControlName("quick-build-link");
        _locatorText = GUILayout.TextField(_locatorText, GUILayout.ExpandWidth(true));
        if (submit && Event.current.type != EventType.Used) Event.current.Use();
        GUI.enabled = !_importing && !_executing;
        var load = GUILayout.Button(_importing ? "불러오는 중…" : "불러오기", _theme!.Primary, GUILayout.Width(130));
        if ((load || submit) && GUI.enabled) _ = ImportAsync();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        DrawQuickBuildSummary();
        if (GUILayout.Button("고급 설정", _theme.CompactButton, GUILayout.Width(120))) ToggleAdvancedReview();
    }

    private void DrawStyledWindows()
    {
        _theme ??= new OverlayTheme(GUI.skin);
        var oldSkin = GUI.skin;
        var oldMatrix = GUI.matrix;
        var oldDepth = GUI.depth;
        try
        {
            GUI.skin = _theme.Skin;
            GUI.depth = -20;
            var scale = Mathf.Clamp(_uiScale.Value, 0.75f, 1.75f);
            // Fit the review window even on a small game window without changing game settings.
            scale = Math.Min(scale, Math.Min(Screen.width / 760f, Screen.height / 540f));
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            var width = Screen.width / scale;
            var height = Screen.height / scale;
            // IMGUI does not consume the game's Input System mouse input. Keep avatar
            // attacks/movement out of our controls while the pointer is over the guide.
            _pointerOverOverlay = false;
            if (_showImport)
            {
                var priorColor = GUI.color;
                GUI.color = new Color(0, 0, 0, .55f);
                GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
                GUI.color = priorColor;
                if (Event.current.type == EventType.Repaint) _controllerMenu.BeginFrame();
                _importRect = FitWindow(_importRect, width, height);
                _importRect = GUI.Window(761331, _importRect, DrawImportWindow, string.Empty);
                GUI.BringWindowToFront(761331);
                GUI.FocusWindow(761331);
                if (Event.current.type == EventType.Repaint) _controllerMenu.EndFrame();
            }
            // Consume outside clicks/scroll/keys too, after our controls have
            // processed them. Never let the closing event hit another IMGUI UI.
            if (_modalInput.Capturing && Event.current.type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag
                or EventType.ScrollWheel or EventType.KeyDown or EventType.KeyUp) Event.current.Use();
        }
        finally { GUI.skin = oldSkin; GUI.matrix = oldMatrix; GUI.depth = oldDepth; }
    }

    private static Rect FitWindow(Rect rect, float width, float height)
    {
        rect.width = Math.Min(rect.width, width - 16);
        rect.height = Math.Min(rect.height, height - 16);
        rect.x = Mathf.Clamp(rect.x, 8, Math.Max(8, width - rect.width - 8));
        rect.y = Mathf.Clamp(rect.y, 8, Math.Max(8, height - rect.height - 8));
        return rect;
    }

    private void WindowHeader(string title, string key, Rect rect, Action close)
    {
        GUI.Label(new Rect(18, 12, rect.width - 170, 30), title, _theme!.Heading);
        GUI.Label(new Rect(rect.width - 146, 18, 95, 24), key + " 닫기", _theme.Small);
        if (GUI.Button(new Rect(rect.width - 46, 10, 30, 30), "×", _theme.CompactButton)) close();
        GUI.DragWindow(new Rect(0, 0, rect.width - 155, 44));
    }

    private void DrawImportWindow(int windowId)
    {
        HandleShortcutEvent();
        WindowHeader("SEPHIRIA  /  빌드 불러오기", _controller.GamepadMode ? _controller.CancelLabel : ImportPrompt, _importRect, () => _showImport = false);
        if (_controller.GamepadMode || ControllerReviewPreview) { DrawControllerReview(); return; }
        if (!_advancedReview) { DrawQuickImport(); return; }
        if (GUILayout.Button("← 간단히 보기", _theme!.CompactButton, GUILayout.Width(140))) ToggleAdvancedReview();
        if (_plan is not null && _state is not null)
        {
            GUILayout.BeginHorizontal();
            var tabs = new[] { "빌드 검토", "획득 횟수 보정", "수동 체크리스트" };
            for (var i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], _guideTab == i ? _theme!.Selected : GUI.skin.button)) _guideTab = i;
            GUILayout.EndHorizontal();
            if (_guideTab != 0)
            {
                _guideScroll = GUILayout.BeginScrollView(_guideScroll, GUILayout.ExpandHeight(true));
                if (_guideTab == 1) DrawGoalList();
                else
                {
                    foreach (var talent in _plan.TalentAllocation) GUILayout.Label($"{TalentLabel(talent.Key)}  {talent.Value}");
                    foreach (var line in _plan.Checklist) GUILayout.Label("□ " + line);
                    GUILayout.Label("영구 특성 포인트는 자동으로 사용하지 않습니다.", _theme!.Small);
                }
                GUILayout.EndScrollView();
                return;
            }
        }
        GUILayout.BeginHorizontal();
        _locatorText = GUILayout.TextField(_locatorText, GUILayout.ExpandWidth(true));
        GUI.enabled = !_importing && !_executing;
        if (GUILayout.Button(_importing ? "가져오는 중…" : "빌드 가져오기", _theme!.Primary, GUILayout.Width(140))) _ = ImportAsync();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label("sephiria.wiki/builds/UUID 또는 UUID  ·  새 빌드는 모두 추천으로 즉시 적용", _theme!.Small);
        GUILayout.Label("로비에서는 시작 프리셋도 적용합니다. 해금/현재 포인트만 사용 · 구매/영구 소비/저장 슬롯 덮어쓰기 없음", _theme.Small);
        _importStatusScroll = GUILayout.BeginScrollView(_importStatusScroll, GUILayout.Height(70));
        GUILayout.Label(new GUIContent(_status, _status), _theme.WarningText);
        GUILayout.EndScrollView();
        if (_review is null)
        {
            GUILayout.BeginVertical(_theme.Card);
            GUILayout.Label("빌드를 불러오면 목표를 검토할 수 있습니다.", _theme.Heading);
            GUILayout.Label("기본은 모두 추천입니다. 필요할 때만 분류·횟수·매핑을 조정하세요.");
            GUILayout.Label($"{OverlayPrompt} 가이드 표시  /  {ConfirmPrompt} 한 동작 확인", _theme.Small);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            return;
        }

        var classified = _review.Sections.Count(x => x.Role != TargetRole.Unclassified);
        var verified = _review.Bindings.Values.Count(x => x.AllowsAutomaticAction);
        GUILayout.Label(_review.Build.Title, _theme.Heading);
        GUILayout.Label($"구역 {classified}/{_review.Sections.Count} 분류  ·  매핑 {verified}/{_review.Bindings.Count} 검증  ·  빌드 {_review.Build.GameVersion} / 게임 {Application.version}", _theme.Small);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(210));
        _sectionScroll = GUILayout.BeginScrollView(_sectionScroll, GUILayout.ExpandHeight(true));
        for (var i = 0; i < _review.Sections.Count; i++)
        {
            var section = _review.Sections[i];
            var style = i == _selectedSection ? _theme.Selected : GUI.skin.button;
            var label = $"{i + 1}. {section.Source.Label}\n{RoleLabel(section.Role)}  ·  {section.Items.Count}개";
            if (GUILayout.Button(label, style, GUILayout.MinHeight(62))) { _selectedSection = i; _reviewScroll = Vector2.zero; }
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        if (_review.Sections.Count > 0)
        {
            _selectedSection = Math.Min(_selectedSection, _review.Sections.Count - 1);
            var section = _review.Sections[_selectedSection];
            GUILayout.BeginHorizontal();
            GUILayout.Label("구역 분류", GUILayout.Width(80));
            foreach (var role in new[] { TargetRole.Required, TargetRole.Recommended, TargetRole.Excluded })
                if (GUILayout.Button(RoleLabel(role), section.Role == role ? _theme.Selected : GUI.skin.button)) section.Role = role;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            _onlyUnresolved = GUILayout.Toggle(_onlyUnresolved, "미해결만 보기");
            _showDetails = GUILayout.Toggle(_showDetails, "설명 / 매핑 보기");
            GUILayout.Label($"우선순위 {section.Priority}", _theme.Small, GUILayout.Width(95));
            if (GUILayout.Button("−", _theme.CompactButton, GUILayout.Width(28))) section.Priority--;
            if (GUILayout.Button("+", _theme.CompactButton, GUILayout.Width(28))) section.Priority++;
            GUILayout.EndHorizontal();
            _reviewScroll = GUILayout.BeginScrollView(_reviewScroll, GUILayout.ExpandHeight(true));
            if (_showDetails && !string.IsNullOrWhiteSpace(section.Source.Description))
                GUILayout.Label(section.Source.Description, _theme.Small);
            foreach (var item in section.Items)
            {
                var slug = item.ManualCatalogKey ?? item.Source.Slug;
                _review.Bindings.TryGetValue(slug, out var binding);
                if (_onlyUnresolved && binding?.AllowsAutomaticAction == true) continue;
                DrawReviewItem(section, item, slug, binding);
            }
            GUILayout.EndScrollView();
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUI.enabled = !_importing && !_executing;
        if (GUILayout.Button("매핑 재검증", GUILayout.Width(160))) _review.VerifyBindings(_gateway.DiscoverCatalogEntities());
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("검토 완료 · 활성화", _theme.Primary, GUILayout.Width(210))) ActivateReviewedBuild();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawReviewItem(ReviewSection section, ReviewItem item, string slug, CatalogBinding? binding)
    {
        GUILayout.BeginVertical(_theme!.Card);
        GUILayout.BeginHorizontal();
        GUILayout.Label(_catalog.FindBySlug(slug)?.KoreanName ?? item.Source.Slug, GUILayout.ExpandWidth(true));
        Badge(BindingLabel(binding?.Status), binding?.AllowsAutomaticAction == true ? OverlayTheme.Good : OverlayTheme.Warning);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(RoleLabel(item.RoleOverride ?? section.Role), GUILayout.Width(68)))
            item.RoleOverride = NextRole(item.RoleOverride ?? section.Role);
        if (item.RoleOverride.HasValue && GUILayout.Button("상속", GUILayout.Width(50))) item.RoleOverride = null;
        GUILayout.FlexibleSpace();
        GUILayout.Label("필요", _theme.Small, GUILayout.Width(28));
        if (GUILayout.Button("−", _theme.CompactButton, GUILayout.Width(28))) item.DesiredAcquisitions = Math.Max(1, item.DesiredAcquisitions - 1);
        GUILayout.Label(item.DesiredAcquisitions.ToString(), GUILayout.Width(24));
        if (GUILayout.Button("+", _theme.CompactButton, GUILayout.Width(28))) item.DesiredAcquisitions++;
        GUILayout.Label("우선", _theme.Small, GUILayout.Width(28));
        if (GUILayout.Button("−", _theme.CompactButton, GUILayout.Width(28))) item.PriorityOverride = (item.PriorityOverride ?? section.Priority) - 1;
        GUILayout.Label((item.PriorityOverride ?? section.Priority).ToString(), GUILayout.Width(24));
        if (GUILayout.Button("+", _theme.CompactButton, GUILayout.Width(28))) item.PriorityOverride = (item.PriorityOverride ?? section.Priority) + 1;
        GUILayout.EndHorizontal();
        if (_showDetails || binding?.AllowsAutomaticAction != true)
        {
            GUILayout.Label(binding?.Explanation ?? "매핑을 재검증하세요.", _theme.Small);
            GUILayout.Label("카탈로그 slug", _theme.Small);
            var edited = GUILayout.TextField(slug);
            item.ManualCatalogKey = string.IsNullOrWhiteSpace(edited) || edited == item.Source.Slug ? null : edited.Trim();
        }
        GUILayout.EndVertical();
    }

    private void DrawOverlayWindow(int windowId)
    {
        HandleShortcutEvent();
        if (_plan is null || _state is null) return;
        WindowHeader("빌드 가이드", OverlayPrompt, _overlayRect, () => _showOverlay = false);
        GUILayout.Label(_review?.Build.Title ?? "활성 빌드", _theme!.Small);
        GUILayout.BeginHorizontal();
        var tabs = new[] { "다음 행동", "목표", "체크리스트", "상태" };
        for (var i = 0; i < tabs.Length; i++)
            if (GUILayout.Button(tabs[i], _guideTab == i ? _theme.Selected : GUI.skin.button)) { _guideTab = i; _guideScroll = Vector2.zero; }
        GUILayout.EndHorizontal();
        _guideScroll = GUILayout.BeginScrollView(_guideScroll, GUILayout.ExpandHeight(true));
        switch (_guideTab)
        {
            case 0: DrawNextAction(); break;
            case 1: DrawGoalList(); break;
            case 2:
                foreach (var talent in _plan.TalentAllocation) GUILayout.Label($"{TalentLabel(talent.Key)}  {talent.Value}");
                foreach (var line in _plan.Checklist) GUILayout.Label("□ " + line);
                GUILayout.Label("영구 특성 포인트는 자동으로 사용하지 않습니다.", _theme.Small);
                break;
            case 3:
                GUILayout.Label($"화면  {_lastSnapshot?.Screen}");
                GUILayout.Label($"로컬 소유  {_lastSnapshot?.IsLocalPlayerOwned}  ·  후보 {_lastSnapshot?.Candidates.Count ?? 0}개");
                GUILayout.Label($"인벤토리 {_lastSnapshot?.Inventory.Count ?? 0}개  ·  돈 {_lastSnapshot?.Money ?? 0}  ·  주사위 {_lastSnapshot?.SharedDice ?? 0}");
                GUILayout.Label($"무기  {EntityName(_lastSnapshot?.CurrentWeapon, CatalogKind.Weapon)}");
                GUILayout.Label("나무 뿌리  " + string.Join(" / ", (_lastSnapshot?.MiracleKeys ?? Array.Empty<string>()).Select(x => EntityName(x, CatalogKind.Miracle))));
                GUILayout.Label(_status, _theme.Small);
                break;
        }
        GUILayout.EndScrollView();
        if (GUILayout.Button("빌드 검토 열기  " + ImportPrompt)) _showImport = true;
    }

    private void DrawNextAction()
    {
        GUILayout.BeginVertical(_theme!.Card);
        GUILayout.Label(_executing ? "응답 대기 중" : "다음 행동", _theme.Heading);
        GUILayout.Label(_recommendation?.Message ?? "현재 화면의 후보를 확인하고 있습니다.");
        if (_recommendation?.Action is { } action)
        {
            var candidate = _lastSnapshot?.Candidates.FirstOrDefault(x => x.Token == action.TargetToken);
            if (candidate?.CatalogKey is not null)
                GUILayout.Label(EntityName(candidate.CatalogKey), _theme.Heading);
            var money = _lastSnapshot?.Money ?? 0;
            var dice = _lastSnapshot?.SharedDice ?? 0;
            GUILayout.Label($"돈  {money} → {money - action.MoneyCost}   (−{action.MoneyCost})");
            GUILayout.Label($"공유 주사위  {dice} → {dice - action.DiceCost}   (−{action.DiceCost})");
            if (action.DiceRisk != DiceRisk.None)
                Badge(action.DiceRisk == DiceRisk.LastSharedDieSpentBeforeMiracle
                    ? "이 행동 후 나무 뿌리 리롤 불가" : "목표 나무 뿌리 획득 전 주사위 소비", OverlayTheme.Danger);
            Badge(!action.AutomaticBindingAllowed ? "매핑 미검증 · 자동 행동 차단" : _executing ? "이전 요청 처리 중" : $"{ConfirmPrompt}  ·  이 동작 하나 확인",
                !action.AutomaticBindingAllowed ? OverlayTheme.Warning : OverlayTheme.Accent);
        }
        GUILayout.EndVertical();
        GUILayout.Label("무기 경로", _theme.Heading);
        GUILayout.Label(string.Join(" → ", _plan!.WeaponPath.Select(x => EntityName(x, CatalogKind.Weapon))));
        GUILayout.Label("나무 뿌리  " + EntityName(_plan.MiracleTarget, CatalogKind.Miracle));
        if (_state!.MiracleAcquired) Badge("목표 나무 뿌리 획득 완료", OverlayTheme.Good);
        GUILayout.Label(_status, _theme.Small);
    }

    private void DrawGoalList()
    {
        foreach (var target in _plan!.Artifacts.OrderBy(x => x.Role == TargetRole.Required ? 0 : 1).ThenBy(x => x.Priority))
        {
            var count = _state!.EffectiveAcquisitions(target.CatalogKey);
            _state.Artifacts.TryGetValue(target.CatalogKey, out var progress);
            GUILayout.BeginVertical(_theme!.Card);
            GUILayout.Label(EntityName(target.CatalogKey, CatalogKind.Artifact));
            GUILayout.BeginHorizontal();
            Badge($"{RoleLabel(target.Role)}  {count}/{target.DesiredAcquisitions}", count >= target.DesiredAcquisitions ? OverlayTheme.Good : OverlayTheme.Accent);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("−", _theme.CompactButton, GUILayout.Width(30))) AdjustProgress(target.CatalogKey, -1);
            if (GUILayout.Button("+", _theme.CompactButton, GUILayout.Width(30))) AdjustProgress(target.CatalogKey, 1);
            GUILayout.EndHorizontal();
            if (progress?.IsUncertain == true) GUILayout.Label($"최소 {count}회 / 기록 불확실 · 오른쪽 버튼으로 보정", _theme.Small);
            GUILayout.EndVertical();
        }
    }

    private string EntityName(string? key, CatalogKind? kind = null)
    {
        if (string.IsNullOrEmpty(key)) return "없음";
        var entry = _catalog.Entries.FirstOrDefault(x => x.GameKey == key && (!kind.HasValue || x.Kind == kind));
        if (entry is null && _review is not null)
        {
            var binding = _review.Bindings.Values.FirstOrDefault(x => x.GameKey == key);
            if (binding is not null) entry = _catalog.FindBySlug(binding.Slug, kind);
        }
        return entry?.KoreanName ?? key!;
    }

    private void Badge(string text, Color color)
    {
        var previous = GUI.contentColor;
        GUI.contentColor = color;
        GUILayout.Label(text);
        GUI.contentColor = previous;
    }

    private static string BindingLabel(BindingStatus? status) => status switch
    {
        BindingStatus.Verified => "검증 완료",
        BindingStatus.Ambiguous => "매핑 모호",
        BindingStatus.MetadataMismatch => "메타데이터 불일치",
        BindingStatus.MissingCatalogEntry => "카탈로그 미등록",
        _ => "게임 매핑 미해결"
    };

    private static string TalentLabel(string key) => key switch
    {
        "base" => "기초", "will" => "의지", "anger" => "분노", "rapid" => "신속",
        "wisdom" => "지혜", "patience" => "인내", "survival" => "생존", _ => key
    };
}
