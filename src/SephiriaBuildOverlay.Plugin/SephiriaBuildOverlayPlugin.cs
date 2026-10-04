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
    public const string PluginVersion = "0.1.23";

    private ConfigEntry<KeyCode> _importKey = null!;
    private ConfigEntry<KeyCode> _overlayKey = null!;
    private ConfigEntry<KeyCode> _confirmKey = null!;
    private ConfigEntry<string> _padModifier = null!;
    private readonly ControllerInputBridge _controller = new(AccessTools.TypeByName);
    private readonly ControllerInputState _controllerState = new();
    private readonly ControllerMenu _controllerMenu = new();
    private readonly ModalInputCapture _modalInput = new();
    private readonly PluginLifetime _lifetime = new();
    private bool _quitting;
    private GameObject? _nativeFocusBeforeModal;
    private bool _controllerModalGateReady;
    private string ImportPrompt => _controller.GamepadMode ? _controller.Prompt("↑") : _importKey.Value.ToString();
    private string OverlayPrompt => _controller.GamepadMode ? _controller.Prompt("←") : _overlayKey.Value.ToString();
    private string ConfirmPrompt => _controller.GamepadMode ? _controller.Prompt("→") : _confirmKey.Value.ToString();
    private ConfigEntry<bool> _exportRuntimeCatalog = null!;
    private ConfigEntry<float> _uiScale = null!;
    private ConfigEntry<float> _panelScale = null!;
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
    private bool _showImport
    {
        get => _modalInput.Visible;
        set
        {
            if (_lifetime.Stopped || value == _modalInput.Visible) return;
            if (value && !_controllerModalGateReady)
            {
                Logger.LogWarning("Review window blocked: exclusive native input gate unavailable.");
                Notify("게임 입력을 분리하지 못해 창을 열 수 없습니다.", NotificationKind.Error);
                return;
            }
            _modalInput.SetVisible(value);
            if (value) StopPlacement("자동배치 중단: 빌드 창이 열렸습니다.");
            if (!value) _nativeBuildWindow?.Hide();
            if (!value) ReleaseSystemCursor();
            _confirmRequested = false;
            ResetNativeUiInput(rememberSelection: value);
        }
    }
    private bool _showOverlay = true;
    private string _locatorText = string.Empty;
    private string _status = "빌드 링크를 불러오면 모든 항목을 추천 목표로 안내합니다.";
    private Vector2 _reviewScroll;
    private Rect _importRect = new(180, 35, 650, 450);
    private readonly object _uiStateGate = new();
    private bool _importing;
    private bool _executing;
    private Harmony? _harmony;
    private static SephiriaBuildOverlayPlugin? _instance;
    private readonly AcquisitionTracker _acquisitions = new();
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
        // An unrecoverable filesystem failure must not run a mixed DLL pair.
        if (File.Exists(Path.Combine(Paths.BepInExRootPath, "cache", "SephiriaBuildOverlayUpdater", "transaction", "journal.txt")))
        {
            Logger.LogError("Overlay disabled: unfinished update transaction. Close the game and reinstall the complete latest package.");
            _lifetime.Dispose();
            enabled = false;
            return;
        }
        _importKey = Config.Bind("Keys", "ImportWindow", KeyCode.F9, "빌드 가져오기/검토 창 (SephPlanner F6 충돌 방지)");
        var migrated = Config.Bind("Keys", "ImportF9MigrationApplied", false, "기존 기본 F6을 F9로 한 번 이전. 이후 사용자 지정은 보존");
        _importKey.Value = (KeyCode)ImportShortcutMigration.Migrate((int)_importKey.Value, (int)KeyCode.F6, (int)KeyCode.F9, migrated.Value);
        if (!migrated.Value) { migrated.Value = true; Config.Save(); }
        _overlayKey = Config.Bind("Keys", "Overlay", KeyCode.F7, "인게임 오버레이 전환");
        _confirmKey = Config.Bind("Keys", "ConfirmOneAction", KeyCode.F8, "추천 동작 확인 / 인벤토리 전체 자동배치 시작·중단");
        _padModifier = Config.Bind("Gamepad", "Modifier", "selectButton", new ConfigDescription(
            "게임 패드 모드에서 이 버튼을 누른 채 방향키 ↑ 검토 / ← 표시 / → 한 동작 확인. 기본 View/Back/Share/−. 네이티브 입력은 검토 창 밖에서 차단하지 않음",
            new AcceptableValueList<string>("selectButton", "leftStickButton", "rightStickButton")));
        _exportRuntimeCatalog = Config.Bind("Debug", "ExportRuntimeCatalog", false, "게임 엔티티 메타데이터를 로컬 JSON으로 내보내기 (카탈로그 디버깅용)");
        _measurePerformance = Config.Bind("Debug", "MeasurePerformance", false, "10초마다 읽기 전용 스냅샷 비용과 포커스 상태 FPS 기록 (게임 행동 없음)");
        _runtimeDiagnostics = Config.Bind("Debug", "RuntimeDiagnostics", false, "개발용 로컬 파일 명령의 읽기 전용 snapshot/catalog 검증 허용. 게임 행동은 실행하지 않음");
        _diagnostics = new RuntimeDiagnostics(Path.Combine(Paths.BepInExRootPath, "cache", "SephiriaBuildOverlay", "diagnostics"));
        _source = new WikiBuildSource();
        _uiScale = Config.Bind("UI", "Scale", 1f, new ConfigDescription("가이드 글꼴/창 크기 배율", new AcceptableValueRange<float>(0.75f, 1.75f)));
        _panelScale = Config.Bind("UI", "PanelScale", 1f, new ConfigDescription("기본 패널 전용 배율. 마우스로 오른쪽 아래 모서리를 드래그하면 저장됨", new AcceptableValueRange<float>(PanelResizeState.MinimumScale, PanelResizeState.MaximumScale)));
        _ghostOpacity = Config.Bind("UI", "GhostOpacity", .4f, new ConfigDescription("인벤토리 목표 배치 고스트 불투명도", new AcceptableValueRange<float>(.15f, .7f)));
        _catalog = VersionedCatalog.LoadEmbedded("1.0.33");
        _gateway = new UnityGameGateway(_catalog, Logger, _exportRuntimeCatalog.Value);
        _gateway.LocalContextChanged += OnLocalContextChanged;
        _gateway.Performance.Enabled = _measurePerformance.Value;
        _nextPerformanceLog = Time.unscaledTime + 10f;
        _executor = new ConfirmedActionExecutor(_gateway);
        _instance = this;
        InstallProgressPatches();
        try { _pendingRestore = _reviewStore.Load(); }
        catch (Exception ex) { Logger.LogWarning("Saved review could not be loaded: " + ex.Message); }
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Game={Application.version}, PID={System.Diagnostics.Process.GetCurrentProcess().Id}");
        StartAutomaticUpdates();
    }

    private void OnApplicationQuit()
    {
        if (_quitting || _lifetime.Stopped) return;
        _quitting = true;
        // Dump 47392: CleanupModule_Accessibility pumps WM_ACTIVATE after
        // NewInput has shut down. Disconnect providers while input is alive.
        WindowsExitCleanup.DisconnectProviders(Logger.LogInfo, Logger.LogWarning);
        Shutdown();
    }

    private void OnDestroy() => Shutdown();
    private void OnApplicationFocus(bool focused) { if (!focused) { _nativeBuildWindow?.CancelResize(); StopPlacement("자동배치 중단: 게임 포커스를 잃었습니다."); } }

    private void Shutdown()
    {
        if (_lifetime.Stopped) return;
        _nativeBuildWindow?.Dispose(destroyUnityObjects: !_quitting);
        _nativeBuildWindow = null;
        _notifications.Clear();
        ReleaseSystemCursor();
        if (!_quitting && _modalInput.Capturing) ResetNativeUiInput(restoreSelection: true);
        _instance = null; // Patches become inert BEFORE cancellation continuations.
        _lifetime.Stop();
        StopAutomaticUpdates();
        _confirmRequested = false;
        StopPlacement("게임 종료");
        _source?.Dispose();
        _gateway?.Dispose(destroyUnityObjects: !_quitting);
        // Live ScriptEngine unload still unpatches. Process shutdown must not
        // rewrite methods/detours while Unity is dismantling the runtime.
        if (!_quitting) _harmony?.UnpatchSelf();
        _theme?.Dispose(destroyUnityObjects: !_quitting);
        _theme = null;
        _lifetime.Dispose();
        Logger.LogInfo("Overlay shutdown complete; pending work cancelled. Quit=" + _quitting);
    }

    private void Update()
    {
        if (_quitting || _lifetime.Stopped) return;
        if (!_updateObserved) { _updateObserved = true; Logger.LogInfo("Update callback active."); }
        _notifications.Advance(Time.unscaledDeltaTime, Application.isFocused);
        TickAutomaticUpdates();
        TickTitleExitSmoke();
        HandleControllerInput();
        _gateway.Performance.Enabled = _measurePerformance.Value;
        var pendingSnapshot = _gateway.Tick();
        if (_runtimeDiagnostics.Value) _diagnostics.Tick(Time.unscaledTime, _gateway,
            () => new { activeBuild = _plan?.SourceBuildId, progress = _state, placement = new { active = _placementBatch.Active, completed = _placementBatch.CompletedSteps, reason = _placementBatch.EndReason }, importVisible = _showImport, overlayVisible = _showOverlay, recommendation = _recommendation, status = _status, checkpointPath = _reviewStore.CheckpointPath, ghosts = _gateway.GhostCount,
                input = new { scheme = _controller.Scheme, pairedDevice = _controller.DeviceId, pairedGamepads = _controller.PairedGamepads, modalGateReady = _controllerModalGateReady, modalCapturing = _modalInput.Capturing, confirm = ConfirmPrompt, import = ImportPrompt, overlay = OverlayPrompt } }, PreviewControllerReview);
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
        if (_overlayToggleRequested) { _overlayToggleRequested = false; _showOverlay = !_showOverlay; if (!_showOverlay) StopPlacement("자동배치 중단: 오버레이가 꺼졌습니다."); }

        if (!_importing && _plan is null && _showOverlay && Time.unscaledTime >= _nextSnapshotAt)
        {
            _lastSnapshot = pendingSnapshot ?? _gateway.CaptureOnMainThread();
            _nextSnapshotAt = Time.unscaledTime + (_lastSnapshot.Screen == ScreenKind.None ? .5f : .1f);
        }
        if (!_importing && _pendingRestore is not null && _lastSnapshot?.IsLocalPlayerOwned == true)
        {
            var saved = _pendingRestore; _pendingRestore = null;
            try
            {
                _review = saved.Restore(_catalog);
                if (saved.WasActivated)
                {
                    ActivateReviewedBuild();
                    var context = _gateway.StartingPresetContext();
                    if (_plan is not null && context is not null) QueueStartingPreset(saved.Build, context, false);
                }
                else _status = "이전 검토를 복원했습니다. 분류와 매핑 확인 후 활성화하세요.";
                Logger.LogInfo($"Saved review restored: {saved.Build.Id}, active={_plan is not null}");
            }
            catch (Exception ex) { _status = "이전 빌드 복원 실패: " + ex.Message; Logger.LogWarning(ex); Notify("저장된 빌드를 복원하지 못했습니다. 링크를 다시 불러오세요.", NotificationKind.Warning); }
        }

        TickStartingPreset();
        if (_gateway.TickTabletRewardCalculation()) _nextSnapshotAt = 0;
        if (!_importing && _plan is not null && _state is not null && Time.unscaledTime >= _nextSnapshotAt)
        {
            var snapshot = pendingSnapshot ?? _gateway.CaptureOnMainThread();
            // Menus remain responsive; exploration and a hidden guide do not
            // need ten expensive observations a second. F8 always reads afresh.
            _nextSnapshotAt = Time.unscaledTime + (snapshot.Screen == ScreenKind.None || !_showOverlay || _showImport ? .5f : .1f);
            _lastSnapshot = snapshot;
            if (!_state.Matches(snapshot))
            {
                _state = ActiveBuildState.Activate(_plan, snapshot);
                _acquisitions.Bind(snapshot);
                _status = "세션 또는 로컬 플레이어 변경으로 진행 상태를 분리했습니다.";
            }
            _recommendation = snapshot.Screen is ScreenKind.Inventory or ScreenKind.TabletBoard || _placementBatch.Active
                ? _gateway.RecommendPlacement(snapshot) : _recommendationEngine!.Recommend(_plan, _state, snapshot);
            if (snapshot.Screen is ScreenKind.ArtifactReward or ScreenKind.Shop && !_placementBatch.Active) _recommendation = _gateway.RecommendReward(_recommendation, snapshot);
            if (!string.IsNullOrEmpty(_plan.MiracleTarget) && snapshot.MiracleKeys.Contains(_plan.MiracleTarget!))
                _state.MarkMiracleAcquired();
            _gateway.SetHighlight(_recommendation.Action?.TargetToken);
        }

        var confirmed = _confirmRequested;
        _confirmRequested = false;
        if (confirmed && (_placementBatch.Active || _postRewardPlacement))
        {
            StopPlacement("자동배치를 중단했습니다.");
            return;
        }
        if (confirmed && (_plan is null || _state is null)) Logger.LogInfo("Confirmation ignored: no active reviewed build.");
        if (confirmed && Application.isFocused && _showOverlay && !_gateway.IsGhostPreview && !ControllerReviewPreview && !_modalInput.Capturing && !_importing && !_executing && _plan is not null && _state is not null)
        {
            if (_lastSnapshot?.Screen == ScreenKind.Inventory && _gateway.BatchBoardReady &&
                (_recommendation?.Action is null || InventoryPlacementBatch.Eligible(_recommendation.Action))) StartPlacement(_lastSnapshot);
            else if (_recommendation?.Action is not null) _ = ConfirmCurrentAsync(_recommendation.Action);
            else if (_recommendation is not null && _lastSnapshot?.Candidates.Any(x => x.Admission == InventoryAdmission.Full) == true)
                Notify(_recommendation.Message, NotificationKind.Warning);
        }
        TickPlacement();
    }

    private async Task ConfirmCurrentAsync(RecommendedAction action, bool placementStep = false)
    {
        var before = _lastSnapshot;
        var candidate = before?.Candidates.FirstOrDefault(x => x.Token == action.TargetToken);
        var build = _plan?.SourceBuildId;
        var authorization = _placementAuthorizationEpoch;
        _executing = true;
        try
        {
            var result = await _executor.ConfirmOnceAsync(action, _lifetime.Token);
            if (_lifetime.Stopped) return;
            Logger.LogInfo($"Confirmed action result: kind={action.Kind}, result={result.Status}, message={result.Message}");
            lock (_uiStateGate) _status = result.Message;
            if (placementStep)
            {
                _placementBatch.Acknowledge(result, Time.unscaledTime);
                if (!_placementBatch.Active) _gateway.PlacementBatchEnabled = false;
            }
            else if (before is not null && authorization == _placementAuthorizationEpoch && build == _plan?.SourceBuildId &&
                InventoryPlacementBatch.FollowsArtifactSelection(before.Screen, action, candidate, result)) QueuePlacementAfterReward(before);
            if (!result.Succeeded) Notify(result.Message, NotificationKind.Warning);
        }
        catch (OperationCanceledException) when (_lifetime.Stopped) { }
        catch (Exception) when (_lifetime.Stopped) { }
        catch (Exception ex) when (!_lifetime.Stopped)
        {
            Logger.LogError(ex);
            if (placementStep) StopPlacement("자동배치 중단: " + ex.Message);
            lock (_uiStateGate) _status = "행동 실행 오류: " + ex.Message;
            Notify("행동을 실행하지 못했습니다. 게임 상태를 확인하세요.", NotificationKind.Error);
        }
        finally
        {
            _executing = false;
            _nextSnapshotAt = 0;
        }
    }

    private async Task ImportAsync()
    {
        if (_lifetime.Stopped || _importing || _executing) return;
        if (!BuildLocator.TryParse(_locatorText, out var locator, out var error))
        {
            _status = error!;
            Notify(error!, NotificationKind.Error);
            return;
        }
        _importing = true;
        var startingContext = _gateway.StartingPresetContext();
        var fromTitle = _gateway.CanDeferStartingPresetFromTitle();
        _pendingStartingBuild = null; _pendingStartingGate = null;
        _recommendation = null;
        _gateway.SetHighlight(null);
        _status = "Wiki에서 빌드를 가져오는 중...";
        try
        {
            Logger.LogInfo($"Import started: {locator!.Id}");
            // Mono's HTTP handler may perform synchronous DNS/TLS work before its first await.
            // Keep that work off the Unity main thread; resume here for Unity-only inspection.
            var result = await Task.Run(() => _source.ImportAsync(locator!, _lifetime.Token), _lifetime.Token);
            if (_lifetime.Stopped) return;
            Logger.LogInfo($"Import response parsed: sections={result.Build.Sections.Count}, origin={result.Origin}");
            var review = new BuildReviewSession(result.Build, _catalog);
            review.RecommendAll();
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
                _status = $"'{result.Build.Title}' 가져오기 완료 ({result.Origin}). 추천 가이드를 준비합니다.";
            }
            try { _reviewStore.Save(ReviewCheckpoint.Capture(review, false)); }
            catch (Exception ex) { Logger.LogWarning("Review checkpoint save failed: " + ex.Message); }
            var presetStatus = QueueStartingPreset(result.Build, startingContext, fromTitle);
            if (!_lifetime.Stopped)
            {
                if (TryActivateReviewedBuild(closeWindow: false))
                {
                    _showOverlay = true;
                    Notify(presetStatus.Applied ? "빌드와 시작 세팅을 적용했습니다." : "빌드를 적용했습니다.", NotificationKind.Success);
                }
                if (!string.IsNullOrWhiteSpace(result.Warning))
                { _status += "\n" + result.Warning; Notify(result.Warning!, NotificationKind.Warning); }
                _status += "\n" + presetStatus.Detail;
                if (!string.IsNullOrWhiteSpace(presetStatus.Warning)) Notify(presetStatus.Warning!, NotificationKind.Warning);
                if (_plan?.Checklist.Any(x => x.StartsWith("미해결", StringComparison.Ordinal)) == true)
                    Notify("일부 목표는 수동 확인이 필요합니다. 설정의 체크리스트를 확인하세요.", NotificationKind.Warning);
                if (review.Bindings.Values.Any(x => !x.AllowsAutomaticAction))
                    Notify("검증되지 않은 항목은 자동 선택하지 않습니다. 설정에서 매핑을 확인하세요.", NotificationKind.Warning);
            }
        }
        catch (OperationCanceledException) when (_lifetime.Stopped) { }
        catch (Exception) when (_lifetime.Stopped) { }
        catch (Exception ex) when (!_lifetime.Stopped)
        {
            Logger.LogWarning(ex);
            lock (_uiStateGate) _status = "가져오기 실패: " + ex.Message;
            Notify("빌드를 불러오지 못했습니다. 링크와 네트워크를 확인하세요.", NotificationKind.Error);
        }
        finally { _importing = false; }
    }

    private void ActivateReviewedBuild()
    {
        var announce = _showImport;
        if (TryActivateReviewedBuild() && announce) Notify("빌드 설정을 적용했습니다.", NotificationKind.Success);
    }

    private bool TryActivateReviewedBuild(bool closeWindow = true)
    {
        if (_review is null) return false;
        try
        {
            _review.VerifyBindings(_gateway.DiscoverCatalogEntities());
            var check = _review.ValidateForActivation(Application.version);
            var plan = _review.CreatePlan(Application.version);
            var snapshot = _gateway.CaptureOnMainThread();
            var store = new ActiveStateStore();
            ActiveBuildState? existing = null;
            try { existing = store.Load(plan.SourceBuildId, snapshot); }
            catch (Exception ex) { Logger.LogWarning("Saved progress unavailable; using uncertain inventory minimum: " + ex.Message); }
            var state = ActiveBuildState.Activate(plan, snapshot, existing);
            var bindings = BindingsByGameKey();
            _plan = plan; _state = state;
            _acquisitions.Bind(snapshot);
            _recommendationEngine = new RecommendationEngine(bindings);
            _gateway.SetPlacementPlan(plan, bindings);
            try { store.Save(state); } catch (Exception ex) { Logger.LogWarning("Progress cache save failed: " + ex.Message); }
            try { _reviewStore.Save(ReviewCheckpoint.Capture(_review, true)); }
            catch (Exception ex) { Logger.LogWarning("Active review checkpoint save failed: " + ex.Message); }
            _status = $"'{_review.Build.Title}' 활성화 완료 · 목표 {_plan.Artifacts.Count}종 / {_plan.Artifacts.Sum(x => x.DesiredAcquisitions)}회. {ConfirmPrompt}로 한 동작씩 확인하세요.";
            foreach (var warning in check.Warnings)
            { Logger.LogWarning(warning); Notify(warning, NotificationKind.Warning); }
            if (closeWindow) _showImport = false;
            return true;
        }
        catch (Exception ex) { _status = "활성화 불가: " + ex.Message; Logger.LogWarning(_status); Notify("빌드를 적용하지 못했습니다. " + ex.Message, NotificationKind.Error); return false; }
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
            InstallRiftShopPatch();
            var inputType = AccessTools.TypeByName("PlayerInputController");
            var inputGate = inputType is null ? null : AccessTools.PropertyGetter(inputType, "BlockAvatarInput");
            if (inputGate is null) throw new MissingMethodException("PlayerInputController.BlockAvatarInput");
            _harmony.Patch(inputGate, postfix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(ApplyReviewInputGate)));
            // Controller submit must not activate an underlying native button
            // while the modal review owns it. No native input is intercepted
            // outside that modal (including warnings about shared dice).
            InstallControllerModalPatches();
            foreach (var name in new[] { "Disconnect", "OnTransportDisconnected", "Shutdown" })
            {
                var boundary = AccessTools.Method(AccessTools.TypeByName("Mirror.NetworkClient"), name);
                if (boundary is not null) _harmony.Patch(boundary, postfix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(OnNetworkDisconnected)));
            }
            // A host can leave/recreate a lobby without disconnecting its own
            // Mirror connection. Invalidate even when rejoining the same lobby ID.
            foreach (var route in new[] { ("SteamInvitation", "HandleLeave"), ("EOSLobbyManager", "ClearLobbyState") })
            {
                var type = AccessTools.TypeByName(route.Item1);
                var boundary = type is null ? null : AccessTools.Method(type, route.Item2);
                if (boundary is not null) _harmony.Patch(boundary, postfix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(OnNetworkDisconnected)));
            }
            var gridType = AccessTools.TypeByName("GridInventory");
            if (gridType is null) throw new TypeLoadException("GridInventory");
            var postfix = new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(OnLocalArtifactAdded));
            var prefix = new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(OnLocalArtifactAdding));
            foreach (var methodName in new[] { "LocalAddItem", "LocalAddItemAtPosition", "AddItemToSubBagWithNotify" })
            {
                var methods = gridType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(x => x.Name == methodName && x.GetParameters().Any(p => p.Name == "isReward"));
                foreach (var method in methods) _harmony.Patch(method, prefix: prefix, postfix: postfix);
            }
            foreach (var route in new[] { ("UserCode_RpcNotifyAddItem__NewItemOwnInstance", nameof(OnClientArtifactAdded)),
                ("UserCode_RpcUniquePairEnchanted__ItemPosition", nameof(OnClientArtifactMerged)) })
            {
                var method = AccessTools.Method(gridType, route.Item1) ?? throw new MissingMethodException(gridType.Name, route.Item1);
                _harmony.Patch(method, postfix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), route.Item2));
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("획득 이벤트 패치를 설치하지 못했습니다. 인벤토리 최소치만 사용합니다: " + ex.Message);
        }
    }

    private void OnLocalContextChanged()
    {
        StopPlacement("자동배치 중단: 연결 또는 로컬 플레이어가 바뀌었습니다.");
        _acquisitions.Reset(); _recommendation = null; _lastSnapshot = null; _nextSnapshotAt = 0;
        if (_pendingStartingGate?.Context is not null) { _pendingStartingBuild = null; _pendingStartingGate = null; }
        _confirmRequested = false;
    }

    private static void OnNetworkDisconnected() { if (_instance is { _quitting: false } plugin) plugin._gateway.NetworkDisconnected(); }

    private static void OnLocalArtifactAdding(object __instance, out int __state) =>
        __state = _instance is { _quitting: false } ? UnityGameGateway.MergeEvidenceCount(__instance) : -1;

    private static void OnLocalArtifactAdded(object __instance, int instanceID, int entityID, bool isReward, object __result, int __state)
    {
        var plugin = _instance;
        if (plugin is null || plugin._quitting || !isReward || (!WasAdditionSuccessful(__result) && !UnityGameGateway.HasMergeSource(__instance, __state, instanceID, entityID))) return;
        plugin.RecordAcquisition(__instance, instanceID.ToString(), entityID.ToString(), AcquisitionEvidence.ServerAddition);
    }

    private static void OnClientArtifactAdded(object __instance, object item)
    {
        var values = UnityGameGateway.ClientAddition(item);
        if (values.Instance is not null && values.Entity is not null)
            _instance?.RecordAcquisition(__instance, values.Instance, values.Entity, AcquisitionEvidence.ClientAddition);
    }

    private static void OnClientArtifactMerged(object __instance, object __0)
    {
        var item = UnityGameGateway.ClientMergedItem(__instance, __0);
        if (item is null) return;
        var values = UnityGameGateway.ClientAddition(item);
        if (values.Instance is not null && values.Entity is not null)
            _instance?.RecordAcquisition(__instance, values.Instance, values.Entity, AcquisitionEvidence.ClientMergeUnproven);
    }

    private void RecordAcquisition(object inventory, string instanceId, string key, AcquisitionEvidence evidence)
    {
        if (_state is null || _quitting || !_gateway.IsLocalInventory(inventory)) return;
        var snapshot = _gateway.ProgressSnapshot();
        if (snapshot is null || !_catalog.Entries.Any(x => x.Kind == CatalogKind.Artifact && x.GameKey == key)) return;
        var observed = _acquisitions.Observe(_state, snapshot, instanceId, key, evidence);
        if (observed == AcquisitionObservation.Ignored) return;
        if (observed == AcquisitionObservation.Confirmed) _gateway.RecordReward(key, instanceId);
        Logger.LogInfo($"Acquisition observed: scope={snapshot.Network.SessionId}, instance={instanceId}, entity={key}, evidence={evidence}, result={observed}");
        try { new ActiveStateStore().Save(_state); } catch (Exception ex) { Logger.LogWarning(ex); }
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
        __result |= _instance is { } plugin && (plugin._quitting || plugin._showImport || plugin.ControllerModalOwnsInput ||
            (plugin._showOverlay && plugin._plan is not null && plugin._pointerOverOverlay));
    }

    private bool ControllerModalOwnsInput => _modalInput.Capturing;
    private static bool AllowNativeModalInput() => _instance is not { } plugin || NativeMenuInputPolicy.Allows(plugin._quitting, plugin.ControllerModalOwnsInput);

    private void InstallControllerModalPatches()
    {
        try
        {
            foreach (var (typeName, methodName) in new[] {
                ("UnityEngine.InputSystem.UI.InputSystemUIInputModule", "Process"), ("UIInputModule", "Update"),
                ("UI_CharacterStatusPanel", "Update") })
            {
                var type = AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName);
                var method = AccessTools.Method(type, methodName) ?? throw new MissingMethodException(typeName, methodName);
                _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin),
                    typeName == "UnityEngine.InputSystem.UI.InputSystemUIInputModule" ? nameof(AllowOwnedUiModule) : nameof(AllowNativeModalInput)));
            }
            // Raw InputAction polling (discard, rotate, close, shop etc.) is
            // separate from EventSystem.Process. IMGUI and our paired-pad
            // ButtonControls remain usable; no native action is changed at rest.
            var actionType = AccessTools.TypeByName("UnityEngine.InputSystem.InputAction") ?? throw new TypeLoadException("InputAction");
            var uiType = AccessTools.TypeByName("UnityEngine.InputSystem.UI.InputSystemUIInputModule");
            if (uiType is null || AccessTools.Method(uiType, "ResetPointers") is null)
                throw new MissingMethodException("InputSystemUIInputModule.ResetPointers");
            foreach (var methodName in new[] { "WasPressedThisFrame", "WasReleasedThisFrame", "IsPressed" })
                _harmony!.Patch(AccessTools.Method(actionType, methodName) ?? throw new MissingMethodException(methodName),
                    prefix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(AllowNativeActionPoll)));
            var callbackType = actionType.GetNestedType("CallbackContext") ?? throw new TypeLoadException("InputAction.CallbackContext");
            var playerInputType = AccessTools.TypeByName("PlayerInputController") ?? throw new TypeLoadException("PlayerInputController");
            foreach (var methodName in NativeMenuInputPolicy.Handlers)
                _harmony!.Patch(AccessTools.Method(playerInputType, methodName, new[] { callbackType }) ?? throw new MissingMethodException(methodName),
                    prefix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(AllowNativeModalInput)));
            _controllerModalGateReady = true;
            Logger.LogInfo("Exclusive modal input gates installed: 11 menu callbacks; owned UI actions preserved.");
        }
        catch (Exception ex) { Logger.LogWarning("Controller review input disabled: " + ex.Message); }
    }

    private static bool AllowOwnedUiModule(object __instance) => AllowNativeModalInput() ||
        (_instance is { _quitting: false } plugin && plugin._nativeBuildWindow?.OwnsModule(__instance) == true);

    private static bool AllowNativeActionPoll(object __instance, ref bool __result)
    {
        if (AllowNativeModalInput() || (_instance is { _quitting: false } plugin && plugin._nativeBuildWindow?.OwnsAction(__instance) == true)) return true;
        __result = false;
        return false;
    }

    private void ResetNativeUiInput(bool rememberSelection = false, bool restoreSelection = false)
    {
        try
        {
            var type = AccessTools.TypeByName("UnityEngine.EventSystems.EventSystem");
            var system = type is null ? null : AccessTools.PropertyGetter(type, "current")?.Invoke(null, null);
            var module = system is null ? null : AccessTools.PropertyGetter(type!, "currentInputModule")?.Invoke(system, null);
            var selected = system is null ? null : AccessTools.PropertyGetter(type!, "currentSelectedGameObject")?.Invoke(system, null) as GameObject;
            if (rememberSelection && selected != null) _nativeFocusBeforeModal = selected;
            // DeactivateModule is a no-op on this game's InputSystem module.
            // ResetPointers clears queued press/release/hover WITHOUT disabling
            // action maps or synthesizing a click/drop on underlying items.
            if (module is not null) AccessTools.Method(module.GetType(), "ResetPointers")?.Invoke(module, null);
            var selection = restoreSelection && selected == null && _nativeFocusBeforeModal != null && _nativeFocusBeforeModal.activeInHierarchy
                ? _nativeFocusBeforeModal : restoreSelection ? selected : null;
            if (system is not null) AccessTools.Method(type!, "SetSelectedGameObject", new[] { typeof(GameObject) })?.Invoke(system, new object?[] { selection });
            if (restoreSelection) _nativeFocusBeforeModal = null;
        }
        catch (Exception ex) { Logger.LogWarning("Modal pointer reset unavailable: " + ex.Message); }
    }

    private void HandleControllerInput()
    {
        if (_controllerReviewPreviewActive && !ControllerReviewPreview)
        {
            _advancedReview = _controllerReviewPreviewWasAdvanced;
            _controllerReviewPreviewActive = false; _showImport = _controllerReviewPreviewWasOpen; _controllerMenu.Reset();
        }
        var previousMode = _controller.GamepadMode;
        _controller.Capture(_padModifier.Value, _modalInput.Capturing);
        _gateway.SetControllerMode(_controller.GamepadMode);
        var wasCapturing = _modalInput.Capturing;
        if (_modalInput.Capturing && !_showImport)
            _modalInput.Tick(Time.frameCount, Application.isFocused, _controller.AnyButtonHeld || WindowsKeyState.AnyModalInputHeld());
        if (wasCapturing && !_modalInput.Capturing) ResetNativeUiInput(restoreSelection: true);
        if (previousMode != _controller.GamepadMode) _controllerMenu.Reset();
        var command = _controllerState.Update(_controller.GamepadMode, Application.isFocused,
            _controller.DeviceId, _controller.Buttons, _showImport);
        if (ControllerReviewPreview) return; // UI-only diagnostics can never confirm or edit.
        switch (command)
        {
            case PadCommand.Import: _importToggleRequested = true; _controllerMenu.Reset(); break;
            case PadCommand.Overlay: _overlayToggleRequested = true; break;
            case PadCommand.Confirm: if ((!_executing || _placementBatch.Active || _postRewardPlacement) && !_importing && !_modalInput.Capturing) _confirmRequested = true; break;
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
            if (keyEvent.keyCode == KeyCode.Escape && _showImport) { _showImport = false; keyEvent.Use(); return; }
            if (keyEvent.keyCode == _importKey.Value && _shortcutLatch.Press((int)keyEvent.keyCode)) _importToggleRequested = true;
            else if (keyEvent.keyCode == _overlayKey.Value && _shortcutLatch.Press((int)keyEvent.keyCode)) _overlayToggleRequested = true;
            else if (keyEvent.keyCode == _confirmKey.Value && _shortcutLatch.Press((int)keyEvent.keyCode) && !_modalInput.Capturing) _confirmRequested = true;
        }
    }

    private void OnGUI()
    {
        if (_quitting || _lifetime.Stopped) return;
        _gateway.SetBoardPointer(Event.current.mousePosition, !_controller.GamepadMode && Event.current.shift);
        HandleShortcutEvent();
        if (!_guiObserved) { _guiObserved = true; Logger.LogInfo("OnGUI callback active."); }
        DrawStyledWindows();
        _gateway.SetItemFont(_theme?.Skin.font);
        if (Event.current.type == EventType.Repaint)
        {
            _gateway.DrawNotification(_notifications.Current, _notifications.Opacity, _uiScale.Value);
            _gateway.BeginNativeOverlay(_showOverlay && !_showImport);
            if (_showOverlay && !_showImport)
            {
                _gateway.DrawPlacementGhosts(_ghostOpacity.Value, _uiScale.Value);
                _gateway.DrawItemOverlay(_plan, _state, _recommendation?.Action, ConfirmPrompt, ImportPrompt, _uiScale.Value);
                _gateway.EndNativeOverlay();
            }
        }
        UpdateModalCursor();
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
