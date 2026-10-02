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
    private bool _showDetails;
    private bool _onlyUnresolved;
    private bool _advancedReview;

    private void ToggleAdvancedReview()
    {
        _advancedReview = !_advancedReview;
        _nativeBuildWindow?.Hide();
        _guideTab = 0;
        _controllerMenu.Reset();
        _importRect.width = OverlayUiTokens.AdvancedWidth;
        _importRect.height = OverlayUiTokens.AdvancedHeight;
    }

    private void DrawStyledWindows()
    {
        DrawNativeBuildWindow();
        if (!_showImport || !_advancedReview) return;
        _theme ??= new OverlayTheme(GUI.skin);
        var oldSkin = GUI.skin;
        var oldMatrix = GUI.matrix;
        var oldDepth = GUI.depth;
        try
        {
            GUI.skin = _theme.Skin;
            GUI.depth = OverlayUiTokens.WindowDepth;
            var scale = Mathf.Clamp(_uiScale.Value, 0.75f, 1.75f);
            // Fit the review window even on a small game window without changing game settings.
            scale = Math.Min(scale, Math.Min((Screen.width - 16) / OverlayUiTokens.AdvancedWidth, (Screen.height - 140) / OverlayUiTokens.AdvancedHeight));
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
                _importRect = FitWindow(_importRect, width, height - 120 / scale);
                _importRect = GUI.Window(OverlayUiTokens.WindowId, _importRect, DrawImportWindow, string.Empty);
                GUI.BringWindowToFront(OverlayUiTokens.WindowId);
                GUI.FocusWindow(OverlayUiTokens.WindowId);
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
        if (GUI.Button(new Rect(rect.width - 46, 10, 30, 30), "×", _theme.CompactButton)) close();
        GUI.DragWindow(new Rect(0, 0, rect.width - 155, 44));
    }

    private void DrawImportWindow(int windowId)
    {
        HandleShortcutEvent();
        WindowHeader("빌드 설정", _controller.GamepadMode ? _controller.CancelLabel : ImportPrompt, _importRect, () => _showImport = false);
        if (_controller.GamepadMode || ControllerReviewPreview) { DrawControllerReview(); return; }
        if (GUILayout.Button("← 간단히 보기", _theme!.CompactButton, GUILayout.Width(140))) ToggleAdvancedReview();
        if (_plan is not null && _state is not null)
        {
            GUILayout.BeginHorizontal();
            var tabs = new[] { "목표", "획득", "체크리스트" };
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
        if (_review is null)
        {
            GUILayout.FlexibleSpace();
            return;
        }

        GUILayout.Label(_review.Build.Title, _theme.Heading);
        if (_review.Build.GameVersion != Application.version)
            GUILayout.Label($"버전 불일치: 빌드 {_review.Build.GameVersion} / 게임 {Application.version}", _theme.WarningText);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(180));
        _sectionScroll = GUILayout.BeginScrollView(_sectionScroll, GUILayout.ExpandHeight(true));
        for (var i = 0; i < _review.Sections.Count; i++)
        {
            var section = _review.Sections[i];
            var style = i == _selectedSection ? _theme.Selected : GUI.skin.button;
            var label = $"{section.Source.Label}\n{RoleLabel(section.Role)}";
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
        if (_showDetails || binding?.AllowsAutomaticAction != true)
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
        "base" => "기지", "will" => "의지", "anger" => "분노", "rapid" => "신속",
        "wisdom" => "지혜", "patience" => "인내", "survival" => "생존", _ => key
    };
}
