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
public sealed partial class SephiriaBuildOverlayPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "io.github.sephiria.build-overlay";
    public const string PluginName = "Sephiria Build Overlay";
    public const string PluginVersion = "0.1.5";

    private ConfigEntry<KeyCode> _importKey = null!;
    private ConfigEntry<KeyCode> _overlayKey = null!;
    private ConfigEntry<KeyCode> _confirmKey = null!;
    private ConfigEntry<string> _padModifier = null!;
    private readonly ControllerInputBridge _controller = new(AccessTools.TypeByName);
    private readonly ControllerInputState _controllerState = new();
    private readonly ControllerMenu _controllerMenu = new();
    private int _controllerModalThroughFrame = -1;
    private bool _controllerModalGateReady;
    private string ImportPrompt => _controller.GamepadMode ? _controller.Prompt("↑") : _importKey.Value.ToString();
    private string OverlayPrompt => _controller.GamepadMode ? _controller.Prompt("←") : _overlayKey.Value.ToString();
    private string ConfirmPrompt => _controller.GamepadMode ? _controller.Prompt("→") : _confirmKey.Value.ToString();
    private ConfigEntry<bool> _acceptVersionMismatch = null!;
    private ConfigEntry<bool> _exportRuntimeCatalog = null!;
    private ConfigEntry<float> _uiScale = null!;
    private ConfigEntry<float> _ghostOpacity = null!;
    private ConfigEntry<bool> _measurePerformance = null!;
    private ConfigEntry<bool> _runtimeDiagnostics = null!;
    private RuntimeDiagnostics _diagnostics = null!;
    private ReviewCheckpoint? _pendingRestore;
    private readonly ReviewCheckpointStore _reviewStore = new();
    private float _nextProbeAt;
    private float _nextPerformanceLog;
    private WikiBuildSource _source = null!;
    private VersionedCatalog _catalog = null!;
    private UnityGameGateway _gateway = null!;
    private ConfirmedActionExecutor _executor = null!;
    private BuildReviewSession? _review;
    private BuildPlan? _plan;
    private ActiveBuildState? _state;
    private Recommendation? _recommendation;
    private RecommendationEngine? _recommendationEngine;
    private bool _showImport;
    private bool _showOverlay = true;
    private string _locatorText = string.Empty;
    private string _status = "빌드 가져오기/검토 창에서 목표를 설정하세요.";
    private Vector2 _reviewScroll;
    private Rect _importRect = new(180, 35, 920, 650);
    private Rect _overlayRect = new(18, 184, 440, 450);
    private readonly object _uiStateGate = new();
    private bool _importing;
    private bool _executing;
    private Harmony? _harmony;
    private static SephiriaBuildOverlayPlugin? _instance;
    private readonly HashSet<string> _seenRewardInstances = new(StringComparer.Ordinal);
    private float _nextSnapshotAt;
    private RunSnapshot? _lastSnapshot;
    private bool _updateObserved;
    private bool _guiObserved;
    private readonly ShortcutLatch _shortcutLatch = new();
    private bool _importToggleRequested;
    private bool _overlayToggleRequested;
    private bool _confirmRequested;
    private bool _pointerOverOverlay;

    private void Awake()
    {
        _importKey = Config.Bind("Keys", "ImportWindow", KeyCode.F6, "빌드 가져오기/검토 창");
        _overlayKey = Config.Bind("Keys", "Overlay", KeyCode.F7, "인게임 오버레이 전환");
        _confirmKey = Config.Bind("Keys", "ConfirmOneAction", KeyCode.F8, "추천 동작 하나 확인");
        _padModifier = Config.Bind("Gamepad", "Modifier", "selectButton", new ConfigDescription(
            "게임 패드 모드에서 이 버튼을 누른 채 방향키 ↑ 검토 / ← 표시 / → 한 동작 확인. 기본 View/Back/Share/−. 네이티브 입력은 검토 창 밖에서 차단하지 않음",
            new AcceptableValueList<string>("selectButton", "leftStickButton", "rightStickButton")));
        _acceptVersionMismatch = Config.Bind("Safety", "AcceptVersionMismatch", false, "빌드/게임 버전 불일치 경고를 확인한 것으로 처리");
        _exportRuntimeCatalog = Config.Bind("Debug", "ExportRuntimeCatalog", false, "게임 엔티티 메타데이터를 로컬 JSON으로 내보내기 (카탈로그 디버깅용)");
        _measurePerformance = Config.Bind("Debug", "MeasurePerformance", false, "10초마다 읽기 전용 스냅샷 비용과 포커스 상태 FPS 기록 (게임 행동 없음)");
        _runtimeDiagnostics = Config.Bind("Debug", "RuntimeDiagnostics", false, "개발용 로컬 파일 명령의 읽기 전용 snapshot/catalog 검증 허용. 게임 행동은 실행하지 않음");
        _diagnostics = new RuntimeDiagnostics(Path.Combine(Paths.BepInExRootPath, "cache", "SephiriaBuildOverlay", "diagnostics"));
        _source = new WikiBuildSource();
        _uiScale = Config.Bind("UI", "Scale", 1f, new ConfigDescription("가이드 글꼴/창 크기 배율", new AcceptableValueRange<float>(0.75f, 1.75f)));
        _ghostOpacity = Config.Bind("UI", "GhostOpacity", .4f, new ConfigDescription("인벤토리 목표 배치 고스트 불투명도", new AcceptableValueRange<float>(.15f, .7f)));
        _catalog = VersionedCatalog.LoadEmbedded("1.0.33");
        _gateway = new UnityGameGateway(_catalog, Logger, _exportRuntimeCatalog.Value);
        _gateway.Performance.Enabled = _measurePerformance.Value;
        _nextPerformanceLog = Time.unscaledTime + 10f;
        _executor = new ConfirmedActionExecutor(_gateway);
        _instance = this;
        InstallProgressPatches();
        try { _pendingRestore = _reviewStore.Load(); }
        catch (Exception ex) { Logger.LogWarning("Saved review could not be loaded: " + ex.Message); }
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Game={Application.version}, PID={System.Diagnostics.Process.GetCurrentProcess().Id}");
    }

    private void OnDestroy()
    {
        _source.Dispose();
        _gateway.Dispose();
        _harmony?.UnpatchSelf();
        _instance = null;
        _theme?.Dispose();
    }

    private void Update()
    {
        if (!_updateObserved) { _updateObserved = true; Logger.LogInfo("Update callback active."); }
        HandleControllerInput();
        _gateway.Performance.Enabled = _measurePerformance.Value;
        var pendingSnapshot = _gateway.Tick();
        if (_runtimeDiagnostics.Value) _diagnostics.Tick(Time.unscaledTime, _gateway,
            () => new { activeBuild = _plan?.SourceBuildId, importVisible = _showImport, overlayVisible = _showOverlay, recommendation = _recommendation, status = _status, checkpointPath = _reviewStore.CheckpointPath, ghosts = _gateway.GhostCount,
                input = new { scheme = _controller.Scheme, pairedDevice = _controller.DeviceId, pairedGamepads = _controller.PairedGamepads, modalGateReady = _controllerModalGateReady, confirm = ConfirmPrompt, import = ImportPrompt, overlay = OverlayPrompt } }, PreviewControllerReview);
        if (_measurePerformance.Value)
        {
            _gateway.Performance.Frame(Time.unscaledDeltaTime, Application.isFocused);
            if (_plan is null && !_showOverlay && Time.unscaledTime >= _nextProbeAt)
            {
                _nextProbeAt = Time.unscaledTime + .2f;
                _lastSnapshot = pendingSnapshot ?? _gateway.CaptureOnMainThread();
            }
            if (Time.unscaledTime >= _nextPerformanceLog)
            {
                _nextPerformanceLog = Time.unscaledTime + 10f;
                Logger.LogInfo(_gateway.Performance.ReportAndReset(_gateway.DiscoveryPasses, _gateway.ReflectionTypes));
            }
        }
        if (WindowsKeyState.IsReleased(_importKey.Value)) _shortcutLatch.Release((int)_importKey.Value);
        if (WindowsKeyState.IsReleased(_overlayKey.Value)) _shortcutLatch.Release((int)_overlayKey.Value);
        if (WindowsKeyState.IsReleased(_confirmKey.Value)) _shortcutLatch.Release((int)_confirmKey.Value);
        if (_importToggleRequested)
        {
            _importToggleRequested = false;
            _showImport = !_showImport;
            Logger.LogInfo($"Import key received. Visible={_showImport}");
        }
        if (_overlayToggleRequested) { _overlayToggleRequested = false; _showOverlay = !_showOverlay; }

        if (!_importing && _plan is null && _showOverlay && Time.unscaledTime >= _nextSnapshotAt)
        {
            _lastSnapshot = pendingSnapshot ?? _gateway.CaptureOnMainThread();
            _nextSnapshotAt = Time.unscaledTime + (_lastSnapshot.Screen == ScreenKind.None ? .5f : .1f);
        }
        if (_pendingRestore is not null && _lastSnapshot?.IsLocalPlayerOwned == true)
        {
            var saved = _pendingRestore; _pendingRestore = null;
            try
            {
                _review = saved.Restore(_catalog);
                if (saved.WasActivated) ActivateReviewedBuild();
                else _status = "이전 검토를 복원했습니다. 분류와 매핑 확인 후 활성화하세요.";
                Logger.LogInfo($"Saved review restored: {saved.Build.Id}, active={_plan is not null}");
            }
            catch (Exception ex) { _status = "이전 빌드 복원 실패: " + ex.Message; Logger.LogWarning(ex); }
        }

        if (!_importing && _plan is not null && _state is not null && Time.unscaledTime >= _nextSnapshotAt)
        {
            var snapshot = pendingSnapshot ?? _gateway.CaptureOnMainThread();
            // Menus remain responsive; exploration and a hidden guide do not
            // need ten expensive observations a second. F8 always reads afresh.
            _nextSnapshotAt = Time.unscaledTime + (snapshot.Screen == ScreenKind.None || !_showOverlay || _showImport ? .5f : .1f);
            _lastSnapshot = snapshot;
            if (_state.RunId != snapshot.RunId)
            {
                _state = ActiveBuildState.Activate(_plan, snapshot);
                _status = "새 런을 감지해 진행 상태를 분리했습니다.";
            }
            _recommendation = snapshot.Screen == ScreenKind.Inventory ? _gateway.RecommendPlacement(snapshot) : _recommendationEngine!.Recommend(_plan, _state, snapshot);
            if (!string.IsNullOrEmpty(_plan.MiracleTarget) && snapshot.MiracleKeys.Contains(_plan.MiracleTarget!))
                _state.MarkMiracleAcquired();
            _gateway.SetHighlight(_recommendation.Action?.TargetToken);
        }

        var confirmed = _confirmRequested;
        _confirmRequested = false;
        if (confirmed && (_plan is null || _state is null)) Logger.LogInfo("Confirmation ignored: no active reviewed build.");
        if (confirmed && _showOverlay && !_gateway.IsGhostPreview && !ControllerReviewPreview && !_showImport && !_importing && !_executing && _plan is not null && _state is not null && _recommendation?.Action is not null)
            _ = ConfirmCurrentAsync(_recommendation.Action);
    }

    private async Task ConfirmCurrentAsync(RecommendedAction action)
    {
        _executing = true;
        try
        {
            var result = await _executor.ConfirmOnceAsync(action);
            Logger.LogInfo($"Confirmed action result: kind={action.Kind}, result={result.Status}, message={result.Message}");
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
        if (_importing || _executing) return;
        if (!BuildLocator.TryParse(_locatorText, out var locator, out var error))
        {
            _status = error!;
            return;
        }
        _importing = true;
        _recommendation = null;
        _gateway.SetHighlight(null);
        _status = "Wiki에서 빌드를 가져오는 중...";
        try
        {
            Logger.LogInfo($"Import started: {locator!.Id}");
            // Mono's HTTP handler may perform synchronous DNS/TLS work before its first await.
            // Keep that work off the Unity main thread; resume here for Unity-only inspection.
            var result = await Task.Run(() => _source.ImportAsync(locator!));
            Logger.LogInfo($"Import response parsed: sections={result.Build.Sections.Count}, origin={result.Origin}");
            var review = new BuildReviewSession(result.Build, _catalog);
            // Unity objects must be inspected on the main thread. ContinueWith is avoided; Unity's context returns here.
            review.VerifyBindings(_gateway.DiscoverCatalogEntities());
            var bindingSummary = string.Join(", ", review.Bindings.Values.GroupBy(x => x.Status)
                .Select(x => $"{x.Key}={x.Count()}"));
            Logger.LogInfo($"Import catalog verification complete: bindings={review.Bindings.Count}, {bindingSummary}");
            lock (_uiStateGate)
            {
                _review = review;
                _pendingRestore = null;
                _plan = null;
                _state = null;
                _recommendation = null;
                _recommendationEngine = null;
                _gateway.SetPlacementPlan(null);
                _lastSnapshot = null;
                _status = result.Warning ?? $"'{result.Build.Title}' 가져오기 완료 ({result.Origin}). 모든 구역을 분류하세요.";
            }
            try { _reviewStore.Save(ReviewCheckpoint.Capture(review, false)); }
            catch (Exception ex) { Logger.LogWarning("Review checkpoint save failed: " + ex.Message); }
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
            var plan = _review.CreatePlan(Application.version, _acceptVersionMismatch.Value);
            var snapshot = _gateway.CaptureOnMainThread();
            var store = new ActiveStateStore();
            ActiveBuildState? existing = null;
            try { existing = store.Load(plan.SourceBuildId, snapshot.RunId); }
            catch (Exception ex) { Logger.LogWarning("Saved progress unavailable; using uncertain inventory minimum: " + ex.Message); }
            var state = ActiveBuildState.Activate(plan, snapshot, existing);
            var bindings = BindingsByGameKey();
            _plan = plan; _state = state;
            _recommendationEngine = new RecommendationEngine(bindings);
            _gateway.SetPlacementPlan(plan, bindings);
            try { store.Save(state); } catch (Exception ex) { Logger.LogWarning("Progress cache save failed: " + ex.Message); }
            try { _reviewStore.Save(ReviewCheckpoint.Capture(_review, true)); }
            catch (Exception ex) { Logger.LogWarning("Active review checkpoint save failed: " + ex.Message); }
            _status = $"빌드 활성화 완료. 필수 목표 {_plan.Artifacts.Count(x => x.Role == TargetRole.Required)}종.";
            _showImport = false;
        }
        catch (Exception ex) { _status = "활성화 불가: " + ex.Message; }
    }

    private IReadOnlyDictionary<string, CatalogBinding> BindingsByGameKey()
    {
        if (_review is null) return new Dictionary<string, CatalogBinding>();
        return _review.Bindings.Values.Where(x => x.GameKey is not null)
            .GroupBy(x => x.GameKey!, StringComparer.Ordinal).ToDictionary(x => x.Key,
                x => x.FirstOrDefault(binding => !binding.AllowsAutomaticAction) ?? x.First(), StringComparer.Ordinal);
    }

    private void InstallProgressPatches()
    {
        try
        {
            _harmony = new Harmony(PluginGuid + ".progress");
            var inputType = AccessTools.TypeByName("PlayerInputController");
            var inputGate = inputType is null ? null : AccessTools.PropertyGetter(inputType, "BlockAvatarInput");
            if (inputGate is not null)
                _harmony.Patch(inputGate, postfix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(ApplyReviewInputGate)));
            // Controller submit must not activate an underlying native button
            // while the modal review owns it. No native input is intercepted
            // outside that modal (including warnings about shared dice).
            InstallControllerModalPatches();
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
        plugin._gateway.RecordReward(catalogKey);
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

    private static void ApplyReviewInputGate(ref bool __result)
    {
        // Keep the game's existing avatar/combat gate. Native controller UI
        // is isolated only while the modal review owns input; ordinary game
        // screens and dice-consumption buttons are not intercepted there.
        __result |= _instance is { } plugin && (plugin._showImport || plugin.ControllerModalOwnsInput ||
            (plugin._showOverlay && plugin._plan is not null && plugin._pointerOverOverlay));
    }

    private bool ControllerModalOwnsInput => _controller.GamepadMode &&
        (_showImport || Time.frameCount <= _controllerModalThroughFrame);
    private static bool AllowNativeModalInput() => _instance is not { } plugin || !plugin.ControllerModalOwnsInput;

    private void InstallControllerModalPatches()
    {
        try
        {
            foreach (var (typeName, methodName) in new[] {
                ("UnityEngine.InputSystem.UI.InputSystemUIInputModule", "Process"), ("UIInputModule", "Update") })
            {
                var type = AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName);
                var method = AccessTools.Method(type, methodName) ?? throw new MissingMethodException(typeName, methodName);
                _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(AllowNativeModalInput)));
            }
            _controllerModalGateReady = true;
        }
        catch (Exception ex) { Logger.LogWarning("Controller review input disabled: " + ex.Message); }
    }

    private void HandleControllerInput()
    {
        if (_controllerReviewPreviewActive && !ControllerReviewPreview)
        {
            _controllerReviewPreviewActive = false; _showImport = _controllerReviewPreviewWasOpen; _controllerMenu.Reset();
        }
        var previousMode = _controller.GamepadMode;
        _controller.Capture(_padModifier.Value);
        _gateway.SetControllerMode(_controller.GamepadMode);
        if (_showImport && _controller.GamepadMode) _controllerModalThroughFrame = Time.frameCount + 1;
        if (previousMode != _controller.GamepadMode) _controllerMenu.Reset();
        var command = _controllerState.Update(_controller.GamepadMode, Application.isFocused,
            _controller.DeviceId, _controller.Buttons, _showImport);
        if (ControllerReviewPreview) return; // UI-only diagnostics can never confirm or edit.
        switch (command)
        {
            case PadCommand.Import: _importToggleRequested = true; _controllerMenu.Reset(); break;
            case PadCommand.Overlay: _overlayToggleRequested = true; break;
            case PadCommand.Confirm: if (!_executing && !_importing && !_showImport) _confirmRequested = true; break;
            case PadCommand.Previous: _controllerMenu.Navigate(-1); break;
            case PadCommand.Next: _controllerMenu.Navigate(1); break;
            case PadCommand.Submit: if (_controllerModalGateReady && !_importing && !_executing) _controllerMenu.Activate(); break;
            case PadCommand.Cancel: if (_controllerModalGateReady) { _showImport = false; _controllerMenu.Reset(); } break;
        }
    }

    private void HandleShortcutEvent()
    {
        // IMGUI key events work with Unity 6's Input System without calling the disabled legacy Input API.
        var keyEvent = Event.current;
        if (keyEvent.type == EventType.KeyUp) _shortcutLatch.Release((int)keyEvent.keyCode);
        if (keyEvent.type == EventType.KeyDown && Application.isFocused && keyEvent.keyCode != KeyCode.None)
        {
            if (keyEvent.keyCode == _importKey.Value && _shortcutLatch.Press((int)keyEvent.keyCode)) _importToggleRequested = true;
            else if (keyEvent.keyCode == _overlayKey.Value && _shortcutLatch.Press((int)keyEvent.keyCode)) _overlayToggleRequested = true;
            else if (keyEvent.keyCode == _confirmKey.Value && _shortcutLatch.Press((int)keyEvent.keyCode)) _confirmRequested = true;
        }
    }

    private void OnGUI()
    {
        _gateway.SetBoardPointer(Event.current.mousePosition, !_controller.GamepadMode && Event.current.shift);
        HandleShortcutEvent();
        if (!_guiObserved) { _guiObserved = true; Logger.LogInfo("OnGUI callback active."); }
        DrawStyledWindows();
        _gateway.SetItemFont(_theme?.Skin.font);
        if (Event.current.type == EventType.Repaint)
        {
            _gateway.BeginNativeOverlay(_showOverlay && !_showImport);
            if (_showOverlay && !_showImport)
            {
                _gateway.DrawPlacementGhosts(_ghostOpacity.Value, _uiScale.Value);
                _gateway.DrawItemOverlay(_plan, _state, _recommendation?.Action, ConfirmPrompt, ImportPrompt, _uiScale.Value);
                _gateway.EndNativeOverlay();
            }
        }
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
