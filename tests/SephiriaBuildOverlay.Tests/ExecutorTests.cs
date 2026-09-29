using SephiriaBuildOverlay.Core.Runtime;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class ExecutorTests
{
    [Fact]
    public async Task NoMutationOccursUntilConfirmAndOneConfirmSendsOneRequest()
    {
        var snapshot = Snapshot(screen: ScreenKind.ArtifactReward,
            candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var gateway = new FakeGateway(snapshot);
        var executor = new ConfirmedActionExecutor(gateway);
        var action = Action(snapshot);
        Assert.Equal(0, gateway.SendCount);
        var result = await executor.ConfirmOnceAsync(action);
        Assert.True(result.Succeeded);
        Assert.Equal(1, gateway.SendCount);
    }

    [Fact]
    public async Task StaleRecommendationAndChangedCostAbortBeforeMutation()
    {
        var current = Snapshot(revision: 2, screen: ScreenKind.Shop,
            candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a", moneyCost: 5) });
        var gateway = new FakeGateway(current);
        var stale = new RecommendedAction(ActionKind.Buy, "target", "", Snapshot(revision: 1).Identity, moneyCost: 5);
        Assert.Equal(ActionExecutionStatus.Stale, (await new ConfirmedActionExecutor(gateway).ConfirmOnceAsync(stale)).Status);
        Assert.Equal(0, gateway.SendCount);

        var wrongCost = new RecommendedAction(ActionKind.Buy, "target", "", current.Identity, moneyCost: 4);
        Assert.Equal(ActionExecutionStatus.Stale, (await new ConfirmedActionExecutor(gateway).ConfirmOnceAsync(wrongCost)).Status);
        Assert.Equal(0, gateway.SendCount);
    }

    [Fact]
    public async Task InsufficientResourcesAndUnsafeBindingAbort()
    {
        var current = Snapshot(screen: ScreenKind.Shop, money: 1, dice: 0,
            candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a", moneyCost: 5) });
        var gateway = new FakeGateway(current);
        var expensive = new RecommendedAction(ActionKind.Buy, "target", "", current.Identity, moneyCost: 5);
        Assert.Equal(ActionExecutionStatus.Rejected, (await new ConfirmedActionExecutor(gateway).ConfirmOnceAsync(expensive)).Status);

        var unsafeAction = new RecommendedAction(ActionKind.Select, "target", "", current.Identity, automaticBindingAllowed: false);
        Assert.Equal(ActionExecutionStatus.UnsafeBinding, (await new ConfirmedActionExecutor(gateway).ConfirmOnceAsync(unsafeAction)).Status);
        Assert.Equal(0, gateway.SendCount);
    }

    [Fact]
    public async Task DuplicateConfirmIsBusyUntilServerAcknowledges()
    {
        var snapshot = Snapshot(screen: ScreenKind.ArtifactReward,
            candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var gateway = new FakeGateway(snapshot) { HoldConfirmation = true };
        var executor = new ConfirmedActionExecutor(gateway, TimeSpan.FromSeconds(2));
        var first = executor.ConfirmOnceAsync(Action(snapshot));
        await gateway.Sent.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var duplicate = await executor.ConfirmOnceAsync(Action(snapshot));
        Assert.Equal(ActionExecutionStatus.Busy, duplicate.Status);
        gateway.Confirm();
        Assert.True((await first).Succeeded);
        Assert.Equal(1, gateway.SendCount);
    }

    [Fact]
    public async Task ServerTimeoutStopsSequence()
    {
        var snapshot = Snapshot(screen: ScreenKind.ArtifactReward,
            candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var gateway = new FakeGateway(snapshot) { HoldConfirmation = true };
        var result = await new ConfirmedActionExecutor(gateway, TimeSpan.FromMilliseconds(30)).ConfirmOnceAsync(Action(snapshot));
        Assert.Equal(ActionExecutionStatus.TimedOut, result.Status);
        Assert.Equal(1, gateway.SendCount);
    }

    private static RecommendedAction Action(RunSnapshot snapshot) =>
        new(ActionKind.Select, "target", "test", snapshot.Identity);

    private sealed class FakeGateway : IGameActionGateway
    {
        private readonly RunSnapshot _snapshot;
        private readonly TaskCompletionSource<bool> _confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeGateway(RunSnapshot snapshot) => _snapshot = snapshot;
        public int SendCount { get; private set; }
        public bool HoldConfirmation { get; set; }
        public TaskCompletionSource<bool> Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RunSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(_snapshot);
        public Task<GameActionReceipt> SendThroughNormalRequestPathAsync(RecommendedAction action, CancellationToken cancellationToken)
        {
            SendCount++; Sent.TrySetResult(true);
            return Task.FromResult(new GameActionReceipt("request", true));
        }
        public Task<bool> WaitForServerConfirmationAsync(GameActionReceipt receipt, CancellationToken cancellationToken) =>
            HoldConfirmation ? _confirmation.Task.WaitAsync(cancellationToken) : Task.FromResult(true);
        public void Confirm() => _confirmation.TrySetResult(true);
    }
}
