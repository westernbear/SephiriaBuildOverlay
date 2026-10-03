using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class InventoryBatchExecutorTests
{
    [Fact]
    public async Task OneAuthorizationRunsFullBoardCycleSwapsAndThreeRotationsThroughExecutorReceipts()
    {
        var gateway = new PlacementGateway(); var executor = new ConfirmedActionExecutor(gateway);
        var batch = new InventoryPlacementBatch(); var build = Guid.NewGuid();
        var goal = new BoardLayout(new Dictionary<string, GridPoint> { ["a"] = new(1, 0), ["b"] = new(2, 0), ["t"] = new(0, 0) }, new Dictionary<string, int> { ["t"] = 3 });
        batch.Start(build, gateway.Snapshot(), 0);
        for (var time = 0; time < 10 && batch.Active; time++)
        {
            var input = gateway.Input(); var step = BoardOptimizationStep.Next(input, goal);
            var snapshot = gateway.Prepare(step);
            var action = step is null ? null : new RecommendedAction(step.Rotation.HasValue ? ActionKind.Rotate : ActionKind.Move,
                "opt:step:" + snapshot.Revision, "", snapshot.Identity);
            var next = batch.Next(build, snapshot, true, "same-model", gateway.Layout, goal, action, step?.Expected, false, null, time);
            if (next is null) continue;
            var pending = executor.ConfirmOnceAsync(next);
            Assert.Null(batch.Next(build, snapshot, true, "same-model", gateway.Layout, goal, action, step?.Expected, false, null, time));
            batch.Acknowledge(await pending, time + .01);
        }
        Assert.False(batch.Active); Assert.Equal("자동배치 완료", batch.EndReason);
        Assert.True(BoardPlanContinuation.Same(goal, gateway.Layout));
        Assert.Equal(5, gateway.Sends); Assert.Equal(5, gateway.Receipts); Assert.Equal(5, batch.CompletedSteps);
        Assert.Equal(10, gateway.Snapshot().Money); Assert.Equal(2, gateway.Snapshot().SharedDice);
    }

    private sealed class PlacementGateway : IGameActionGateway
    {
        public BoardLayout Layout { get; private set; } = new(new Dictionary<string, GridPoint> { ["a"] = new(0, 0), ["b"] = new(1, 0), ["t"] = new(2, 0) }, new Dictionary<string, int> { ["t"] = 0 });
        private long _revision;
        private BoardOptimizationStep? _step;
        public int Sends { get; private set; }
        public int Receipts { get; private set; }
        public RunSnapshot Snapshot() => new("run", "local", _revision, ScreenKind.Inventory,
            _step is null ? Array.Empty<ScreenCandidate>() : new[] { new ScreenCandidate("opt:step:" + _revision, CandidateKind.Item, null) },
            Array.Empty<InventoryArtifact>(), null, null, 10, 2);
        public RunSnapshot Prepare(BoardOptimizationStep? step) { _step = step; return Snapshot(); }
        public BoardOptimizationInput Input() => new(3, 1, 3, Enumerable.Range(0, 3).Select(x => new BoardCell(new(x, 0), 0)),
            new[] { new BoardArtifact("a", "a", Layout.Positions["a"], 5, 0, true), new BoardArtifact("b", "b", Layout.Positions["b"], 5, 0, true) },
            new[] { new BoardTablet("t", "2100", Layout.Positions["t"], Layout.Rotations["t"], true, Array.Empty<BoardTabletOption>()) }, Layout.Positions,
            Array.Empty<SephiriaBuildOverlay.Core.Models.ArtifactTarget>());
        public Task<RunSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot());
        public Task<GameActionReceipt> SendThroughNormalRequestPathAsync(RecommendedAction action, CancellationToken cancellationToken)
        { Assert.Equal(Sends, Receipts); Assert.True(InventoryPlacementBatch.Eligible(action)); Sends++; return Task.FromResult(new GameActionReceipt(Sends.ToString(), true)); }
        public async Task<bool> WaitForServerConfirmationAsync(GameActionReceipt receipt, CancellationToken cancellationToken)
        { await Task.Delay(1, cancellationToken); Layout = _step!.Expected; _revision++; Receipts++; return true; }
    }
}
