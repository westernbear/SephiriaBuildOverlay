using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class RecommendationTests
{
    [Fact]
    public void ArtifactChoosesRemainingRequiredTargetAndNeverOutsideBuild()
    {
        var plan = Plan(desired: 2);
        var state = ActiveBuildState.Activate(plan, Snapshot());
        state.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired);
        var candidates = new[]
        {
            new ScreenCandidate("outside", CandidateKind.Artifact, "outside"),
            new ScreenCandidate("target", CandidateKind.Artifact, "a")
        };
        var result = new RecommendationEngine().Recommend(plan, state, Snapshot(screen: ScreenKind.ArtifactReward, candidates: candidates));
        Assert.Equal("target", result.Action!.TargetToken);
        Assert.Contains("1회 남음", result.Message);

        var outsideOnly = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.ArtifactReward, candidates: new[] { candidates[0] }));
        Assert.Null(outsideOnly.Action);
        Assert.Contains("수동 결정", outsideOnly.Message);
    }

    [Fact]
    public void RerollOrderIsFreeThenPaidThenAbandon()
    {
        var plan = Plan(); var state = ActiveBuildState.Activate(plan, Snapshot());
        var all = new[]
        {
            new ScreenCandidate("abandon", CandidateKind.AbandonOrConvert, null),
            new ScreenCandidate("paid", CandidateKind.Reroll, null, diceCost: 1),
            new ScreenCandidate("free", CandidateKind.Reroll, null, isFreeReroll: true)
        };
        Assert.Equal("free", new RecommendationEngine().Recommend(plan, state, Snapshot(screen: ScreenKind.ArtifactReward, candidates: all)).Action!.TargetToken);
        Assert.Equal("paid", new RecommendationEngine().Recommend(plan, state, Snapshot(screen: ScreenKind.ArtifactReward, candidates: all[..2])).Action!.TargetToken);
        Assert.Equal("abandon", new RecommendationEngine().Recommend(plan, state, Snapshot(screen: ScreenKind.ArtifactReward, candidates: all[..1])).Action!.TargetToken);
    }

    [Fact]
    public void LastSharedDieWarnsBeforeMiracleFreeItemRerollDoesNot()
    {
        var plan = Plan(); var state = ActiveBuildState.Activate(plan, Snapshot());
        var paid = new[] { new ScreenCandidate("reroll", CandidateKind.Reroll, null, diceCost: 1) };
        var warning = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.ArtifactReward, candidates: paid, dice: 1));
        Assert.Equal(DiceRisk.LastSharedDieSpentBeforeMiracle, warning.Action!.DiceRisk);

        var free = new[] { new ScreenCandidate("free", CandidateKind.Reroll, null, diceCost: 0, isFreeReroll: true) };
        var noWarning = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.ArtifactReward, candidates: free, dice: 1));
        Assert.Equal(DiceRisk.None, noWarning.Action!.DiceRisk);

        state.MarkMiracleAcquired();
        var cleared = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.ArtifactReward, candidates: paid, dice: 1));
        Assert.Equal(DiceRisk.None, cleared.Action!.DiceRisk);
    }

    [Fact]
    public void ShopStillProposesGoalWhenMoneyIsInsufficient()
    {
        var plan = Plan(); var state = ActiveBuildState.Activate(plan, Snapshot());
        var result = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.Shop, candidates: new[] { new ScreenCandidate("buy", CandidateKind.Artifact, "a", moneyCost: 50) }, money: 1));
        Assert.Equal(ActionKind.Buy, result.Action!.Kind);
        Assert.Contains("재화 부족", result.Message);
    }

    [Fact]
    public void WeaponTakesNextChildOrRerolls()
    {
        var plan = Plan(weapon: new[] { "root", "child", "final" });
        var state = ActiveBuildState.Activate(plan, Snapshot());
        var select = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.WeaponUpgrade, weapon: "root",
                candidates: new[] { new ScreenCandidate("child-button", CandidateKind.Weapon, "child") }));
        Assert.Equal("child-button", select.Action!.TargetToken);

        var reroll = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.WeaponUpgrade, weapon: "root",
                candidates: new[] { new ScreenCandidate("reroll", CandidateKind.Reroll, null, diceCost: 1) }));
        Assert.Equal(ActionKind.Reroll, reroll.Action!.Kind);
    }

    [Fact]
    public void MiracleTargetHasPriorityAndAcquisitionClearsWarningPolicy()
    {
        var plan = Plan(); var state = ActiveBuildState.Activate(plan, Snapshot());
        var result = new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.MiracleChoice, candidates: new[]
            {
                new ScreenCandidate("other", CandidateKind.Miracle, "other"),
                new ScreenCandidate("target", CandidateKind.Miracle, "miracle-target")
            }));
        Assert.Equal("target", result.Action!.TargetToken);
        state.MarkMiracleAcquired();
        Assert.Null(new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.MiracleChoice)).Action);
    }

    [Fact]
    public void MultiplayerForeignPlayerAndPendingRequestHaveNoAction()
    {
        var plan = Plan(); var state = ActiveBuildState.Activate(plan, Snapshot());
        var candidate = new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") };
        Assert.Null(new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.ArtifactReward, candidates: candidate, owned: false)).Action);
        Assert.Null(new RecommendationEngine().Recommend(plan, state,
            Snapshot(screen: ScreenKind.ArtifactReward, candidates: candidate, pending: true)).Action);
    }
}
