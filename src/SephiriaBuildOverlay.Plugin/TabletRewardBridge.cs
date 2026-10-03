using System.Reflection;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private readonly List<(string Token, object Entity, int Instance)> _rewardTabletSpecs = new();
    private string _rewardSignature = "";
    private Task<TabletRewardSuggestion?>? _rewardTask;
    private CancellationTokenSource? _rewardCancellation;
    private TabletRewardSuggestion? _rewardSuggestion;
    private bool _rewardCapturing;
    private float _rewardCaptureStarted;

    private void ClearTabletRewards()
    {
        var cancel = _rewardCancellation; var task = _rewardTask;
        cancel?.Cancel();
        if (task is not null) _ = task.ContinueWith(t => { _ = t.Exception; cancel?.Dispose(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else cancel?.Dispose();
        _rewardTask = null; _rewardCancellation = null; _rewardSuggestion = null; _rewardSignature = ""; _rewardCapturing = false;
    }

    private void CaptureTabletRewards(ScreenKind screen, List<ScreenCandidate> candidates)
    {
        if (screen is not (ScreenKind.ArtifactReward or ScreenKind.Shop) || _rewardTabletSpecs.Count == 0 || _placementPlan is null)
        { ClearTabletRewards(); return; }
        var signature = screen + "|" + _boardContext + "|" + _boardSignature + "|" + string.Join(";", _rewardTabletSpecs.Select(x => x.Token + ":" + x.Instance + ":" + ReadNamedString(x.Entity, "id"))) +
            "|" + string.Join(";", candidates.Select(x => x.Token + ":" + x.Kind + ":" + x.CatalogKey + ":" + x.IsSelectable + ":" + x.MoneyCost + ":" + x.AutomaticActionAllowed + ":" + x.AdditionalCostDescription));
        if (_rewardSignature != signature) { ClearTabletRewards(); _rewardSignature = signature; _rewardCapturing = true; _rewardCaptureStarted = Time.unscaledTime; }
        if (_optimizationInput is null || _optimizationInput.Unavailable is not null)
        { if (Time.unscaledTime - _rewardCaptureStarted > 3) _rewardCapturing = false; return; }
        if (_rewardCapturing)
        {
            var offers = new List<TabletRewardOffer>();
            _optionCaptureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                foreach (var spec in _rewardTabletSpecs.Take(12))
                {
                    var prefab = ReadNamedObject(spec.Entity, "resourcePrefab") as GameObject;
                    var type = GameType("StoneTablet");
                    var tablet = prefab == null || type is null ? null : prefab.GetComponent(type);
                    if (tablet == null) continue;
                    var options = NativeTabletOptions(tablet, _optimizationInput.Width, _optimizationInput.Height, _optimizationInput.Storage, default, 0, false, spec.Instance);
                    offers.Add(new TabletRewardOffer(spec.Token, ReadNamedString(spec.Entity, "id")!, options));
                }
            }
            catch (InvalidOperationException ex)
            {
                // Parsing is resumable within the main-thread slice. Other
                // failures leave no automatic tablet selection.
                if (ex.Message == "석판 효과 맵 준비 중") return;
                _rewardCapturing = false; _log.LogWarning("Tablet reward capture unavailable: " + ex.Message); return;
            }
            var captured = _optimizationInput;
            _rewardCapturing = false; _rewardCancellation = new CancellationTokenSource(); var token = _rewardCancellation.Token;
            _rewardTask = Task.Run(() => TabletRewardPlanner.Recommend(captured, offers, token), token);
        }
        if (_rewardTask is { IsCompleted: true })
        {
            if (_rewardTask.Status == TaskStatus.RanToCompletion) _rewardSuggestion = _rewardTask.Result;
            else if (_rewardTask.IsFaulted) _log.LogWarning("Tablet reward calculation failed: " + _rewardTask.Exception?.GetBaseException().Message);
            _rewardTask = null; _rewardCancellation?.Dispose(); _rewardCancellation = null;
        }
    }

    internal Recommendation RecommendReward(Recommendation artifact, RunSnapshot snapshot) =>
        TabletRewardPlanner.Choose(artifact, snapshot, _rewardTabletSpecs.Count > 0, _rewardCapturing || _rewardTask is not null, _rewardSuggestion);
}
