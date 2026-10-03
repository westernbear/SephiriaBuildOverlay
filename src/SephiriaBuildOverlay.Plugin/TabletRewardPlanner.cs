using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class TabletRewardOffer
{
    public TabletRewardOffer(string token, string key, IEnumerable<BoardTabletOption> options)
    { Token = token; Key = key; Options = options.ToArray(); }
    public string Token { get; }
    public string Key { get; }
    public IReadOnlyList<BoardTabletOption> Options { get; }
}
internal sealed class TabletRewardSuggestion
{
    public TabletRewardSuggestion(TabletRewardOffer offer, BoardObjective before, BoardObjective after)
    { Offer = offer; Before = before; After = after; }
    public TabletRewardOffer Offer { get; }
    public BoardObjective Before { get; }
    public BoardObjective After { get; }
    public string Reason => $"석판 효과 개선 · 필수 레벨 {Before.RequiredLevel} → {After.RequiredLevel} · 추천 레벨 {Before.RecommendedLevel} → {After.RecommendedLevel}";
}
internal static class TabletRewardPlanner
{
    public static TabletRewardSuggestion? Recommend(BoardOptimizationInput input, IEnumerable<TabletRewardOffer> offers, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (input.Unavailable is not null || input.Cells.Count > 64 || input.Artifacts.Any(x => x.Condition == ArtifactCondition.Unknown)) return null;
        var solver = new JointBoardPlanner();
        var baseline = solver.Solve(input, token, 512, useExactSearch: false);
        if (baseline.Unavailable is not null) return null;
        var occupied = new HashSet<GridPoint>(JointBoardPlanner.Current(input).Positions.Values);
        var empty = input.Cells.Select(x => x.Position).Where(x => !occupied.Contains(x)).ToArray();
        if (empty.Length == 0) return null;
        TabletRewardSuggestion? best = null;
        foreach (var offer in offers.Take(12))
        {
            token.ThrowIfCancellationRequested();
            var initial = offer.Options.FirstOrDefault(x => empty.Contains(x.Position));
            if (initial is null) continue;
            var id = "reward:" + offer.Token;
            var items = input.Items.ToDictionary(x => x.Key, x => x.Value); items[id] = initial.Position;
            var tablet = new BoardTablet(id, offer.Key, initial.Position, initial.Rotation, true, offer.Options);
            var augmented = new BoardOptimizationInput(input.Width, input.Height, input.Storage, input.Cells, input.Artifacts,
                input.Tablets.Concat(new[] { tablet }), items, input.Goals, input.Unavailable, input.GloballyActive);
            var result = solver.Solve(augmented, token, 1024, useExactSearch: false);
            if (result.Unavailable is not null || result.After.CompareBenefits(baseline.After) <= 0) continue;
            if (best is null || result.After.CompareBenefits(best.After) > 0) best = new TabletRewardSuggestion(offer, baseline.After, result.After);
        }
        return best;
    }

    public static Recommendation Choose(Recommendation artifact, RunSnapshot snapshot, bool hasTablets, bool calculating, TabletRewardSuggestion? suggestion)
    {
        if (snapshot.Screen is not (ScreenKind.ArtifactReward or ScreenKind.Shop) || artifact.Action?.Kind is ActionKind.Select or ActionKind.Buy ||
            !hasTablets || !snapshot.IsLocalPlayerOwned || snapshot.ServerRequestPending) return artifact;
        var tablets = snapshot.Candidates.Where(x => x.Kind == CandidateKind.Tablet && x.IsSelectable).ToArray();
        if (tablets.Length > 0 && tablets.All(x => x.Admission == InventoryAdmission.Full)) return artifact;
        if (calculating) return new Recommendation(null, "석판 효과를 비교 중입니다.");
        if (suggestion is null) return artifact;
        var candidate = snapshot.Candidates.FirstOrDefault(x => x.Token == suggestion.Offer.Token && x.Kind == CandidateKind.Tablet && x.CatalogKey == suggestion.Offer.Key && x.IsSelectable);
        if (candidate is null) return artifact;
        if (InventoryAdmissionPolicy.BlockReason(candidate.Admission) is not null) return artifact;
        var cost = string.IsNullOrWhiteSpace(candidate.AdditionalCostDescription) ? "" : " · " + candidate.AdditionalCostDescription;
        var action = new RecommendedAction(snapshot.Screen == ScreenKind.Shop ? ActionKind.Buy : ActionKind.Select, candidate.Token, suggestion.Reason, snapshot.Identity,
            candidate.MoneyCost, candidate.DiceCost, expectedResult: suggestion.Reason + cost, automaticBindingAllowed: candidate.AutomaticActionAllowed);
        return new Recommendation(action, suggestion.Reason + cost);
    }
}
