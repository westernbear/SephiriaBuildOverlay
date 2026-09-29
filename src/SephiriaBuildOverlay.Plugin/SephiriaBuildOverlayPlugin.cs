using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Review;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class SephiriaBuildOverlayPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "io.github.sephiria.build-overlay";
    public const string PluginName = "Sephiria Build Overlay";
    public const string PluginVersion = "0.1.0";

    private ConfigEntry<KeyCode> _importKey = null!;
    private ConfigEntry<KeyCode> _overlayKey = null!;
    private ConfigEntry<KeyCode> _confirmKey = null!;
    private ConfigEntry<bool> _acceptVersionMismatch = null!;
    private WikiBuildSource _source = null!;
    private VersionedCatalog _catalog = null!;
    private UnityGameGateway _gateway = null!;
    private ConfirmedActionExecutor _executor = null!;
    private BuildReviewSession? _review;
    private BuildPlan? _plan;
    private ActiveBuildState? _state;
    private Recommendation? _recommendation;
    private bool _showImport;
    private bool _showOverlay = true;
    private string _locatorText = string.Empty;
    private string _status = "F6: 빌드 가져오기";
    private Vector2 _reviewScroll;
    private Rect _importRect = new(40, 40, 720, 680);
    private Rect _overlayRect = new(20, 20, 430, 360);
    private readonly object _uiStateGate = new();
    private bool _importing;
    private bool _executing;
    private Harmony? _harmony;
    private static SephiriaBuildOverlayPlugin? _instance;
    private readonly HashSet<string> _seenRewardInstances = new(StringComparer.Ordinal);
    private float _nextSnapshotAt;
    private RunSnapshot? _lastSnapshot;

    private void Awake()
    {
        _importKey = Config.Bind("Keys", "ImportWindow", KeyCode.F6, "빌드 가져오기/검토 창");
        _overlayKey = Config.Bind("Keys", "Overlay", KeyCode.F7, "인게임 오버레이 전환");
        _confirmKey = Config.Bind("Keys", "ConfirmOneAction", KeyCode.F8, "추천 동작 하나 확인");
        _acceptVersionMismatch = Config.Bind("Safety", "AcceptVersionMismatch", false, "빌드/게임 버전 불일치 경고를 확인한 것으로 처리");
        _source = new WikiBuildSource();
        _catalog = VersionedCatalog.LoadEmbedded("1.0.33");
        _gateway = new UnityGameGateway(_catalog, Logger);
        _executor = new ConfirmedActionExecutor(_gateway);
        _instance = this;
        InstallProgressPatches();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Game={Application.version}");
    }

    private void OnDestroy()
    {
        _source.Dispose();
        _gateway.Dispose();
        _harmony?.UnpatchSelf();
        _instance = null;
    }

    private void Update()
    {
        _gateway.Tick();
        if (Input.GetKeyDown(_importKey.Value)) _showImport = !_showImport;
        if (Input.GetKeyDown(_overlayKey.Value)) _showOverlay = !_showOverlay;

        if (_plan is not null && _state is not null && Time.unscaledTime >= _nextSnapshotAt)
        {
            _nextSnapshotAt = Time.unscaledTime + 0.1f;
            var snapshot = _gateway.CaptureOnMainThread();
            _lastSnapshot = snapshot;
            if (_state.RunId != snapshot.RunId)
            {
                _state = ActiveBuildState.Activate(_plan, snapshot);
                _status = "새 런을 감지해 진행 상태를 분리했습니다.";
            }
            _recommendation = new RecommendationEngine(BindingsByGameKey()).Recommend(_plan, _state, snapshot);
            if (!string.IsNullOrEmpty(_plan.MiracleTarget) && _gateway.GetLocalMiracleKeys().Contains(_plan.MiracleTarget!))
                _state.MarkMiracleAcquired();
            _gateway.SetHighlight(_recommendation.Action?.TargetToken);
        }

        if (Input.GetKeyDown(_confirmKey.Value) && !_executing && _recommendation?.Action is not null)
            _ = ConfirmCurrentAsync(_recommendation.Action);
    }

    private async Task ConfirmCurrentAsync(RecommendedAction action)
    {
        _executing = true;
        try
        {
            var result = await _executor.ConfirmOnceAsync(action);
            lock (_uiStateGate) _status = result.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            lock (_uiStateGate) _status = "행동 실행 오류: " + ex.Message;
        }
        finally
        {
            _executing = false;
        }
    }

    private async Task ImportAsync()
    {
        if (_importing) return;
        if (!BuildLocator.TryParse(_locatorText, out var locator, out var error))
        {
            _status = error!;
            return;
        }
        _importing = true;
        _status = "Wiki에서 빌드를 가져오는 중...";
        try
        {
            var result = await _source.ImportAsync(locator!);
            var review = new BuildReviewSession(result.Build, _catalog);
            // Unity objects must be inspected on the main thread. ContinueWith is avoided; Unity's context returns here.
            review.VerifyBindings(_gateway.DiscoverCatalogEntities());
            lock (_uiStateGate)
            {
                _review = review;
                _plan = null;
                _state = null;
                _status = result.Warning ?? $"'{result.Build.Title}' 가져오기 완료 ({result.Origin}). 모든 구역을 분류하세요.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex);
            lock (_uiStateGate) _status = "가져오기 실패: " + ex.Message;
        }
        finally { _importing = false; }
    }

    private void ActivateReviewedBuild()
    {
        if (_review is null) return;
        try
        {
            _review.VerifyBindings(_gateway.DiscoverCatalogEntities());
            _plan = _review.CreatePlan(Application.version, _acceptVersionMismatch.Value);
            var snapshot = _gateway.CaptureOnMainThread();
            var store = new ActiveStateStore();
            var existing = store.Load(_plan.SourceBuildId, snapshot.RunId);
            _state = ActiveBuildState.Activate(_plan, snapshot, existing);
            store.Save(_state);
            _status = $"빌드 활성화 완료. 필수 목표 {_plan.Artifacts.Count(x => x.Role == TargetRole.Required)}종.";
            _showImport = false;
        }
        catch (Exception ex) { _status = "활성화 불가: " + ex.Message; }
    }

    private IReadOnlyDictionary<string, CatalogBinding> BindingsByGameKey()
    {
        if (_review is null) return new Dictionary<string, CatalogBinding>();
        return _review.Bindings.Values.Where(x => x.GameKey is not null)
            .GroupBy(x => x.GameKey!, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    }

    private void InstallProgressPatches()
    {
        try
        {
            _harmony = new Harmony(PluginGuid + ".progress");
            var gridType = AccessTools.TypeByName("GridInventory");
            if (gridType is null) throw new TypeLoadException("GridInventory");
            var postfix = new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(OnLocalArtifactAdded));
            foreach (var methodName in new[] { "LocalAddItem", "LocalAddItemAtPosition", "AddItemToSubBagWithNotify" })
            {
                var methods = gridType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(x => x.Name == methodName && x.GetParameters().Any(p => p.Name == "isReward"));
                foreach (var method in methods) _harmony.Patch(method, postfix: postfix);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("획득 이벤트 패치를 설치하지 못했습니다. 인벤토리 최소치만 사용합니다: " + ex.Message);
        }
    }

    private static void OnLocalArtifactAdded(object __instance, int instanceID, int entityID, bool isReward, object __result)
    {
        var plugin = _instance;
        if (plugin?._state is null || !isReward || !WasAdditionSuccessful(__result) || !plugin._gateway.IsLocalInventory(__instance)) return;
        var catalogKey = plugin._catalog.Entries.FirstOrDefault(x => x.Kind == CatalogKind.Artifact && x.GameKey == entityID.ToString())?.GameKey;
        if (catalogKey is null) return;
        var eventKey = plugin._state.RunId + ":" + Time.frameCount + ":" + instanceID;
        if (plugin._seenRewardInstances.Count > 2048) plugin._seenRewardInstances.Clear();
        if (!plugin._seenRewardInstances.Add(eventKey)) return; // overlapping add methods report the same reward once
        plugin._state.RecordArtifact(catalogKey, ArtifactProgressEvent.RewardAcquired);
        try { new ActiveStateStore().Save(plugin._state); } catch (Exception ex) { plugin.Logger.LogWarning(ex); }
    }

    private static bool WasAdditionSuccessful(object result)
    {
        if (result is bool boolean) return boolean;
        try
        {
            var code = Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
            return code is 0 or 1; // ItemAdditionCheckResult.Success / Success_Stack
        }
        catch { return false; }
    }

    private void OnGUI()
    {
        _gateway.DrawHighlight();
        if (_showImport) _importRect = GUI.Window(761331, _importRect, DrawImportWindow, "Sephiria 빌드 가져오기 / 검토 (F6)");
        if (_showOverlay && _plan is not null) _overlayRect = GUI.Window(761332, _overlayRect, DrawOverlayWindow, "Sephiria 빌드 가이드 (F7)");
    }

    private void DrawImportWindow(int windowId)
    {
        GUILayout.BeginVertical();
        GUILayout.Label("sephiria.wiki 빌드 URL 또는 UUID");
        GUILayout.BeginHorizontal();
        _locatorText = GUILayout.TextField(_locatorText, GUILayout.ExpandWidth(true));
        GUI.enabled = !_importing;
        if (GUILayout.Button("가져오기", GUILayout.Width(90))) _ = ImportAsync();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label(_status);

        if (_review is not null)
        {
            GUILayout.Label($"빌드: {_review.Build.Title} / 버전 {_review.Build.GameVersion} / 현재 게임 {Application.version}");
            _reviewScroll = GUILayout.BeginScrollView(_reviewScroll, GUILayout.Height(500));
            foreach (var section in _review.Sections)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label(section.Source.Label, GUILayout.Width(300));
                if (GUILayout.Button(RoleLabel(section.Role), GUILayout.Width(100))) section.Role = NextRole(section.Role);
                if (GUILayout.Button("우선 -", GUILayout.Width(65))) section.Priority--;
                if (GUILayout.Button("우선 +", GUILayout.Width(65))) section.Priority++;
                GUILayout.Label(section.Priority.ToString(), GUILayout.Width(30));
                GUILayout.EndHorizontal();
                if (!string.IsNullOrWhiteSpace(section.Source.Description)) GUILayout.Label(section.Source.Description);
                foreach (var item in section.Items)
                {
                    var key = item.ManualCatalogKey ?? item.Source.Slug;
                    _review.Bindings.TryGetValue(key, out var binding);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"• {item.Source.Slug}", GUILayout.Width(230));
                    if (GUILayout.Button(RoleLabel(item.RoleOverride ?? section.Role), GUILayout.Width(90)))
                        item.RoleOverride = NextRole(item.RoleOverride ?? section.Role);
                    if (GUILayout.Button("-", GUILayout.Width(28))) item.DesiredAcquisitions = Math.Max(1, item.DesiredAcquisitions - 1);
                    GUILayout.Label($"x{item.DesiredAcquisitions}", GUILayout.Width(35));
                    if (GUILayout.Button("+", GUILayout.Width(28))) item.DesiredAcquisitions++;
                    if (GUILayout.Button("P-", GUILayout.Width(32))) item.PriorityOverride = (item.PriorityOverride ?? section.Priority) - 1;
                    if (GUILayout.Button("P+", GUILayout.Width(32))) item.PriorityOverride = (item.PriorityOverride ?? section.Priority) + 1;
                    var oldColor = GUI.color;
                    GUI.color = binding?.Status == BindingStatus.Verified ? Color.green : Color.yellow;
                    GUILayout.Label(binding?.Status.ToString() ?? "미해결", GUILayout.Width(130));
                    GUI.color = oldColor;
                    GUILayout.EndHorizontal();
                    if (binding is null || binding.Status != BindingStatus.Verified)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(20);
                        GUILayout.Label("매핑 수정(카탈로그 slug):", GUILayout.Width(170));
                        var edited = GUILayout.TextField(item.ManualCatalogKey ?? item.Source.Slug);
                        item.ManualCatalogKey = string.IsNullOrWhiteSpace(edited) || edited == item.Source.Slug ? null : edited.Trim();
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("매핑 재검증")) _review.VerifyBindings(_gateway.DiscoverCatalogEntities());
            if (GUILayout.Button("검토 완료 및 활성화")) ActivateReviewedBuild();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0, 0, 10000, 24));
    }

    private void DrawOverlayWindow(int windowId)
    {
        if (_plan is null || _state is null) return;
        GUILayout.BeginVertical();
        GUILayout.Label(_status);
        foreach (var target in _plan.Artifacts.OrderBy(x => x.Priority).Take(12))
        {
            var count = _state.EffectiveAcquisitions(target.CatalogKey);
            _state.Artifacts.TryGetValue(target.CatalogKey, out var progress);
            GUILayout.BeginHorizontal();
            var targetName = _catalog.Entries.FirstOrDefault(x => x.Kind == CatalogKind.Artifact && x.GameKey == target.CatalogKey)?.KoreanName ?? target.CatalogKey;
            GUILayout.Label($"[{(target.Role == TargetRole.Required ? "필수" : "추천")}] {targetName}: {count}/{target.DesiredAcquisitions}" +
                            (progress?.IsUncertain == true ? " (최소/기록 불확실)" : string.Empty));
            if (GUILayout.Button("-", GUILayout.Width(26))) AdjustProgress(target.CatalogKey, -1);
            if (GUILayout.Button("+", GUILayout.Width(26))) AdjustProgress(target.CatalogKey, 1);
            GUILayout.EndHorizontal();
        }
        if (_recommendation is not null)
        {
            GUILayout.Space(8);
            GUILayout.Label("다음 행동: " + _recommendation.Message);
            if (_recommendation.Action is { } action)
            {
                var diceBefore = _lastSnapshot?.SharedDice ?? 0;
                var moneyBefore = _lastSnapshot?.Money ?? 0;
                GUILayout.Label($"비용: 돈 {action.MoneyCost} ({moneyBefore} → {moneyBefore - action.MoneyCost}), " +
                                $"공유 주사위 {action.DiceCost} ({diceBefore} → {diceBefore - action.DiceCost})");
                if (action.DiceRisk != DiceRisk.None)
                {
                    var old = GUI.color; GUI.color = Color.red;
                    GUILayout.Label(action.DiceRisk == DiceRisk.LastSharedDieSpentBeforeMiracle
                        ? "이 행동 후 나무 뿌리 리롤 불가"
                        : "목표 나무 뿌리 획득 전 공유 주사위가 감소합니다.");
                    GUI.color = old;
                }
                GUILayout.Label($"{_confirmKey.Value}: 이 동작 하나 확인");
            }
        }
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0, 0, 10000, 24));
    }

    private static string RoleLabel(TargetRole role) => role switch
    {
        TargetRole.Required => "필수",
        TargetRole.Recommended => "추천",
        TargetRole.Excluded => "제외",
        _ => "미분류"
    };

    private static TargetRole NextRole(TargetRole role) => role switch
    {
        TargetRole.Unclassified => TargetRole.Required,
        TargetRole.Required => TargetRole.Recommended,
        TargetRole.Recommended => TargetRole.Excluded,
        _ => TargetRole.Required
    };

    private void AdjustProgress(string catalogKey, int delta)
    {
        if (_state is null) return;
        _state.Artifacts.TryGetValue(catalogKey, out var progress);
        _state.SetUserAdjustment(catalogKey, (progress?.UserAdjustment ?? 0) + delta);
        try { new ActiveStateStore().Save(_state); } catch (Exception ex) { Logger.LogWarning(ex); }
    }
}
