using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class BoardSearchRegressionTests
{
    [Fact]
    public void AllDifferentBoundCountsRotationsSeparatelyAndPreservesPinnedSlots()
    {
        var cells = Enumerable.Range(0, 7).Select(i => new BoardCell(new(i, 0), 0)).ToArray();
        var artifacts = cells.Skip(2).Take(4).Select((c, i) => new BoardArtifact("a" + i, "key" + i, c.Position, 5, 0, true)).ToArray();
        var tablets = cells.Take(2).Select((c, i) => new BoardTablet("t" + i, "tablet", c.Position, 0, true,
            Enumerable.Range(0, 4).Select(r => new BoardTabletOption(c.Position, r,
                new[] { new BoardEffect(artifacts[0].Position, BoardEffectKind.Add, r) })))).ToArray();
        var items = artifacts.ToDictionary(a => a.Id, a => a.Position);
        foreach (var t in tablets) items[t.Id] = t.Position;
        items["pinned"] = cells[6].Position;
        var input = new BoardOptimizationInput(7, 1, 7, cells, artifacts, tablets, items,
            new[] { new ArtifactTarget(artifacts[0].Key, 1, TargetRole.Required, 0) });
        var exact = new JointBoardPlanner().Solve(input, evaluationBudget: 384);
        Assert.True(exact.ProvenOptimal);
        Assert.Equal(384, exact.Evaluations); // 4^2 rotations * 4! artifacts
        Assert.Equal(cells[6].Position, exact.Layout.Positions["pinned"]);
        Assert.False(new JointBoardPlanner().Solve(input, evaluationBudget: 383).ProvenOptimal);
    }

    [Fact]
    public void SevenOccupiedMovableSlotsUseThe5040LegalPermutationsNotSevenToTheSeventh()
    {
        var cells = Enumerable.Range(0, 7).Select(i => new BoardCell(new(i % 6, i / 6), i - 2)).ToArray();
        var artifacts = cells.Select((c, i) => new BoardArtifact("a" + i, "key" + i, c.Position, 2 + i % 3, 0, true)).ToArray();
        var input = new BoardOptimizationInput(6, 2, 7, cells, artifacts, Array.Empty<BoardTablet>(),
            artifacts.ToDictionary(a => a.Id, a => a.Position), artifacts.Select(a => new ArtifactTarget(a.Key, 1, TargetRole.Required, 0)));
        var result = new JointBoardPlanner().Solve(input);
        Assert.True(result.ProvenOptimal);
        Assert.Equal(5040, result.Evaluations);
        Assert.Equal(7, result.Layout.Positions.Values.Distinct().Count());
    }

    [Fact]
    public void ThreeNeutralTabletRotationsAreEvaluatedTogetherOnAFullLargeBoard()
    {
        var cells = Enumerable.Range(0, 24).Select(i => new BoardCell(new(i % 6, i / 6), 0)).ToArray();
        var artifacts = cells.Skip(3).Select((c, i) => new BoardArtifact("a" + i, "key" + i, c.Position, 5, 0, true)).ToArray();
        var target = artifacts[0].Position;
        var tablets = cells.Take(3).Select((c, i) => new BoardTablet("t" + i, "tablet", c.Position, 0, true,
            Enumerable.Range(0, 4).Select(r => new BoardTabletOption(c.Position, r,
                new[] { new BoardEffect(target, BoardEffectKind.Disable, r == 1 ? 0 : 1) })))).ToArray();
        // Only the pinned target matters; all other occupied slots stay present.
        artifacts[0] = new BoardArtifact(artifacts[0].Id, artifacts[0].Key, target, 5, 0, false);
        var items = artifacts.ToDictionary(a => a.Id, a => a.Position);
        foreach (var t in tablets) items[t.Id] = t.Position;
        var input = new BoardOptimizationInput(6, 4, 24, cells, artifacts, tablets, items,
            new[] { new ArtifactTarget(artifacts[0].Key, 1, TargetRole.Required, 0) });
        var result = new JointBoardPlanner().Solve(input, evaluationBudget: 6000, useExactSearch: false);
        Assert.Equal(0, result.Before.RequiredActive);
        Assert.Equal(1, result.After.RequiredActive);
        Assert.All(tablets, t => Assert.Equal(1, result.Layout.Rotations[t.Id]));
        Assert.Equal(24, result.Layout.Positions.Values.Distinct().Count());
        Assert.InRange(result.Evaluations, 1, 6000);
    }
}
