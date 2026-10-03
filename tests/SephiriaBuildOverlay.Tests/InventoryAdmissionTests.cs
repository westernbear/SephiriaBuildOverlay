using SephiriaBuildOverlay.Core.Runtime;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class InventoryAdmissionTests
{
    [Theory]
    [InlineData(0, true, InventoryAdmission.Available)]
    [InlineData(0, false, InventoryAdmission.Full)]
    [InlineData(1, false, InventoryAdmission.Merge)]
    [InlineData(2, false, InventoryAdmission.Full)]
    [InlineData(3, true, InventoryAdmission.LimitReached)]
    [InlineData(4, false, InventoryAdmission.LimitReached)]
    [InlineData(6, true, InventoryAdmission.LimitReached)]
    [InlineData(9, true, InventoryAdmission.Unverified)]
    [InlineData(-1, false, InventoryAdmission.Unverified)]
    [InlineData(99, true, InventoryAdmission.Unverified)]
    public void NativeAdmissionFailsClosedAndAllowsVerifiedStacking(int result, bool empty, InventoryAdmission expected) =>
        Assert.Equal(expected, InventoryAdmissionPolicy.Evaluate(result, empty));

    [Theory]
    [InlineData(2, 5, 0, InventoryAdmission.Merge)]
    [InlineData(4, 5, 4, InventoryAdmission.Merge)]
    [InlineData(2, 5, 5, InventoryAdmission.LimitReached)]
    [InlineData(0, 0, 0, InventoryAdmission.LimitReached)]
    [InlineData(2, null, 0, InventoryAdmission.Unverified)]
    [InlineData(2, 5, null, InventoryAdmission.Unverified)]
    public void WisdomMergeUsesOwnedEnchantLimitAndDoesNotNeedAnEmptySlot(int result, int? maximum, int? enchant, InventoryAdmission expected) =>
        Assert.Equal(expected, InventoryAdmissionPolicy.Evaluate(result, false, true, true, maximum, enchant));

    [Fact]
    public void WisdomWithoutTheSameOwnedArtifactStillNeedsSpace()
    {
        Assert.Equal(InventoryAdmission.Full, InventoryAdmissionPolicy.Evaluate(2, false, true, false));
        Assert.Equal(InventoryAdmission.Unverified, InventoryAdmissionPolicy.Evaluate(2, false, true, null));
        Assert.Equal(InventoryAdmission.Unverified, InventoryAdmissionPolicy.Evaluate(null, null));
        Assert.Equal(InventoryAdmission.Unverified, InventoryAdmissionPolicy.Evaluate(99, false, true, true, 5, 0));
        Assert.Equal(InventoryAdmission.LimitReached, InventoryAdmissionPolicy.Evaluate(10, false, true, true, 5, 0));
    }

    [Theory]
    [InlineData(ScreenKind.ArtifactReward)]
    [InlineData(ScreenKind.Shop)]
    public void FullTargetKeepsItsFrameButCannotSpendOrReroll(ScreenKind screen)
    {
        var plan = Plan();
        var snapshot = Snapshot(screen: screen, candidates: new[] {
            new ScreenCandidate("target", CandidateKind.Artifact, "a", moneyCost: 5, admission: InventoryAdmission.Full),
            new ScreenCandidate("reroll", CandidateKind.Reroll, null, diceCost: 1),
            new ScreenCandidate("convert", CandidateKind.AbandonOrConvert, null) });
        var recommendation = new RecommendationEngine().Recommend(plan, ActiveBuildState.Activate(plan, snapshot), snapshot);
        Assert.Equal("target", recommendation.Action!.TargetToken);
        Assert.False(recommendation.Action.AutomaticBindingAllowed);
        Assert.Contains("가방 공간", recommendation.Action.ExecutionBlockReason);
        Assert.Contains("가방 공간", recommendation.Message);
    }

    [Fact]
    public void AvailableDuplicateCandidateIsPreferredAndAllFullTabletsDoNotReroll()
    {
        var plan = Plan();
        var snapshot = Snapshot(screen: ScreenKind.ArtifactReward, candidates: new[] {
            new ScreenCandidate("full", CandidateKind.Artifact, "a", admission: InventoryAdmission.Full),
            new ScreenCandidate("merge", CandidateKind.Artifact, "a", admission: InventoryAdmission.Merge) });
        Assert.Equal("merge", new RecommendationEngine().Recommend(plan, ActiveBuildState.Activate(plan, snapshot), snapshot).Action!.TargetToken);
        snapshot = Snapshot(screen: ScreenKind.ArtifactReward, candidates: new[] {
            new ScreenCandidate("tablet", CandidateKind.Tablet, "tablet", admission: InventoryAdmission.Full),
            new ScreenCandidate("reroll", CandidateKind.Reroll, null, isFreeReroll: true) });
        var result = new RecommendationEngine().Recommend(plan, ActiveBuildState.Activate(plan, snapshot), snapshot);
        Assert.Null(result.Action);
        Assert.Contains("가방 공간", result.Message);
    }

    [Fact]
    public void FullOutsideBuildOffersKeepNativeConversionWithoutInventingADiscardOrPurchase()
    {
        var plan = Plan();
        var snapshot = Snapshot(screen: ScreenKind.ArtifactReward, candidates: new[] {
            new ScreenCandidate("outside", CandidateKind.Artifact, "outside", admission: InventoryAdmission.Full),
            new ScreenCandidate("reroll", CandidateKind.Reroll, null, diceCost: 1),
            new ScreenCandidate("convert", CandidateKind.AbandonOrConvert, null) });
        var result = new RecommendationEngine().Recommend(plan, ActiveBuildState.Activate(plan, snapshot), snapshot);
        Assert.Equal(ActionKind.AbandonOrConvert, result.Action!.Kind);
        Assert.Equal("convert", result.Action.TargetToken);
        Assert.Equal(0, result.Action.DiceCost);
    }
}
