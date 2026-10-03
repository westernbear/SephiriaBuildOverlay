using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class BoardContinuationTests
{
    private static BoardLayout Layout(int a, int b, int rotation = 0) => new(
        new Dictionary<string, GridPoint> { ["a"] = new(a, 0), ["b"] = new(b, 0) },
        new Dictionary<string, int> { ["t"] = rotation });
    private static BoardOptimizationResult Result(BoardLayout goal) => new(goal, default, new BoardObjective(1, 3, 0, 0, 2, 3), 20, false);

    [Fact]
    public void IntermediateMoveAndThreeRotationsKeepOriginalGoalAfterMissingSnapshots()
    {
        var session = new BoardPlanContinuation();
        var start = Layout(0, 1); var goal = Layout(1, 0, 3); var result = Result(goal);
        session.Hold("run/player/model", start, result);
        var current = Layout(1, 0);
        session.Expect(current);
        // During missing or inconsistent snapshots, no Resume is called and
        // no action can be sent. A later validated snapshot resumes the goal.
        Assert.Same(result, session.Resume("run/player/model", start));
        Assert.Same(result, session.Resume("run/player/model", current));
        for (var angle = 1; angle <= 3; angle++)
        {
            current = Layout(1, 0, angle); session.Expect(current);
            if (angle < 3) Assert.Same(result, session.Resume("run/player/model", current));
            else Assert.Null(session.Resume("run/player/model", current));
        }
    }

    [Theory]
    [InlineData("other run/player/model", 0, 1)]
    [InlineData("run/player/changed effects", 0, 1)]
    [InlineData("run/player/model", 2, 1)]
    public void UnrelatedChangeDiscardsPlan(string invariant, int a, int b)
    {
        var session = new BoardPlanContinuation();
        session.Hold("run/player/model", Layout(0, 1), Result(Layout(1, 0, 3)));
        session.Expect(Layout(1, 0));
        Assert.Null(session.Resume(invariant, Layout(a, b)));
        Assert.Null(session.Resume("run/player/model", Layout(1, 0)));
    }

    [Fact]
    public void TimeoutOrRejectedRequestClearsExpectedStep()
    {
        var session = new BoardPlanContinuation();
        session.Hold("same", Layout(0, 1), Result(Layout(1, 0, 3)));
        session.Expect(Layout(1, 0)); session.Clear();
        Assert.Null(session.Resume("same", Layout(1, 0)));
    }

    [Fact]
    public void NewOptimizationCanStartAfterPreviousGoalCompletes()
    {
        var session = new BoardPlanContinuation();
        var first = Result(Layout(1, 0)); session.Hold("same", Layout(0, 1), first);
        session.Expect(first.Layout); Assert.Null(session.Resume("same", first.Layout));
        var next = Result(Layout(2, 0)); session.Hold("new item", first.Layout, next);
        Assert.Same(next, session.Resume("new item", first.Layout));
    }
}
