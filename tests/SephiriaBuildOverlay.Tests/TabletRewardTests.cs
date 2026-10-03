using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class TabletRewardTests
{
    private static BoardOptimizationInput Board(bool full = false, ArtifactCondition condition = ArtifactCondition.None, int level = 0) => new(3, 1, 3,
        Enumerable.Range(0, 3).Select(x => new BoardCell(new(x, 0), level)),
        new[] { new BoardArtifact("a", "a", new(0, 0), 5, 0, false, condition) }, Array.Empty<BoardTablet>(),
        full ? new Dictionary<string, GridPoint> { ["a"] = new(0, 0), ["b"] = new(1, 0), ["c"] = new(2, 0) } : new Dictionary<string, GridPoint> { ["a"] = new(0, 0) },
        new[] { new ArtifactTarget("a", 1, TargetRole.Required, 0) });
    private static TabletRewardOffer Offer(string token = "tablet", int value = 4, BoardCondition[]? conditions = null) => new(token, "2100",
        new[] { new BoardTabletOption(new(1, 0), 0, new[] { new BoardEffect(new(0, 0), BoardEffectKind.Add, value) }, conditions) });

    [Fact]
    public void HelpfulTabletIsSelectedInsteadOfRerollAndLargerBuildEffectWins()
    {
        var suggestion = TabletRewardPlanner.Recommend(Board(), new[] { Offer("weak", 1), Offer("strong", 4) });
        Assert.NotNull(suggestion); Assert.Equal("strong", suggestion.Offer.Token); Assert.Equal(4, suggestion.After.RequiredLevel);
        var s = Snapshot(screen: ScreenKind.ArtifactReward, candidates: new[] { new ScreenCandidate("strong", CandidateKind.Tablet, "2100") });
        var selected = TabletRewardPlanner.Choose(new(new(ActionKind.Reroll, "reroll", "", s.Identity), ""), s, true, false, suggestion);
        Assert.Equal(ActionKind.Select, selected.Action!.Kind); Assert.Equal("strong", selected.Action.TargetToken); Assert.True(selected.Action.AutomaticBindingAllowed);
    }

    [Fact]
    public void FullBoardUnknownConditionsAndNonImprovingTabletDoNotAuthorizeSelection()
    {
        Assert.Null(TabletRewardPlanner.Recommend(Board(full: true), new[] { Offer() }));
        Assert.Null(TabletRewardPlanner.Recommend(Board(condition: ArtifactCondition.Unknown), new[] { Offer() }));
        Assert.Null(TabletRewardPlanner.Recommend(Board(), new[] { Offer(value: -2) }));
        Assert.Null(TabletRewardPlanner.Recommend(Board(level: 5), new[] { Offer() }));
        Assert.Null(TabletRewardPlanner.Recommend(Board(), new[] { Offer(conditions: new[] { new BoardCondition(new(2, 0), BoardConditionKind.Charm) }) }));
    }

    [Fact]
    public void MixedArtifactThenRerollThenTabletRefreshesRecommendationAndStaleOfferIsDiscarded()
    {
        var plan = Plan(); var engine = new RecommendationEngine();
        var state = ActiveBuildState.Activate(plan, Snapshot()); var suggestion = TabletRewardPlanner.Recommend(Board(), new[] { Offer() });
        var mixed = Snapshot(revision: 1, screen: ScreenKind.ArtifactReward, candidates: new[] {
            new ScreenCandidate("artifact", CandidateKind.Artifact, "a"), new ScreenCandidate("tablet", CandidateKind.Tablet, "2100") });
        Assert.Equal("artifact", TabletRewardPlanner.Choose(engine.Recommend(plan, state, mixed), mixed, true, false, suggestion).Action!.TargetToken);
        var outside = Snapshot(revision: 2, screen: ScreenKind.ArtifactReward, candidates: new[] { new ScreenCandidate("outside", CandidateKind.Artifact, "x"), new ScreenCandidate("reroll", CandidateKind.Reroll, null, isFreeReroll: true) });
        Assert.Equal(ActionKind.Reroll, TabletRewardPlanner.Choose(engine.Recommend(plan, state, outside), outside, false, false, suggestion).Action!.Kind);
        var tablets = Snapshot(revision: 3, screen: ScreenKind.ArtifactReward, candidates: new[] { new ScreenCandidate("tablet", CandidateKind.Tablet, "2100"), new ScreenCandidate("reroll", CandidateKind.Reroll, null, isFreeReroll: true) });
        var selected = TabletRewardPlanner.Choose(engine.Recommend(plan, state, tablets), tablets, true, false, suggestion);
        Assert.Equal("tablet", selected.Action!.TargetToken); Assert.Equal(tablets.Identity, selected.Action.BasedOn);
        var changed = Snapshot(revision: 4, screen: ScreenKind.ArtifactReward, candidates: new[] { new ScreenCandidate("new-tablet", CandidateKind.Tablet, "2101"), new ScreenCandidate("reroll", CandidateKind.Reroll, null, isFreeReroll: true) });
        Assert.Equal(ActionKind.Reroll, TabletRewardPlanner.Choose(engine.Recommend(plan, state, changed), changed, true, false, suggestion).Action!.Kind);
        Assert.Null(TabletRewardPlanner.Choose(engine.Recommend(plan, state, tablets), tablets, true, true, null).Action);
    }

    [Fact]
    public void NoGoalsNoAffordableRerollUsesNativeDiceConversionButFreeRerollStillWins()
    {
        var plan = Plan(); var state = ActiveBuildState.Activate(plan, Snapshot());
        var choices = new[] { new ScreenCandidate("outside", CandidateKind.Artifact, "x"), new ScreenCandidate("reroll", CandidateKind.Reroll, null, diceCost: 1), new ScreenCandidate("convert", CandidateKind.AbandonOrConvert, null) };
        var s = Snapshot(screen: ScreenKind.ArtifactReward, dice: 0, candidates: choices);
        Assert.Equal("convert", new RecommendationEngine().Recommend(plan, state, s).Action!.TargetToken);
        var free = Snapshot(screen: ScreenKind.ArtifactReward, dice: 0, candidates: choices.Concat(new[] { new ScreenCandidate("free", CandidateKind.Reroll, null, isFreeReroll: true) }).ToArray());
        Assert.Equal("free", new RecommendationEngine().Recommend(plan, state, free).Action!.TargetToken);
        Assert.Null(new RecommendationEngine().Recommend(plan, state, Snapshot(screen: ScreenKind.ArtifactReward, dice: 0, candidates: choices.Take(2).ToArray())).Action);
    }

    [Fact]
    public void TabletAcquisitionRequiresObservedInventoryNotJustChangedScreen()
    {
        var before = Snapshot(screen: ScreenKind.ArtifactReward, candidates: new[] { new ScreenCandidate("tablet", CandidateKind.Tablet, "2100") });
        var observer = new ActionOutcomeObserver(before, new(ActionKind.Select, "tablet", "", before.Identity));
        Assert.Equal(ObservedActionOutcome.Pending, observer.Observe(Snapshot(revision: 2)));
        Assert.Equal(ObservedActionOutcome.Succeeded, observer.Observe(Snapshot(revision: 3, inventory: new[] { new InventoryArtifact("new", "2100") })));
    }

    [Fact]
    public void PlannerCancellationDoesNotReturnASelectableStaleOffer()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => TabletRewardPlanner.Recommend(Board(), new[] { Offer() }, cancelled.Token));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "사파이어 10 · 영구 재화 · 수동 확인")]
    [InlineData(false, "돈 20 · 게임 구매 창에서 확인")]
    public void HelpfulShopTabletIsBuyInsteadOfSelectAndPreservesNativeCurrencyAndConfirmation(bool automatic, string? cost)
    {
        var suggestion = TabletRewardPlanner.Recommend(Board(), new[] { Offer() });
        var shop = Snapshot(screen: ScreenKind.Shop, money: 0, candidates: new[] {
            new ScreenCandidate("tablet", CandidateKind.Tablet, "2100", moneyCost: automatic ? 20 : 0,
                automaticActionAllowed: automatic, additionalCostDescription: cost) });
        var recommended = TabletRewardPlanner.Choose(new(null, ""), shop, true, false, suggestion);
        Assert.Equal(ActionKind.Buy, recommended.Action!.Kind);
        Assert.Equal(automatic, recommended.Action.AutomaticBindingAllowed);
        Assert.Equal(automatic ? 20 : 0, recommended.Action.MoneyCost);
        Assert.Equal(0, recommended.Action.DiceCost); Assert.Equal(DiceRisk.None, recommended.Action.DiceRisk);
        if (cost is not null) Assert.Contains(cost, recommended.Message);
    }

    [Fact]
    public void MixedMerchantKeepsNeededArtifactBeforeTabletAndImprovingTabletBeforeReplenishment()
    {
        var suggestion = TabletRewardPlanner.Recommend(Board(), new[] { Offer() });
        var shop = Snapshot(screen: ScreenKind.Shop, candidates: new[] { new ScreenCandidate("tablet", CandidateKind.Tablet, "2100") });
        var artifact = new Recommendation(new(ActionKind.Buy, "artifact", "", shop.Identity), "");
        Assert.Same(artifact, TabletRewardPlanner.Choose(artifact, shop, true, false, suggestion));
        var reroll = new Recommendation(new(ActionKind.Reroll, "refill", "", shop.Identity, automaticBindingAllowed: false), "");
        Assert.Equal("tablet", TabletRewardPlanner.Choose(reroll, shop, true, false, suggestion).Action!.TargetToken);
        Assert.Null(TabletRewardPlanner.Choose(reroll, shop, true, true, suggestion).Action);
        Assert.Same(reroll, TabletRewardPlanner.Choose(reroll, shop, true, false, null));
    }

    [Fact]
    public void TabletShopDoesNotUseStaleDifferentMerchantNonLocalOrClosedScreenOffer()
    {
        var suggestion = TabletRewardPlanner.Recommend(Board(), new[] { Offer() });
        var different = Snapshot(screen: ScreenKind.Shop, candidates: new[] { new ScreenCandidate("other", CandidateKind.Tablet, "2100") });
        var fallback = new Recommendation(null, "");
        Assert.Same(fallback, TabletRewardPlanner.Choose(fallback, different, true, false, suggestion));
        Assert.Same(fallback, TabletRewardPlanner.Choose(fallback, Snapshot(screen: ScreenKind.Inventory,
            candidates: new[] { new ScreenCandidate("tablet", CandidateKind.Tablet, "2100") }), true, false, suggestion));
        Assert.Same(fallback, TabletRewardPlanner.Choose(fallback, Snapshot(screen: ScreenKind.Shop, owned: false,
            candidates: new[] { new ScreenCandidate("tablet", CandidateKind.Tablet, "2100") }), true, false, suggestion));
    }
}
