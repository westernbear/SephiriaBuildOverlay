using SephiriaBuildOverlay.Core.Runtime;

namespace SephiriaBuildOverlay.Plugin;

// A confirmation authorizes one pinned destination, never a stream of arbitrary
// recommendations. This state machine is pure; Unity capture stays on its thread.
internal sealed class InventoryPlacementBatch
{
    private Guid _build;
    private RunScope _scope;
    private string? _model;
    private BoardLayout? _goal, _observed, _expected;
    private double _deadline;
    private bool _awaitingLayout;
    public bool Active { get; private set; }
    public bool RequestPending { get; private set; }
    public int CompletedSteps { get; private set; }
    public string? EndReason { get; private set; }

    public void Start(Guid build, RunSnapshot snapshot, double now)
    {
        _build = build;
        _scope = snapshot.Scope;
        _model = null; _goal = _observed = _expected = null;
        Active = true; RequestPending = _awaitingLayout = false; CompletedSteps = 0; EndReason = null;
        _deadline = now + 10;
    }

    public RecommendedAction? Next(Guid build, RunSnapshot snapshot, bool contextAllowed, string? model,
        BoardLayout? current, BoardLayout? goal, RecommendedAction? action, BoardLayout? expected,
        bool calculating, string? unavailable, double now)
    {
        if (!Active) return null;
        if (!contextAllowed || build != _build || !snapshot.Scope.Equals(_scope) || !snapshot.IsLocalPlayerOwned || !snapshot.Network.Connected)
        { Stop("자동배치 중단: 창 또는 플레이어 상태가 바뀌었습니다."); return null; }
        if (now >= _deadline) { Stop("자동배치 중단: 배치 상태 확인 시간이 초과되었습니다."); return null; }
        if (RequestPending || snapshot.ServerRequestPending) return null;
        if (model is null || current is null) return null; // transient native map: wait, never act
        if (_model is not null && _model != model) { Stop("자동배치 중단: 아이템 또는 효과가 바뀌었습니다."); return null; }
        if (_observed is not null && !BoardPlanContinuation.Same(current, _observed))
        {
            if (_awaitingLayout && _expected is not null && BoardPlanContinuation.Same(current, _expected))
            { _observed = current; _awaitingLayout = false; _expected = null; _deadline = now + 10; }
            else { Stop("자동배치 중단: 예상하지 않은 수동 배치 변경을 감지했습니다."); return null; }
        }
        if (_awaitingLayout) return null;
        if (_goal is not null && BoardPlanContinuation.Same(current, _goal)) { Stop("자동배치 완료"); return null; }
        if (!string.IsNullOrEmpty(unavailable)) { Stop("자동배치 중단: " + unavailable); return null; }
        if (calculating) return null;
        if (action is null)
        { Stop(_goal is null ? "현재 배치를 유지합니다." : "자동배치 중단: 다음 배치를 실행할 수 없습니다."); return null; }
        if (!Eligible(action) || expected is null || goal is null || !action.BasedOn.Equals(snapshot.Identity))
        { Stop("자동배치 중단: 안전한 이동·회전 추천이 아닙니다."); return null; }
        if (_goal is not null && !BoardPlanContinuation.Same(goal, _goal))
        { Stop("자동배치 중단: 목표 배치가 바뀌었습니다."); return null; }
        _model ??= model; _goal ??= goal; _observed = current; _expected = expected;
        RequestPending = true; _deadline = now + 10;
        return action;
    }

    public void Acknowledge(ActionExecutionResult result, double now)
    {
        if (!Active) return;
        RequestPending = false;
        if (!result.Succeeded) { Stop("자동배치 중단: " + result.Message); return; }
        CompletedSteps++; _awaitingLayout = true; _deadline = now + 10;
        if (CompletedSteps >= 256) Stop("자동배치 중단: 실행 횟수 제한을 초과했습니다.");
    }

    public void Stop(string reason) { Active = false; RequestPending = false; EndReason = reason; }
    public static bool Eligible(RecommendedAction action) => action.Kind is ActionKind.Move or ActionKind.Rotate &&
        action.TargetToken.StartsWith("opt:", StringComparison.Ordinal) && action.MoneyCost == 0 && action.DiceCost == 0 && action.AutomaticBindingAllowed;
    public static bool FollowsArtifactSelection(ScreenKind screen, RecommendedAction action, ScreenCandidate? candidate, ActionExecutionResult result) =>
        result.Succeeded && screen == ScreenKind.ArtifactReward && action.Kind == ActionKind.Select && action.AutomaticBindingAllowed &&
        candidate?.Kind == CandidateKind.Artifact && candidate.Token == action.TargetToken && candidate.AutomaticActionAllowed;
}
