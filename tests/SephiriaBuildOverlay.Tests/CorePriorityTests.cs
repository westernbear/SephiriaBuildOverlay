using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class CorePriorityTests
{
    [Theory]
    [InlineData(true, true)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(false, false)]
    public void CoreGetsBestSlotEvenWhenTotalLevelFalls(bool exact, bool required)
    {
        var role = required ? TargetRole.Required : TargetRole.Recommended;
        var core = new BoardArtifact("core", "core", new(0, 0), 2, 0, true);
        var secondary = new BoardArtifact("secondary", "secondary", new(1, 0), 5, 0, true);
        var artifacts = new[] { core, secondary };
        var input = new BoardOptimizationInput(2, 1, 2, new[] { new BoardCell(new(0, 0), 1), new BoardCell(new(1, 0), 5) },
            artifacts, Array.Empty<BoardTablet>(), artifacts.ToDictionary(a => a.Id, a => a.Position),
            new[] { new ArtifactTarget("core", 1, role, -100), new ArtifactTarget("secondary", 1, role, int.MaxValue) });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: exact);
        Assert.True(result.Improved); Assert.Equal(new GridPoint(1, 0), result.Layout.Positions["core"]);
        Assert.Equal(6, result.Before.RequiredLevel + result.Before.RecommendedLevel);
        Assert.Equal(3, result.After.RequiredLevel + result.After.RecommendedLevel);
        Assert.Equal(2, result.After.RequiredActive + result.After.RecommendedActive);
        Assert.Equal(exact, result.ProvenOptimal);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void RequiredActivationNeverSacrificedForCoreLevel(bool exact)
    {
        var artifacts = new[] { new BoardArtifact("core", "core", new(0, 1), 5, 0, true),
            new BoardArtifact("secondary", "secondary", new(0, 0), 5, 0, true, ArtifactCondition.Top) };
        var input = new BoardOptimizationInput(1, 2, 2, new[] { new BoardCell(new(0, 0), 5), new BoardCell(new(0, 1), 1) },
            artifacts, Array.Empty<BoardTablet>(), artifacts.ToDictionary(a => a.Id, a => a.Position),
            new[] { new ArtifactTarget("core", 1, TargetRole.Required, 0), new ArtifactTarget("secondary", 1, TargetRole.Required, 99) });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: exact);
        Assert.Equal(2, result.After.RequiredActive); Assert.Equal(new GridPoint(0, 1), result.Layout.Positions["core"]);
        Assert.Equal(new GridPoint(0, 0), result.Layout.Positions["secondary"]);
        Assert.False(result.Improved);
    }

    [Fact]
    public void SamePriorityKeepsWholeTierEfficiencyInsteadOfSourceOrIdOrder()
    {
        var artifacts = new[] { new BoardArtifact("core", "core", new(0, 0), 2, 0, true), new BoardArtifact("secondary", "secondary", new(1, 0), 5, 0, true) };
        var input = new BoardOptimizationInput(2, 1, 2, new[] { new BoardCell(new(0, 0), 1), new BoardCell(new(1, 0), 5) },
            artifacts, Array.Empty<BoardTablet>(), artifacts.ToDictionary(a => a.Id, a => a.Position),
            new[] { new ArtifactTarget("core", 1, TargetRole.Required, 7), new ArtifactTarget("secondary", 1, TargetRole.Required, 7) });
        var result = new JointBoardPlanner().Solve(input);
        Assert.False(result.Improved); Assert.Equal(6, result.After.RequiredLevel);
    }

    [Fact]
    public void CoreSpecialEffectOutranksSecondaryLevelAndPriorityCannotOverflow()
    {
        var coreEffect = new BoardObjective(2, 2, 0, 0, 1, 0, requiredCore: new long[] { 1, 1, 1000, 1, 1, 0 });
        var secondaryLevels = new BoardObjective(2, long.MaxValue, 0, 0, 0, 0, requiredCore: new long[] { 1, 1, 0, 1, long.MaxValue, 0 });
        Assert.True(coreEffect.CompareBenefits(secondaryLevels) > 0);
        Assert.True(secondaryLevels.CompareBenefits(coreEffect) < 0);
        Assert.True(new BoardObjective(3, 0, 0, 0, 0, 0).CompareBenefits(coreEffect) > 0);
    }

    [Fact]
    public void CoreVectorsAreImmutableCopies()
    {
        var values = new long[] { 1, 2, 3 };
        var objective = new BoardObjective(1, 2, 0, 0, 0, 0, requiredCore: values);
        values[1] = 100; Assert.Equal(2, objective.RequiredCore![1]);
    }

    [Fact]
    public void TabletRewardFavorsCoreImprovementOverLargerSecondaryBoost()
    {
        var artifacts = new[] { new BoardArtifact("core", "core", new(0, 0), 5, 0, false), new BoardArtifact("secondary", "secondary", new(1, 0), 5, 0, false) };
        var input = new BoardOptimizationInput(3, 1, 3, Enumerable.Range(0, 3).Select(x => new BoardCell(new(x, 0), 0)),
            artifacts, Array.Empty<BoardTablet>(), artifacts.ToDictionary(a => a.Id, a => a.Position),
            new[] { new ArtifactTarget("core", 1, TargetRole.Required, 0), new ArtifactTarget("secondary", 1, TargetRole.Required, 1) });
        TabletRewardOffer Offer(string key, GridPoint target, int boost) => new(key, key, new[] {
            new BoardTabletOption(new(2, 0), 0, new[] { new BoardEffect(target, BoardEffectKind.Add, boost) }) });
        var suggestion = TabletRewardPlanner.Recommend(input, new[] { Offer("secondary +5", new(1, 0), 5), Offer("core +1", new(0, 0), 1) });
        Assert.NotNull(suggestion); Assert.Equal("core +1", suggestion.Offer.Token); Assert.Equal(1, suggestion.After.RequiredLevel);
    }

    [Fact]
    public void HundredsOfUnownedGoalsDoNotWidenCoreCostsOrChangeLayout()
    {
        var artifact = new BoardArtifact("a", "owned", new(0, 0), 5, 0, true);
        var goals = new[] { new ArtifactTarget("owned", 1, TargetRole.Required, 1000) };
        BoardOptimizationInput Input(IEnumerable<ArtifactTarget> targets) => new(2, 1, 2,
            new[] { new BoardCell(new(0, 0), 0), new BoardCell(new(1, 0), 5) }, new[] { artifact }, Array.Empty<BoardTablet>(),
            new Dictionary<string, GridPoint> { ["a"] = artifact.Position }, targets);
        var simple = Input(goals);
        var future = Input(goals.Concat(Enumerable.Range(0, 400).Select(i => new ArtifactTarget("future" + i, 1, TargetRole.Required, i))));
        Assert.Single(future.GoalArtifacts); Assert.Equal(1, future.RequiredTiers);
        var solver = new JointBoardPlanner();
        Assert.Equal(solver.Solve(simple, useExactSearch: false).Layout.Positions["a"], solver.Solve(future, useExactSearch: false).Layout.Positions["a"]);
    }
}
