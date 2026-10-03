using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class BoardOptimizationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ZeroSlotBeatsNegativeEvenWhenActivationAndDisplayedLevelsAreTied(bool exact, bool inactiveCondition)
    {
        var condition = inactiveCondition ? ArtifactCondition.External : ArtifactCondition.None;
        var a = new BoardArtifact("a", "ice", P(0), 5, 0, true, condition, conditionActive: condition != ArtifactCondition.External);
        var input = Board(2, 1, new[] { a }, Array.Empty<BoardTablet>(), new[] { Goal("ice") },
            new[] { new BoardCell(P(0), -2), new BoardCell(P(1), 0) });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: exact);
        Assert.True(result.Improved); Assert.Equal(P(1), result.Layout.Positions["a"]);
        Assert.Equal(2, result.Before.NegativePenalty); Assert.Equal(0, result.After.NegativePenalty);
        Assert.NotNull(BoardOptimizationStep.Next(input, result.Layout));
    }

    [Fact]
    public void CompensatedNegativeSlotStillLosesToZeroWhenBothLevelsAlreadySufficient()
    {
        var a = new BoardArtifact("a", "ice", P(0), 1, 3, true);
        var input = Board(2, 1, new[] { a }, Array.Empty<BoardTablet>(), new[] { Goal("ice") },
            new[] { new BoardCell(P(0), -2), new BoardCell(P(1), 0) });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: false);
        Assert.Equal(result.Before.RequiredLevel, result.After.RequiredLevel);
        Assert.True(result.Improved); Assert.Equal(P(1), result.Layout.Positions["a"]);
    }

    [Fact]
    public void NegativePenaltyDoesNotOverrideRequiredActivationAndZeroToZeroDoesNotChurn()
    {
        Assert.True(new BoardObjective(1, 0, 0, 0, 4, 0, negativePenalty: 20).CompareBenefits(new(0, 0, 0, 0, 0, 0)) > 0);
        var a = new BoardArtifact("a", "ice", P(0), 5, 0, true);
        var input = Board(2, 1, new[] { a }, Array.Empty<BoardTablet>(), new[] { Goal("ice") });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: false);
        Assert.False(result.Improved); Assert.Equal(P(0), result.Layout.Positions["a"]);
    }
    private static GridPoint P(int x, int y = 0) => new(x, y);
    private static ArtifactTarget Goal(string key, TargetRole role = TargetRole.Required, int count = 1) => new(key, count, role, 0);
    private static BoardOptimizationInput Board(int width, int height, BoardArtifact[] artifacts, BoardTablet[] tablets,
        ArtifactTarget[] goals, BoardCell[]? cells = null, Dictionary<string, GridPoint>? items = null)
    {
        items ??= artifacts.ToDictionary(x => x.Id, x => x.Position);
        foreach (var t in tablets.Where(x => x.Movable)) items[t.Id] = t.Position;
        cells ??= Enumerable.Range(0, width * height).Select(i => new BoardCell(P(i % width, i / width), 0)).ToArray();
        return new BoardOptimizationInput(width, height, width * height, cells, artifacts, tablets, items, goals);
    }
    private static BoardTabletOption Option(GridPoint p, int angle, GridPoint target, int level, params BoardCondition[] conditions) =>
        new(p, angle, new[] { new BoardEffect(target, BoardEffectKind.Add, level) }, conditions);

    [Fact]
    public void RotatesNativeQuarterTurnsForRequiredLevelAndDoesNotMoveFixedEngraving()
    {
        var a = new BoardArtifact("a", "ice", P(1), 5, 0, false);
        var tablet = new BoardTablet("t", "tablet", P(0), 0, true, new[] { Option(P(0), 0, P(1), -1), Option(P(0), 1, P(1), 3) });
        var fixedTablet = new BoardTablet("engraved", "fixed", P(0), 0, false, new[] { Option(P(0), 0, P(1), 1) });
        var input = Board(2, 1, new[] { a }, new[] { tablet, fixedTablet }, new[] { Goal("ice") });
        var result = new JointBoardPlanner().Solve(input);
        Assert.True(result.Improved);
        Assert.Equal(1, result.Layout.Rotations["t"]);
        Assert.Equal(0, result.Layout.Rotations["engraved"]);
        Assert.Equal(P(0), result.Layout.Positions["engraved"]);
        Assert.Equal(4, result.After.RequiredLevel);
    }

    [Fact]
    public void TabletMovementPreservesEveryInstanceAndCanUseOccupiedSlot()
    {
        var a = new BoardArtifact("a", "ice", P(2), 5, 0, true);
        var t = new BoardTablet("t", "tablet", P(0), 0, true, new[] { Option(P(0), 0, P(1), 0), Option(P(2), 0, P(0), 4) });
        var pinned = new Dictionary<string, GridPoint> { ["a"] = P(2), ["locked"] = P(1) };
        var input = Board(3, 1, new[] { a }, new[] { t }, new[] { Goal("ice") }, items: pinned);
        var result = new JointBoardPlanner().Solve(input);
        Assert.True(result.Improved);
        Assert.Equal(P(2), result.Layout.Positions["t"]);
        Assert.Equal(P(0), result.Layout.Positions["a"]);
        Assert.Equal(P(1), result.Layout.Positions["locked"]);
        var step = Assert.IsType<BoardOptimizationStep>(BoardOptimizationStep.Next(input, result.Layout));
        Assert.Equal("a", step.SwappedId);
        Assert.Null(step.Rotation);
        Assert.Equal(3, step.Expected.Positions.Values.Distinct().Count());
    }

    [Fact]
    public void RequiredActivationOutranksManyRecommendedLevelsAndMovement()
    {
        var a = new BoardObjective(1, 0, 0, 0, 1000, 3);
        var b = new BoardObjective(0, 0, 50, 999999999, 0, 0);
        Assert.True(a.CompareTo(b) > 0);
    }

    [Fact]
    public void ConditionsUseAndForItemsAndOrForPlacedWorldPositions()
    {
        var a = new BoardArtifact("a", "ice", P(1), 9, 0, false);
        var conditions = new[] { new BoardCondition(P(1), BoardConditionKind.Charm), new BoardCondition(P(0), BoardConditionKind.Placed), new BoardCondition(P(2), BoardConditionKind.Placed) };
        var t = new BoardTablet("t", "tablet", P(0), 0, true, new[] { Option(P(0), 0, P(1), 4, conditions), Option(P(2), 0, P(1), 4, conditions) });
        var input = Board(3, 1, new[] { a }, new[] { t }, new[] { Goal("ice") });
        Assert.Equal(4, JointBoardPlanner.Evaluate(input, JointBoardPlanner.Current(input)).RequiredLevel);
        var bad = new BoardTablet("t", "tablet", P(0), 0, true, new[] { Option(P(0), 0, P(1), 4, new BoardCondition(P(2), BoardConditionKind.AnyItem)) });
        var blocked = Board(3, 1, new[] { a }, new[] { bad }, new[] { Goal("ice") });
        Assert.Equal(0, JointBoardPlanner.Evaluate(blocked, JointBoardPlanner.Current(blocked)).RequiredLevel);
    }

    [Fact]
    public void MultiplierAddsBeforeMultiplyingAndEnchantTravelsWithInstance()
    {
        var a = new BoardArtifact("a", "ice", P(1), 20, 2, true);
        var t = new BoardTablet("t", "tablet", P(0), 0, true, new[] { new BoardTabletOption(P(0), 0, new[]
        { new BoardEffect(P(1), BoardEffectKind.Add, 3), new BoardEffect(P(1), BoardEffectKind.Multiply, 2) }) });
        var input = Board(3, 1, new[] { a }, new[] { t }, new[] { Goal("ice") });
        var current = JointBoardPlanner.Current(input);
        Assert.Equal(10, JointBoardPlanner.Cells(input, current).Single(x => x.Position.Equals(P(1))).Level);
        var moved = new BoardLayout(new Dictionary<string, GridPoint> { ["a"] = P(2), ["t"] = P(0) }, current.Rotations);
        Assert.Equal(6, JointBoardPlanner.Cells(input, moved).Single(x => x.Position.Equals(P(1))).Level);
        Assert.Equal(2, JointBoardPlanner.Cells(input, moved).Single(x => x.Position.Equals(P(2))).Level);
    }

    [Fact]
    public void DisableBeatsIgnoreAndIgnoreBypassesArtifactButNotWeaponCondition()
    {
        var a = new BoardArtifact("a", "ice", P(1), 5, 0, false, ArtifactCondition.Top);
        var t = new BoardTablet("t", "tablet", P(0), 0, true, new[] { new BoardTabletOption(P(0), 0, new[]
        { new BoardEffect(P(1), BoardEffectKind.IgnoreCriteria), new BoardEffect(P(1), BoardEffectKind.Disable), new BoardEffect(P(1), BoardEffectKind.Add, 5) }) });
        var input = Board(2, 1, new[] { a }, new[] { t }, new[] { Goal("ice") });
        Assert.Equal(0, JointBoardPlanner.Evaluate(input, JointBoardPlanner.Current(input)).RequiredActive);
        var wrongWeapon = new BoardArtifact("a", "ice", P(1), 5, 0, false, externalActive: false);
        var weaponInput = Board(2, 1, new[] { wrongWeapon }, Array.Empty<BoardTablet>(), new[] { Goal("ice") }, new[] { new BoardCell(P(0), 0), new BoardCell(P(1), 5, ignore: 1) });
        Assert.Equal(0, JointBoardPlanner.Evaluate(weaponInput, JointBoardPlanner.Current(weaponInput)).RequiredActive);
    }

    [Fact]
    public void MatchingPreservesDuplicatesAndDifferentKeysHaveUniqueDestinations()
    {
        var artifacts = new[] { new BoardArtifact("one", "a", P(0), 5, 0, true), new BoardArtifact("two", "a", P(1), 5, 0, true), new BoardArtifact("three", "b", P(2), 5, 0, true) };
        var cells = Enumerable.Range(0, 6).Select(x => new BoardCell(P(x), x)).ToArray();
        var input = Board(6, 1, artifacts, Array.Empty<BoardTablet>(), new[] { Goal("a", count: 2), Goal("b", TargetRole.Recommended) }, cells);
        var first = new JointBoardPlanner().Solve(input); var second = new JointBoardPlanner().Solve(input);
        Assert.True(first.Improved);
        Assert.Equal(3, first.Layout.Positions.Values.Distinct().Count());
        Assert.Equal(first.Layout.Positions.OrderBy(x => x.Key), second.Layout.Positions.OrderBy(x => x.Key));
        Assert.Equal(9, first.After.RequiredLevel);
    }

    [Fact]
    public void FindsJointImprovementWhenNeitherTabletNorArtifactMoveAloneHelps()
    {
        var a = new BoardArtifact("a", "ice", P(2), 5, 0, true);
        var t = new BoardTablet("t", "tablet", P(0), 0, true, new[] { Option(P(0), 0, P(1), 0), Option(P(0), 1, P(1), 5) });
        var input = Board(3, 1, new[] { a }, new[] { t }, new[] { Goal("ice") });
        var result = new JointBoardPlanner().Solve(input);
        Assert.True(result.Improved); Assert.Equal(1, result.Layout.Rotations["t"]); Assert.Equal(P(1), result.Layout.Positions["a"]);
        Assert.Equal(5, result.After.RequiredLevel);
    }

    [Fact]
    public void UnknownCriteriaRefusesAutomaticOptimization()
    {
        var input = Board(2, 1, new[] { new BoardArtifact("a", "a", P(0), 5, 0, true, ArtifactCondition.Unknown) }, Array.Empty<BoardTablet>(), new[] { Goal("a") });
        var result = new JointBoardPlanner().Solve(input);
        Assert.False(result.Improved); Assert.NotNull(result.Unavailable); Assert.Equal(0, result.Evaluations);
    }

    [Fact]
    public void BudgetIsDeterministicAndNeverReturnsWorseThanCurrent()
    {
        var input = Board(6, 6, new[] { new BoardArtifact("a", "a", P(0), 5, 0, true) }, Array.Empty<BoardTablet>(), new[] { Goal("a") },
            Enumerable.Range(0, 36).Select(i => new BoardCell(P(i % 6, i / 6), i % 6)).ToArray());
        var result = new JointBoardPlanner().Solve(input, evaluationBudget: 1);
        Assert.True(result.BudgetReached); Assert.Equal(1, result.Evaluations); Assert.True(result.After.CompareTo(result.Before) >= 0);
    }

    [Fact]
    public void CancellationStopsBackgroundSearch()
    {
        var input = Board(2, 1, new[] { new BoardArtifact("a", "a", P(0), 5, 0, true) }, Array.Empty<BoardTablet>(), new[] { Goal("a") });
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => new JointBoardPlanner().Solve(input, cancel.Token));
    }

    [Theory]
    [InlineData(0, 3, 1)] [InlineData(3, 1, 0)] [InlineData(2, 1, 3)]
    public void AStepIsExactlyOneForwardQuarterTurnEvenIfTargetIsThreeStepsAway(int current, int target, int expected)
    {
        var options = Enumerable.Range(0, 4).Select(x => Option(P(0), x, P(1), x)).ToArray();
        var t = new BoardTablet("t", "tablet", P(0), current, true, options);
        var input = Board(2, 1, Array.Empty<BoardArtifact>(), new[] { t }, Array.Empty<ArtifactTarget>());
        var layout = new BoardLayout(new Dictionary<string, GridPoint> { ["t"] = P(0) }, new Dictionary<string, int> { ["t"] = target });
        var step = Assert.IsType<BoardOptimizationStep>(BoardOptimizationStep.Next(input, layout));
        Assert.Equal(current, step.Rotation); Assert.Equal(expected, step.Expected.Rotations["t"]); Assert.Equal(P(0), step.Expected.Positions["t"]);
    }

    [Fact]
    public void FullBoardSwapCycleConvergesWithoutRemovingItems()
    {
        var artifacts = new[] { new BoardArtifact("a", "a", P(0), 3, 0, true), new BoardArtifact("b", "b", P(1), 3, 0, true), new BoardArtifact("c", "c", P(2), 3, 0, true) };
        var target = new BoardLayout(new Dictionary<string, GridPoint> { ["a"] = P(1), ["b"] = P(2), ["c"] = P(0) }, new Dictionary<string, int>());
        var input = Board(3, 1, artifacts, Array.Empty<BoardTablet>(), Array.Empty<ArtifactTarget>());
        for (var i = 0; i < 2; i++)
        {
            var step = Assert.IsType<BoardOptimizationStep>(BoardOptimizationStep.Next(input, target));
            artifacts = artifacts.Select(x => new BoardArtifact(x.Id, x.Key, step.Expected.Positions[x.Id], 3, 0, true)).ToArray();
            input = Board(3, 1, artifacts, Array.Empty<BoardTablet>(), Array.Empty<ArtifactTarget>());
            Assert.Equal(3, step.Expected.Positions.Values.Distinct().Count());
        }
        Assert.Null(BoardOptimizationStep.Next(input, target));
    }

    [Fact]
    public void PinnedItemsCannotBeDisplacedEvenWhenTheyOccupyTheBestCell()
    {
        var input = Board(2, 1, new[] { new BoardArtifact("a", "a", P(0), 5, 0, true) }, Array.Empty<BoardTablet>(), new[] { Goal("a") },
            new[] { new BoardCell(P(0), 0), new BoardCell(P(1), 5) }, new Dictionary<string, GridPoint> { ["a"] = P(0), ["pinned"] = P(1) });
        var result = new JointBoardPlanner().Solve(input);
        Assert.False(result.Improved); Assert.Equal(P(0), result.Layout.Positions["a"]); Assert.Equal(P(1), result.Layout.Positions["pinned"]);
    }

    [Fact]
    public void NeighborsAreReevaluatedAndIgnoreCriteriaCanBypassFullHpCondition()
    {
        var a = new BoardArtifact("a", "a", P(1), 5, 0, false, ArtifactCondition.BothCharms);
        var left = new BoardArtifact("left", "x", P(0), 5, 0, false);
        var right = new BoardArtifact("right", "x", P(2), 5, 0, true);
        var input = Board(4, 1, new[] { a, left, right }, Array.Empty<BoardTablet>(), new[] { Goal("a") });
        var current = JointBoardPlanner.Current(input);
        Assert.True(JointBoardPlanner.Criteria(input, current, a, P(1)));
        var moved = new BoardLayout(new Dictionary<string, GridPoint> { ["a"] = P(1), ["left"] = P(0), ["right"] = P(3) }, current.Rotations);
        Assert.False(JointBoardPlanner.Criteria(input, moved, a, P(1)));
        var fullHp = new BoardArtifact("a", "a", P(0), 5, 0, false, ArtifactCondition.External, conditionActive: false);
        var bypass = Board(1, 1, new[] { fullHp }, Array.Empty<BoardTablet>(), new[] { Goal("a") }, new[] { new BoardCell(P(0), 3, ignore: 1) });
        Assert.Equal(3, JointBoardPlanner.Evaluate(bypass, JointBoardPlanner.Current(bypass)).RequiredLevel);
    }

    [Fact]
    public void MoreVisibleFramesKeepNextActionStrongerThanOtherTargets()
    {
        Assert.True(CandidateFramePolicy.Thickness(CandidateFrameKind.Required, 1) >= 3);
        Assert.True(CandidateFramePolicy.Thickness(CandidateFrameKind.NextAction, 1) > CandidateFramePolicy.Thickness(CandidateFrameKind.Required, 1));
    }

    [Fact]
    public void TwoTabletLookaheadFindsConditionalSynergyAcrossNeutralMoves()
    {
        var a = new BoardArtifact("a", "ice", P(2), 5, 0, false);
        var first = new BoardTablet("first", "tablet", P(0), 0, true, new[] {
            Option(P(0), 0, P(2), 0), Option(P(0), 1, P(2), 5, new BoardCondition(P(4), BoardConditionKind.AnyItem)) });
        var second = new BoardTablet("second", "tablet", P(1), 0, true, new[] { Option(P(1), 0, P(2), 0), Option(P(4), 0, P(2), 0) });
        var input = Board(5, 1, new[] { a }, new[] { first, second }, new[] { Goal("ice") });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: false);
        Assert.Equal(0, new LegacyBoardPlanner().Solve(input).After.RequiredLevel);
        Assert.True(result.Improved); Assert.Equal(5, result.After.RequiredLevel);
        Assert.Equal(P(4), result.Layout.Positions["second"]); Assert.Equal(1, result.Layout.Rotations["first"]);
        Assert.InRange(result.Evaluations, 1, 6000);
    }

    [Fact]
    public void EqualTotalLevelsFavorHigherBuildPriorityWithoutSacrificingActivation()
    {
        var input = Board(3, 1, new[] { new BoardArtifact("a", "low", P(0), 5, 0, true), new BoardArtifact("b", "high", P(1), 5, 0, true) },
            Array.Empty<BoardTablet>(), new[] { new ArtifactTarget("low", 1, TargetRole.Required, 5), new ArtifactTarget("high", 1, TargetRole.Required, 0) },
            new[] { new BoardCell(P(0), 0), new BoardCell(P(1), 2), new BoardCell(P(2), 3) });
        var result = new JointBoardPlanner().Solve(input);
        Assert.Equal(P(2), result.Layout.Positions["b"]); Assert.Equal(5, result.After.RequiredLevel); Assert.Equal(2, result.After.RequiredActive);
    }

    [Fact]
    public void HiddenFirstMoveDoesNotBlockOtherVisibleStepsAndDoesNotMutateTarget()
    {
        var artifacts = new[] { new BoardArtifact("a", "a", P(0), 3, 0, true), new BoardArtifact("b", "b", P(1), 3, 0, true) };
        var input = Board(4, 1, artifacts, Array.Empty<BoardTablet>(), Array.Empty<ArtifactTarget>());
        var target = new BoardLayout(new Dictionary<string, GridPoint> { ["a"] = P(3), ["b"] = P(2) }, new Dictionary<string, int>());
        var step = Assert.IsType<BoardOptimizationStep>(BoardOptimizationStep.Next(input, target, (_, to) => to.X < 3));
        Assert.Equal("b", step.Id); Assert.Equal(P(3), target.Positions["a"]);
        Assert.Null(BoardOptimizationStep.Next(input, target, (_, _) => false));
    }
}
