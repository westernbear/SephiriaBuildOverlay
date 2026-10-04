namespace SephiriaBuildOverlay.Core.Runtime;

public enum ActionExecutionStatus
{
    Succeeded,
    Rejected,
    Stale,
    TimedOut,
    Busy,
    UnsafeBinding
}

public sealed class ActionExecutionResult
{
    public ActionExecutionResult(ActionExecutionStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    public ActionExecutionStatus Status { get; }
    public string Message { get; }
    public bool Succeeded => Status == ActionExecutionStatus.Succeeded;
}

public sealed class GameActionReceipt
{
    public GameActionReceipt(string requestId, bool accepted, string? rejectionReason = null)
    {
        RequestId = requestId;
        Accepted = accepted;
        RejectionReason = rejectionReason;
    }

    public string RequestId { get; }
    public bool Accepted { get; }
    public string? RejectionReason { get; }
}

public interface IGameActionGateway
{
    Task<RunSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken);
    Task<GameActionReceipt> SendThroughNormalRequestPathAsync(RecommendedAction action, CancellationToken cancellationToken);
    Task<bool> WaitForServerConfirmationAsync(GameActionReceipt receipt, CancellationToken cancellationToken);
}

public sealed class ConfirmedActionExecutor
{
    private readonly IGameActionGateway _gateway;
    private readonly TimeSpan _serverTimeout;
    private int _executing;

    public ConfirmedActionExecutor(IGameActionGateway gateway, TimeSpan? serverTimeout = null)
    {
        _gateway = gateway;
        _serverTimeout = serverTimeout ?? TimeSpan.FromSeconds(5);
    }

    // One native request per call. The caller needs an explicit confirmation;
    // a confirmed inventory-only batch may authorize successive free moves.
    public async Task<ActionExecutionResult> ConfirmOnceAsync(RecommendedAction recommendation, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _executing, 1, 0) != 0)
            return new ActionExecutionResult(ActionExecutionStatus.Busy, "이전 서버 응답을 기다리는 중입니다.");
        try
        {
            if (recommendation.ExecutionBlockReason is not null)
                return new ActionExecutionResult(ActionExecutionStatus.Rejected, recommendation.ExecutionBlockReason);
            if (!recommendation.AutomaticBindingAllowed)
                return new ActionExecutionResult(ActionExecutionStatus.UnsafeBinding, "자동 실행이 허용되지 않는 추천입니다. 게임에서 수동으로 확인하세요.");

            var current = await _gateway.CaptureSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!current.IsLocalPlayerOwned || !current.Network.Connected)
                return new ActionExecutionResult(ActionExecutionStatus.Rejected, "로컬 소유 플레이어가 아닙니다.");
            if (!current.Identity.Equals(recommendation.BasedOn))
                return new ActionExecutionResult(ActionExecutionStatus.Stale, "화면 또는 상태가 바뀌어 오래된 추천을 폐기했습니다.");
            if (current.ServerRequestPending)
                return new ActionExecutionResult(ActionExecutionStatus.Busy, "게임이 이전 요청을 처리 중입니다.");
            var candidate = current.Candidates.FirstOrDefault(x => x.Token == recommendation.TargetToken);
            if (candidate is not null && recommendation.Kind is ActionKind.Select or ActionKind.Buy &&
                InventoryAdmissionPolicy.IsInventoryItem(candidate.Kind) && InventoryAdmissionPolicy.BlockReason(candidate.Admission) is { } blocked)
                return new ActionExecutionResult(ActionExecutionStatus.Rejected, blocked);
            if (recommendation.MoneyCost > current.Money)
                return new ActionExecutionResult(ActionExecutionStatus.Rejected, "보유 재화가 부족합니다.");
            if (recommendation.DiceCost > current.SharedDice)
                return new ActionExecutionResult(ActionExecutionStatus.Rejected, "공유 주사위가 부족합니다.");
            if (!current.Candidates.Any(x => x.Token == recommendation.TargetToken && x.IsSelectable && x.AutomaticActionAllowed &&
                x.MoneyCost == recommendation.MoneyCost && x.DiceCost == recommendation.DiceCost))
                return new ActionExecutionResult(ActionExecutionStatus.Stale, "대상 후보 또는 비용이 바뀌었습니다.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_serverTimeout);
            GameActionReceipt receipt;
            try
            {
                receipt = await _gateway.SendThroughNormalRequestPathAsync(recommendation, timeout.Token).ConfigureAwait(false);
                if (!receipt.Accepted)
                    return new ActionExecutionResult(ActionExecutionStatus.Rejected, receipt.RejectionReason ?? "게임 요청이 거절되었습니다.");
                var confirmed = await _gateway.WaitForServerConfirmationAsync(receipt, timeout.Token).ConfigureAwait(false);
                return confirmed
                    ? new ActionExecutionResult(ActionExecutionStatus.Succeeded, "서버 확인 완료. 상태를 다시 계산합니다.")
                    : new ActionExecutionResult(ActionExecutionStatus.Rejected, "서버가 요청을 확인하지 않았습니다.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ActionExecutionResult(ActionExecutionStatus.TimedOut, "서버 응답 시간이 초과되어 다음 행동을 중단했습니다.");
            }
        }
        finally
        {
            Volatile.Write(ref _executing, 0);
        }
    }
}
