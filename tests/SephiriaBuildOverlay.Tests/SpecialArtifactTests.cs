using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class SpecialArtifactTests
{
    private static GridPoint P(int x, int y = 0) => new(x, y);
    private static BoardArtifact A(string id, GridPoint p, ArtifactPlacementEffect? e = null, bool movable = true, bool magic = false,
        ArtifactCondition condition = ArtifactCondition.None) => new(id, id, p, 5, 0, movable, condition, magic: magic, placementEffect: e);
    private static ArtifactPlacementEffect Needle(GridPoint offset) => new(ArtifactPlacementKind.Needle, offset: offset, amounts: new double[] { 10, 20, 30, 40, 50, 60 });
    private static BoardOptimizationInput Board(int width, int height, BoardArtifact[] a, BoardCell[]? cells = null,
        BoardComboModel? combos = null, Dictionary<string, GridPoint>? items = null, ArtifactTarget[]? goals = null)
        => new(width, height, width * height, cells ?? Enumerable.Range(0, width * height).Select(i => new BoardCell(P(i % width, i / width), 0)).ToArray(),
            a, Array.Empty<BoardTablet>(), items ?? a.ToDictionary(x => x.Id, x => x.Position),
            goals ?? a.Select(x => new ArtifactTarget(x.Key, 1, TargetRole.Required, 0)).ToArray(), combos: combos);
    private static BoardLayout Layout(BoardOptimizationInput input, params (string Id, GridPoint Point)[] moves)
    {
        var positions = input.Items.ToDictionary(x => x.Key, x => x.Value);
        foreach (var move in moves) positions[move.Id] = move.Point;
        return new BoardLayout(positions, new Dictionary<string, int>());
    }
    private static SpecialArtifactEvaluation State(BoardOptimizationInput b, BoardLayout? layout = null)
    {
        layout ??= JointBoardPlanner.Current(b);
        return new(b, layout, JointBoardPlanner.Cells(b, layout).ToDictionary(x => x.Position));
    }

    [Fact]
    public void NeedleFollowsEveryDirectionIncludingInactiveIntermediate()
    {
        var n = A("n", P(1, 1), Needle(P(0, -1)));
        var middle = A("middle", P(1), Needle(P(1)), condition: ArtifactCondition.External);
        middle = new BoardArtifact(middle.Id, middle.Key, middle.Position, 5, 0, true, ArtifactCondition.External, conditionActive: false, placementEffect: middle.PlacementEffect);
        var target = A("attack", P(2), new(categories: new[] { "GLACIER" }, attackable: true));
        var input = Board(3, 2, new[] { n, middle, target }); var state = State(input);
        Assert.Equal("attack", state.DirectedTarget(n)?.Id);
        Assert.Equal(new[] { "GLACIER" }, state.Categories(n));
        Assert.True(state.Value(n).Active); Assert.False(state.Value(middle).Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeedleRejectsEmptyOrNonAttackingEndpoint(bool occupied)
    {
        var n = A("n", P(0), Needle(P(1)));
        var input = Board(2, 1, occupied ? new[] { n, A("utility", P(1)) } : new[] { n });
        Assert.Null(State(input).DirectedTarget(n)); Assert.False(State(input).Value(n).Active);
        Assert.Empty(State(input).Categories(n));
    }

    [Fact]
    public void NeedleCyclesDoNotBecomeAttackersOrInventComboCategories()
    {
        var a = A("a", P(0), Needle(P(1))); var b = A("b", P(1), Needle(P(-1)));
        var input = Board(2, 1, new[] { a, b }); var state = State(input);
        Assert.Null(state.DirectedTarget(a)); Assert.Empty(state.Categories(a));
        Assert.True(state.CategoriesReliable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingNeedleTargetIsProtectedFromRetargetingAndDisable(bool retarget)
    {
        var n = A("n", P(0), Needle(P(1)));
        var target = A("target", P(1), new(attackable: true));
        var second = A("second", P(2), new(attackable: true));
        var input = Board(4, 1, new[] { n, target, second }, new[] { new BoardCell(P(0), 0), new BoardCell(P(1), 0), new BoardCell(P(2), 0), new BoardCell(P(3), -1) });
        var candidate = retarget ? Layout(input, ("target", P(2)), ("second", P(1))) : Layout(input, ("target", P(3)));
        Assert.False(SpecialArtifactRules.Allows(input, candidate));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NeedleAndTargetMoveTogetherToBetterPairWithFullOccupancy(bool exact)
    {
        var n = A("n", P(0), Needle(P(1))); var target = A("target", P(1), new(attackable: true));
        var filler = A("filler", P(2)); var last = A("last", P(3));
        var input = Board(4, 1, new[] { n, target, filler, last }, new[] { new BoardCell(P(0), 0), new BoardCell(P(1), 0), new BoardCell(P(2), 3), new BoardCell(P(3), 3) });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: exact);
        Assert.True(result.Improved); Assert.Equal(P(2), result.Layout.Positions["n"]); Assert.Equal(P(3), result.Layout.Positions["target"]);
        Assert.Equal(6, result.After.RequiredLevel); Assert.Equal(40000, result.After.RequiredEffects);
        Assert.True(SpecialArtifactRules.Allows(input, result.Layout));
    }

    [Fact]
    public void OppositeMagicSupportsTranslateWithTheirSharedSpell()
    {
        var cooldown = A("cooldown", P(0), new(ArtifactPlacementKind.CooldownSupport, offset: P(1), amounts: new double[] { 10, 20, 30, 40 }));
        var magic = A("magic", P(1), magic: true);
        var mana = A("mana", P(2), new(ArtifactPlacementKind.ManaSupport, offset: P(-1), amounts: new double[] { 5, 10, 15, 20 }));
        var input = Board(6, 1, new[] { cooldown, magic, mana }, Enumerable.Range(0, 6).Select(x => new BoardCell(P(x), x >= 3 ? 3 : 0)).ToArray());
        var result = new JointBoardPlanner().Solve(input, useExactSearch: false);
        Assert.True(result.Improved); Assert.Equal(9, result.After.RequiredLevel);
        var state = State(input, result.Layout);
        Assert.Equal("magic", state.DirectedTarget(cooldown)?.Id); Assert.Equal("magic", state.DirectedTarget(mana)?.Id);
        Assert.Equal(60000, result.After.RequiredEffects);
    }

    [Fact]
    public void OrphanMagicHelperCanFormANewUsefulConnection()
    {
        var mana = A("mana", P(0), new(ArtifactPlacementKind.ManaSupport, offset: P(-1), amounts: new double[] { 25 }));
        var magic = A("magic", P(2), magic: true);
        var input = Board(4, 1, new[] { mana, magic });
        var result = new JointBoardPlanner().Solve(input, useExactSearch: false);
        Assert.True(result.Improved); Assert.Equal(2, result.After.RequiredActive);
        Assert.Equal("magic", State(input, result.Layout).DirectedTarget(mana)?.Id);
    }

    [Fact]
    public void NeedleRarityExtraAndManaSaturationUseNativeTables()
    {
        var needle = A("needle", P(0), new(ArtifactPlacementKind.Needle, offset: P(1), amounts: new double[] { 10, 20 },
            dependencyAmounts: new double[] { 5, 15 }, dependencyCondition: true, maximumRarity: 2));
        var target = A("target", P(1), new(attackable: true, rarity: 2), magic: true);
        var mana = A("mana", P(2), new(ArtifactPlacementKind.ManaSupport, offset: P(-1), amounts: new double[] { 150 }));
        var state = State(Board(3, 1, new[] { needle, target, mana }));
        Assert.Equal(15000, state.Value(needle).Effect); Assert.Equal(100000, state.Value(mana).Effect);
    }

    [Fact]
    public void WhitePaperInheritsOnlyRepeatedHorizontalCategoriesIncludingInactiveNeighbors()
    {
        var left = A("left", P(0), new(categories: new[] { "GLACIER", "EMBER" }), condition: ArtifactCondition.External);
        var paper = A("paper", P(1), new(ArtifactPlacementKind.WhitePaper));
        var right = A("right", P(2), new(categories: new[] { "GLACIER" }));
        var above = A("above", P(1, 1), new(categories: new[] { "EMBER" }));
        var state = State(Board(3, 2, new[] { left, paper, right, above }));
        Assert.Equal(new[] { "GLACIER" }, state.Categories(paper));
        Assert.Equal(3, SpecialArtifactRules.Count(state.ComboCounts(), "GLACIER"));
        Assert.Equal(2, SpecialArtifactRules.Count(state.ComboCounts(), "EMBER"));
    }

    [Fact]
    public void PaperCyclesAreNotPredictedAsAnInventedStableFixedPoint()
    {
        var a = A("a", P(0), new(ArtifactPlacementKind.WhitePaper)); var b = A("b", P(1), new(ArtifactPlacementKind.WhitePaper));
        var input = Board(3, 1, new[] { a, b }); var state = State(input); state.ComboCounts();
        Assert.False(state.CategoriesReliable); Assert.False(SpecialArtifactRules.Allows(input, JointBoardPlanner.Current(input)));
        Assert.NotNull(new JointBoardPlanner().Solve(input).Unavailable);
    }

    [Fact]
    public void RowKeyUsesNativePeriodNotActivationAndNeedleInheritsIt()
    {
        var key = A("key", P(1, 1), new(ArtifactPlacementKind.RowCategory, attackable: true, rowCategories: new[] { "EMBER", "GLACIER", "MAGITECH" }));
        var needle = A("needle", P(0, 1), Needle(P(1)));
        var input = Board(2, 4, new[] { key, needle });
        Assert.Equal(new[] { "GLACIER" }, State(input).Categories(key)); Assert.Equal(new[] { "GLACIER" }, State(input).Categories(needle));
        Assert.Equal(new[] { "EMBER" }, State(input, Layout(input, ("key", P(1, 3)), ("needle", P(0, 3)))).Categories(key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DesiredComboBreaksALevelTieForPaperPlacement(bool exact)
    {
        var a = A("a", P(0), new(categories: new[] { "GLACIER" }), movable: false);
        var b = A("b", P(2), new(categories: new[] { "GLACIER" }), movable: false);
        var paper = A("paper", P(3), new(ArtifactPlacementKind.WhitePaper));
        var input = Board(4, 1, new[] { a, b, paper }, combos: new(new[] { "GLACIER" }));
        var result = new JointBoardPlanner().Solve(input, useExactSearch: exact);
        Assert.True(result.Improved); Assert.Equal(P(1), result.Layout.Positions["paper"]); Assert.Equal(3, result.After.Combo);
    }

    [Fact]
    public void MysticCountIsPreservedRatherThanPredictingRandomEngravings()
    {
        var key = A("key", P(0), new(ArtifactPlacementKind.RowCategory, rowCategories: new[] { "MYSTIC", "EMBER" }));
        var input = Board(2, 2, new[] { key }, Enumerable.Range(0, 4).Select(i => new BoardCell(P(i % 2, i / 2), i >= 2 ? 5 : 0)).ToArray(),
            new(new[] { "EMBER" }, new Dictionary<string, int> { ["MYSTIC"] = 2 }, new[] { "MYSTIC" }));
        var target = Layout(input, ("key", P(0, 1)));
        Assert.False(SpecialArtifactRules.Allows(input, target)); Assert.Null(BoardOptimizationStep.Next(input, target));
        Assert.False(new JointBoardPlanner().Solve(input).Improved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScalesPreserveNativeBoundaryXTwoNotHalfInventoryWidth(bool exact)
    {
        var scales = A("scales", P(2), new(preserveSide: true));
        var input = Board(8, 1, new[] { scales }, Enumerable.Range(0, 8).Select(x => new BoardCell(P(x), x >= 3 ? 5 : x == 1 ? 2 : 0)).ToArray());
        var result = new JointBoardPlanner().Solve(input, useExactSearch: exact);
        Assert.Equal(P(1), result.Layout.Positions["scales"]); Assert.False(SpecialArtifactRules.PreservesSides(input, Layout(input, ("scales", P(3)))));
    }

    [Fact]
    public void IntermediateSwapNeverMovesProtectedScalesToOppositeSide()
    {
        var scales = A("scales", P(2), new(preserveSide: true)); var other = A("a", P(4));
        var input = Board(6, 1, new[] { scales, other });
        // Moving a to 2 first would displace scales to 4. The safe ordering
        // moves scales to its left-side goal first instead.
        var step = BoardOptimizationStep.Next(input, Layout(input, ("a", P(2)), ("scales", P(1))));
        Assert.NotNull(step); Assert.Equal("scales", step.Id); Assert.True(SpecialArtifactRules.PreservesSides(input, step.Expected));
    }

    [Fact]
    public void NeighborDamageUsesDisabledDisplayedLevelAndFloorsNativeSum()
    {
        var crystal = A("crystal", P(1), new(ArtifactPlacementKind.NeighborLevels, amounts: new double[] { .75 }));
        var disabled = A("disabled", P(0)); var negative = A("negative", P(2));
        var input = Board(3, 1, new[] { crystal, disabled, negative }, new[] { new BoardCell(P(0), 4, disabled: 1), new BoardCell(P(1), 0), new BoardCell(P(2), -1) });
        Assert.Equal(2000, State(input).Value(crystal).Effect);
    }

    [Fact]
    public void TelescopeRequiresActualSummonedPlanetNotInheritedCategory()
    {
        var telescope = A("t", P(1), new(ArtifactPlacementKind.PlanetSupport));
        var planet = A("p", P(0), new(categories: new[] { "PLANET" }, summonPlanet: true));
        var paper = A("paper", P(2), new(categories: new[] { "PLANET" }));
        var input = Board(3, 1, new[] { telescope, planet, paper });
        Assert.Equal(1000, State(input).Value(telescope).Effect);
    }

    [Fact]
    public void CompanionSupportCountsFullRowNotOnlyAdjacentCells()
    {
        var badge = A("badge", P(0), new(ArtifactPlacementKind.RowCompanions));
        var companion = A("companion", P(5), new(companion: true));
        var outside = A("outside", P(0, 1), new(companion: true));
        Assert.Equal(1000, State(Board(6, 2, new[] { badge, companion, outside })).Value(badge).Effect);
    }

    [Fact]
    public void UnverifiedPinnedSupportTargetCannotBeMovedByGroupSearch()
    {
        var needle = A("n", P(0), Needle(P(1))); var target = A("target", P(1), new(attackable: true), movable: false);
        var input = Board(4, 1, new[] { needle, target });
        Assert.All(SpecialArtifactRules.DirectedCandidates(input, JointBoardPlanner.Current(input), default), x => Assert.Equal(P(1), x.Positions["target"]));
        Assert.False(new JointBoardPlanner().Solve(input).Improved);
    }

    [Theory]
    [InlineData("Charm_Basic", true)]
    [InlineData("Charm_WhitePaper", true)]
    [InlineData("Charm_3Elemental_ByRow", true)]
    [InlineData("Charm_UpCharmDamage", true)]
    [InlineData("ModdedCharm", false)]
    [InlineData(null, false)]
    public void OnlyVerifiedNativeCategoryCallbacksAreSupported(string? owner, bool supported)
        => Assert.Equal(supported, ArtifactPlacementEffect.SupportsCategoryCallback(owner));

    [Fact]
    public void SpecialMetadataIsCopiedAndCancellationBudgetStillApplies()
    {
        var table = new double[] { 10 }; var categories = new[] { "EMBER" };
        var effect = new ArtifactPlacementEffect(ArtifactPlacementKind.Needle, categories, offset: P(1), amounts: table);
        table[0] = 999; categories[0] = "MYSTIC";
        Assert.Equal(10, effect.At(0)); Assert.Equal("EMBER", effect.Categories[0]);
        var input = Board(8, 1, new[] { A("n", P(0), effect), A("target", P(1), new(attackable: true)) });
        var result = new JointBoardPlanner().Solve(input, evaluationBudget: 3, useExactSearch: false);
        Assert.InRange(result.Evaluations, 0, 3);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new JointBoardPlanner().Solve(input, canceled.Token));
    }

    [Fact]
    public void DirectedChainMovesAsAComponentNotJustTheLastPair()
    {
        var first = A("first", P(0), Needle(P(1)));
        var second = A("second", P(1), Needle(P(1)));
        var target = A("target", P(2), new(attackable: true));
        var input = Board(6, 1, new[] { first, second, target }, Enumerable.Range(0, 6).Select(x => new BoardCell(P(x), x >= 3 ? 4 : 0)).ToArray());
        var result = new JointBoardPlanner().Solve(input, useExactSearch: false);
        Assert.True(result.Improved); Assert.Equal(P(3), result.Layout.Positions["first"]); Assert.Equal(P(4), result.Layout.Positions["second"]);
        Assert.Equal(P(5), result.Layout.Positions["target"]); Assert.Equal(100000, result.After.RequiredEffects);
    }

    [Fact]
    public void SpecialQuantityCapsDoNotRewardExcessDuplicateHelpers()
    {
        var effect = new ArtifactPlacementEffect(ArtifactPlacementKind.CooldownSupport, offset: P(1), amounts: new double[] { 10 });
        var one = new BoardArtifact("one", "helper", P(0), 5, 0, true, placementEffect: effect);
        var two = new BoardArtifact("two", "helper", P(2), 5, 0, true, placementEffect: effect);
        var a = A("a", P(1), magic: true); var b = A("b", P(3), magic: true);
        var input = Board(4, 1, new[] { one, two, a, b }, goals: new[] { new ArtifactTarget("helper", 1, TargetRole.Required, 0) });
        var score = JointBoardPlanner.Evaluate(input, JointBoardPlanner.Current(input));
        Assert.Equal(1, score.RequiredActive); Assert.Equal(10000, score.RequiredEffects);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingPlanetOrCompanionSupportCannotBeStrippedForSlotLevel(bool planet)
    {
        var support = A("support", P(0), new(planet ? ArtifactPlacementKind.PlanetSupport : ArtifactPlacementKind.RowCompanions));
        var target = A("target", P(1), new(categories: new[] { "PLANET" }, summonPlanet: planet, companion: !planet));
        var input = Board(4, 2, new[] { support, target });
        Assert.False(SpecialArtifactRules.Allows(input, Layout(input, ("target", P(3, 1)))));
        Assert.True(SpecialArtifactRules.Allows(input, Layout(input, ("support", P(2, 1)), ("target", P(3, 1)))));
    }

    [Fact]
    public void NeedleToPaperCategoryChangesRespectNativePrePostRefreshOrder()
    {
        var needle = A("needle", P(1, 1), Needle(P(0, -1)));
        var paper = A("paper", P(1), new(ArtifactPlacementKind.WhitePaper, attackable: true));
        var left = A("left", P(0), new(categories: new[] { "GLACIER" }));
        var right = A("right", P(2), new(categories: new[] { "GLACIER" }));
        var input = Board(3, 3, new[] { needle, paper, left, right });
        // Removing paper's matching neighbor changes its Post category, but
        // the Pre needle would still have read the old GLACIER category.
        var target = Layout(input, ("right", P(2, 2)));
        Assert.False(SpecialArtifactRules.Allows(input, target));
        Assert.False(SpecialArtifactRules.PreservesProtectedCombos(input, target));
        Assert.Null(BoardOptimizationStep.Next(input, target));
    }

    [Fact]
    public void IntermediatePaperCycleIsRejectedEvenWithoutMysticCombo()
    {
        var one = A("one", P(0), new(ArtifactPlacementKind.WhitePaper));
        var two = A("two", P(3), new(ArtifactPlacementKind.WhitePaper));
        var input = Board(4, 1, new[] { one, two }); var target = Layout(input, ("two", P(1)));
        Assert.Null(BoardOptimizationStep.Next(input, target));
    }

    [Fact]
    public void ExactNeedleSearchMatchesIndependentProtectedPairEnumeration()
    {
        var random = new Random(319278);
        for (var iteration = 0; iteration < 80; iteration++)
        {
            var levels = Enumerable.Range(0, 5).Select(_ => random.Next(0, 7)).ToArray();
            var needle = A("needle", P(0), Needle(P(1))); var target = A("target", P(1), new(attackable: true));
            var input = Board(5, 1, new[] { needle, target }, levels.Select((v, x) => new BoardCell(P(x), v)).ToArray());
            var result = new JointBoardPlanner().Solve(input);
            // A connected, active pair has to keep this exact endpoint. The
            // independent reference enumerates geometry and native level tables
            // directly; it does not call solver scoring or validity helpers.
            var expected = Enumerable.Range(0, 4).Select(x => (Level: Math.Min(5, levels[x]) + Math.Min(5, levels[x + 1]),
                Effect: (10L + 10 * Math.Min(5, levels[x])) * 1000, Distance: Math.Abs(x) + Math.Abs(x + 1 - 1)))
                .OrderByDescending(x => x.Level).ThenByDescending(x => x.Effect).ThenBy(x => x.Distance).First();
            Assert.True(result.ProvenOptimal); Assert.Equal(expected.Level, result.After.RequiredLevel); Assert.Equal(expected.Effect, result.After.RequiredEffects);
            Assert.Equal(result.Layout.Positions["needle"].Add(P(1)), result.Layout.Positions["target"]);
        }
    }

    [Theory]
    [InlineData("Charm_AutoMagic", true)]
    [InlineData("Charm_NearMagicBullet", true)]
    [InlineData("Charm_ReduceMPCost", false)]
    [InlineData("Charm_WhitePaper", false)]
    public void UnmodeledNativeSpellLinksAreNotOrdinaryMovableStats(string type, bool manual)
        => Assert.Equal(manual, ArtifactPlacementEffect.RequiresManualPlacement(type));
}
