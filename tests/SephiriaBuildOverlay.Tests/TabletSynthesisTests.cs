using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class TabletSynthesisTests
{
    private static GridPoint P(int x) => new(x, 0);
    private static BoardOptimizationInput Board(bool secondRotatable = true, bool harmful = false, bool pinned = false)
    {
        BoardTablet Tablet(string id, int position, bool rotatable, int level) => new(id, id, P(position), 0, !pinned,
            Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, rotatable ? 4 : 1).Select(r => new BoardTabletOption(P(x), r,
                new[] { new BoardEffect(P(3), BoardEffectKind.Add, level) }))));
        var a = new BoardArtifact("a", "ice", P(3), 20, 0, false);
        return new BoardOptimizationInput(4, 1, 4, Enumerable.Range(0, 4).Select(x => new BoardCell(P(x), 0)), new[] { a },
            new[] { Tablet("t1", 0, true, 2), Tablet("t2", 1, secondRotatable, harmful ? -5 : 3) },
            new Dictionary<string, GridPoint> { ["a"] = P(3), ["t1"] = P(0), ["t2"] = P(1) },
            new[] { new ArtifactTarget("ice", 1, TargetRole.Required, 0) });
    }

    [Fact]
    public void CombinesEffectsAndPreservesArtifactsWhileFreeingExactlyOneSlot()
    {
        var input = Board(); var recipe = new TabletSynthesisRecipe("t1", 0, "t2", 0, true);
        var merged = Assert.IsType<BoardOptimizationInput>(TabletSynthesisPlanner.Merge(input, recipe));
        Assert.Equal(input.Items.Count - 1, merged.Items.Count);
        Assert.Equal(P(3), merged.Items["a"]); Assert.Single(merged.Tablets);
        Assert.Equal(5, JointBoardPlanner.Evaluate(merged, JointBoardPlanner.Current(merged)).RequiredLevel);
        var suggestion = Assert.IsType<TabletSynthesisSuggestion>(TabletSynthesisPlanner.Recommend(input, new[] { recipe }));
        Assert.False(suggestion.LosesRotation); Assert.Contains("슬롯 1칸", suggestion.Reason);
    }

    [Fact]
    public void NativeRejectedAndDuplicateMaterialsAreNeverSuggested()
    {
        Assert.Null(TabletSynthesisPlanner.Recommend(Board(), new[] { new TabletSynthesisRecipe("t1", 0, "t2", 0, true, nativeAllowed: false) }));
        Assert.Null(TabletSynthesisPlanner.Merge(Board(), new TabletSynthesisRecipe("t1", 0, "t1", 0, true)));
        Assert.Null(TabletSynthesisPlanner.Merge(Board(), new TabletSynthesisRecipe("t1", 0, "missing", 0, true)));
    }

    [Fact]
    public void NonRotatableMaterialLocksResultAndWarnsAboutLostRotation()
    {
        var input = Board(secondRotatable: false); var recipe = new TabletSynthesisRecipe("t1", 0, "t2", 0, false);
        var merged = Assert.IsType<BoardOptimizationInput>(TabletSynthesisPlanner.Merge(input, recipe));
        Assert.All(merged.Tablets.Single().Options, o => Assert.Equal(0, o.Rotation));
        Assert.True(Assert.IsType<TabletSynthesisSuggestion>(TabletSynthesisPlanner.Recommend(input, new[] { recipe })).LosesRotation);
    }

    [Fact]
    public void FixedEngravingsAndAlreadyCustomTabletsAreNotMergeMaterials()
    {
        Assert.Null(TabletSynthesisPlanner.Merge(Board(pinned: true), new TabletSynthesisRecipe("t1", 0, "t2", 0, true)));
        var input = Board(); var changed = input.Tablets.Select(t => t.Id == "t1" ? new BoardTablet(t.Id, "2101", t.Position, t.Rotation, t.Movable, t.Options) : t);
        var custom = new BoardOptimizationInput(4, 1, 4, input.Cells, input.Artifacts, changed, input.Items, input.Goals);
        Assert.Null(TabletSynthesisPlanner.Merge(custom, new TabletSynthesisRecipe("t1", 0, "t2", 0, true)));
    }

    [Fact]
    public void UsesSharedConditionOnceRatherThanDoublingOrDroppingIt()
    {
        var input = Board();
        var tablets = input.Tablets.Select(t => new BoardTablet(t.Id, t.Key, t.Position, t.Rotation, true, t.Options.Select(o =>
            new BoardTabletOption(o.Position, o.Rotation, o.Effects, new[] { new BoardCondition(P(0), BoardConditionKind.AnyItem), new BoardCondition(P(2), BoardConditionKind.AnyItem) }))));
        var conditional = new BoardOptimizationInput(4, 1, 4, input.Cells, input.Artifacts, tablets, input.Items, input.Goals);
        var merged = Assert.IsType<BoardOptimizationInput>(TabletSynthesisPlanner.Merge(conditional, new TabletSynthesisRecipe("t1", 0, "t2", 0, true)));
        Assert.All(merged.Tablets.Single().Options, o => Assert.Equal(2, o.Conditions.Count));
        Assert.Equal(0, JointBoardPlanner.Evaluate(merged, JointBoardPlanner.Current(merged)).RequiredLevel);
        Assert.Null(TabletSynthesisPlanner.Recommend(conditional, new[] { new TabletSynthesisRecipe("t1", 0, "t2", 0, true) }));
    }

    [Fact]
    public void RecommendationsAreDeterministicAndCancelable()
    {
        var input = Board(); var recipes = new[] { new TabletSynthesisRecipe("t1", 0, "t2", 0, true) };
        Assert.Equal(TabletSynthesisPlanner.Recommend(input, recipes)!.Result.After.RequiredLevel, TabletSynthesisPlanner.Recommend(input, recipes)!.Result.After.RequiredLevel);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => TabletSynthesisPlanner.Recommend(input, recipes, cts.Token));
        Assert.Null(TabletSynthesisPlanner.Recommend(input, recipes, evaluationBudget: 0));
    }
}
