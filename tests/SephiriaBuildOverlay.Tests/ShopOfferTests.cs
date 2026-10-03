using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Plugin;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class ShopOfferTests
{
    [Theory]
    [InlineData(-1, 2)] [InlineData(0, 2)] [InlineData(1, 4)] [InlineData(2, 8)] [InlineData(3, 16)] [InlineData(4, 32)] [InlineData(8, 32)]
    public void ReplenishmentUsesNativeBoundedSapphireCost(int tries, int expected) => Assert.Equal(expected, ShopOfferPolicy.ReplenishmentCost(tries));

    [Theory]
    [InlineData(true, 0, 0, true)] [InlineData(true, 2, 1, true)] [InlineData(true, 2, 0, false)] [InlineData(false, 2, 1, false)]
    public void DoNotInventReplenishmentWhenNativeButtonUnavailableOrAllPurchased(bool button, int count, int unpurchased, bool expected) =>
        Assert.Equal(expected, ShopOfferPolicy.CanReplenish(button, count, unpurchased));

    [Theory]
    [InlineData("사파이어 10 · 영구 재화 · 수동 확인")]
    [InlineData("돈 20 · 게임 구매 창에서 확인")]
    [InlineData("거래권 1장 · 수동 확인")]
    public void RiftNewStockAndVoucherGoodsRemainRecommendedButNeverAutomaticallyConsumed(string cost)
    {
        var s = Snapshot(screen: ScreenKind.Shop, candidates: new[] { new ScreenCandidate("item", CandidateKind.Artifact, "a", automaticActionAllowed: false, additionalCostDescription: cost) });
        var plan = Plan(); var result = new RecommendationEngine().Recommend(plan, ActiveBuildState.Activate(plan, s), s);
        Assert.Equal(ActionKind.Buy, result.Action!.Kind); Assert.False(result.Action.AutomaticBindingAllowed); Assert.Contains(cost, result.Message);
    }

    [Fact]
    public void NoShopGoalProposesManualSapphireReplenishmentWithoutDiceWarning()
    {
        var plan = Plan(); var s = Snapshot(screen: ScreenKind.Shop, dice: 1, candidates: new[] { new ScreenCandidate("refill", CandidateKind.Reroll, null,
            automaticActionAllowed: false, additionalCostDescription: ShopOfferPolicy.SapphireCost(2)) });
        var result = new RecommendationEngine().Recommend(plan, ActiveBuildState.Activate(plan, s), s);
        Assert.Equal(ActionKind.Reroll, result.Action!.Kind); Assert.False(result.Action.AutomaticBindingAllowed);
        Assert.Equal(DiceRisk.None, result.Action.DiceRisk); Assert.Equal(0, result.Action.DiceCost); Assert.Contains("사파이어 2", result.Message);
    }

    [Theory]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]
    public void SynthesisRedFrameOnlyAfterFinishedWithoutRecommendation(bool active, bool preparing, bool calculating, bool suggestion, bool expected) =>
        Assert.Equal(expected, SynthesisFeedbackPolicy.NoRecommendation(active, preparing, calculating, suggestion));
}
