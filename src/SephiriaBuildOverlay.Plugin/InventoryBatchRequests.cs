using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private readonly InventoryPlacementBatch _placementBatch = new();
    private bool _postRewardPlacement;
    private Guid _postRewardBuild;
    private string _postRewardRun = "", _postRewardPlayer = "";
    private float _postRewardDeadline;
    private long _placementAuthorizationEpoch;

    private void StopPlacement(string reason)
    {
        var wasActive = _placementBatch.Active || _postRewardPlacement;
        _placementAuthorizationEpoch++;
        _postRewardPlacement = false;
        if (_placementBatch.Active) _placementBatch.Stop(reason);
        if (_gateway is not null) _gateway.PlacementBatchEnabled = false;
        if (wasActive && !_quitting && !_lifetime.Stopped) Notify(reason, NotificationKind.Warning);
    }

    private void StartPlacement(RunSnapshot snapshot)
    {
        _placementBatch.Start(_plan!.SourceBuildId, snapshot, Time.unscaledTime);
        _gateway.PlacementBatchEnabled = true;
        _nextSnapshotAt = 0;
        Notify("자동배치 시작 · 다시 확인키를 누르면 중단", NotificationKind.Information);
    }

    private void QueuePlacementAfterReward(RunSnapshot before)
    {
        if (_plan is null) return;
        _postRewardPlacement = true; _postRewardBuild = _plan.SourceBuildId;
        _postRewardRun = before.RunId; _postRewardPlayer = before.LocalPlayerId;
        _postRewardDeadline = Time.unscaledTime + 10;
        _nextSnapshotAt = 0;
    }

    private void TickPlacement()
    {
        var snapshot = _lastSnapshot;
        var allowed = !_lifetime.Stopped && Application.isFocused && _showOverlay && !_showImport && !_importing &&
            !_gateway.IsGhostPreview && !ControllerReviewPreview && _plan is not null && snapshot?.IsLocalPlayerOwned == true;
        if (!allowed) { StopPlacement("자동배치 중단: 화면 또는 입력 상태가 바뀌었습니다."); return; }
        if (_postRewardPlacement && !_executing)
        {
            if (_plan!.SourceBuildId != _postRewardBuild || snapshot!.RunId != _postRewardRun || snapshot.LocalPlayerId != _postRewardPlayer || Time.unscaledTime >= _postRewardDeadline)
            { StopPlacement("획득 후 자동배치 중단: 상태가 바뀌었습니다."); return; }
            if (snapshot.ServerRequestPending) return;
            try
            {
                if (_gateway.TryOpenInventoryAfterReward())
                { _postRewardPlacement = false; StartPlacement(snapshot); return; }
            }
            catch (Exception ex) { StopPlacement("획득 후 자동배치 중단: " + ex.GetBaseException().Message); return; }
            if (snapshot.Screen is not (ScreenKind.None or ScreenKind.ArtifactReward or ScreenKind.Inventory))
            { StopPlacement("획득 후 자동배치 중단: 다른 창이 열렸습니다."); return; }
        }
        if (!_placementBatch.Active || _executing) return;
        // At most one dispatch per frame, and none until the previous receipt
        // and exact expected layout are both observed.
        var next = _gateway.NextPlacementBatch(_placementBatch, _plan!.SourceBuildId, snapshot!, allowed, Time.unscaledTime);
        if (!_placementBatch.Active)
        {
            _gateway.PlacementBatchEnabled = false;
            Logger.LogInfo($"Inventory batch ended: steps={_placementBatch.CompletedSteps}, reason={_placementBatch.EndReason}");
            Notify(_placementBatch.EndReason!, NotificationKind.Information);
        }
        else if (next is not null) _ = ConfirmCurrentAsync(next, placementStep: true);
    }
}
