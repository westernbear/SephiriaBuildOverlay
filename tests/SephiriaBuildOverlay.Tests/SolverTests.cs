using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Core.Runtime;

namespace SephiriaBuildOverlay.Tests;

public sealed class SolverTests
{
    [Fact]
    public void AssignmentUsesExactLexicographicPriorityNotFixedWeights()
    {
        var artifacts = new[] { new MovableArtifact("a", "a", new GridPoint(0, 0)) };
        var result = new ArtifactAssignmentSolver().Solve(artifacts, new[]
        {
            new ArtifactDestination(new GridPoint(1, 0), "a", 1, 0, 0, 0),
            new ArtifactDestination(new GridPoint(0, 0), "a", 0, 0, 100001, 0)
        });
        Assert.Equal(new GridPoint(1, 0), Assert.Single(result.Assignments).To);
        Assert.Equal(0, result.UnmatchedRequired);
    }

    [Fact]
    public void FixedEngravingCannotMoveOrDisappearForBetterEffect()
    {
        var board = new TabletBoard(3, 1, null, new[] { new TabletBoardArtifact(new GridPoint(2, 0), "ice") });
        var piece = new TabletPiece("fixed", new[] { new GridPoint(0, 0) }, new[] { 0, 1 },
            new[] { new TabletEffect("ice", 1, true, 9) }, new GridPoint(0, 0), fixedEngraving: true);
        var result = new TabletPlacementSolver().Solve(board, new[] { piece });
        Assert.Equal(new GridPoint(0, 0), Assert.Single(result.Placements).Origin);
        Assert.Equal(0, result.Score.RequiredActivated);
    }

    [Fact]
    public void OneRequiredActivationOutranksAnyRecommendedEffectsOrMovement()
    {
        var required = new PlacementScore(1, 1, 0, 0, 9999, 9999);
        var optional = new PlacementScore(0, 9999, 9999, 9999, 0, 0);
        Assert.True(required.CompareTo(optional) > 0);
    }

    [Fact]
    public async Task BackgroundPlannerDropsStaleResult()
    {
        using var planner = new BackgroundPlanner<int>();
        var results = new List<int>();
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.ResultReady += value => { lock (results) results.Add(value); if (value == 2) ready.TrySetResult(true); };
        planner.Submit(1, (value, _) => { Thread.Sleep(60); return value; });
        planner.Submit(2, (value, _) => value);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(100);
        Assert.Equal(new[] { 2 }, results);
    }

    [Fact]
    public void TabletSolverHonorsRotationConditionalEffectsAndFixedEngraving()
    {
        var board = new TabletBoard(3, 2, null, new[]
        {
            new TabletBoardArtifact(new GridPoint(1, 0), "ice"),
            new TabletBoardArtifact(new GridPoint(1, 1), "ice")
        });
        var piece = new TabletPiece("t", new[] { new GridPoint(0, 0), new GridPoint(1, 0) }, new[] { 0, 1 },
            new[] { new TabletEffect("ice", 2, required: true, level: 3, comboValue: 2) });
        var result = new TabletPlacementSolver().Solve(board, new[] { piece });
        var placed = Assert.Single(result.Placements);
        Assert.Equal(1, placed.Rotation);
        Assert.Equal(1, result.Score.RequiredActivated);
        Assert.Equal(3, result.Score.RequiredEffectLevel);

        var fixedPiece = new TabletPiece("fixed", piece.Shape, new[] { 0, 1, 2, 3 }, piece.Effects,
            currentOrigin: new GridPoint(0, 0), currentRotation: 0, fixedEngraving: true);
        var fixedResult = new TabletPlacementSolver().Solve(board, new[] { fixedPiece });
        Assert.All(fixedResult.Placements, x => Assert.Equal(0, x.Rotation));
        Assert.Equal(0, fixedResult.Score.RequiredActivated);
    }

    [Fact]
    public void FullBoardReturnsDeterministicImpossibleState()
    {
        var board = new TabletBoard(1, 1, new[] { new GridPoint(0, 0) }, null);
        var piece = new TabletPiece("t", new[] { new GridPoint(0, 0) }, new[] { 0 }, Array.Empty<TabletEffect>());
        var first = new TabletPlacementSolver().Solve(board, new[] { piece });
        var second = new TabletPlacementSolver().Solve(board, new[] { piece });
        Assert.False(first.AllPlaced);
        Assert.Empty(first.Placements);
        Assert.Equal(first.Score.MovementCost, second.Score.MovementCost);
    }

    [Fact]
    public void ArtifactAssignmentPreservesDuplicatesAndMinimizesMovement()
    {
        var artifacts = new[]
        {
            new MovableArtifact("left", "a", new GridPoint(0, 0)),
            new MovableArtifact("right", "a", new GridPoint(4, 0)),
            new MovableArtifact("other", "b", new GridPoint(2, 0))
        };
        var destinations = new[]
        {
            new ArtifactDestination(new GridPoint(1, 0), "a", 1, 1, 0, 0),
            new ArtifactDestination(new GridPoint(3, 0), "a", 1, 1, 0, 0)
        };
        var result = new ArtifactAssignmentSolver().Solve(artifacts, destinations);
        Assert.Equal(2, result.Assignments.Count);
        Assert.Equal(2, result.TotalMovementCost);
        Assert.Equal(0, result.UnmatchedRequired);
        Assert.Equal(new GridPoint(1, 0), result.Assignments.Single(x => x.InstanceId == "left").To);
    }

    [Fact]
    public void AssignmentReportsMissingRequiredSlot()
    {
        var result = new ArtifactAssignmentSolver().Solve(
            new[] { new MovableArtifact("one", "a", new GridPoint(0, 0)) },
            new[]
            {
                new ArtifactDestination(new GridPoint(0, 0), "a", 1, 0, 0, 0),
                new ArtifactDestination(new GridPoint(1, 0), "a", 1, 0, 0, 0)
            });
        Assert.Equal(1, result.UnmatchedRequired);
    }
}
