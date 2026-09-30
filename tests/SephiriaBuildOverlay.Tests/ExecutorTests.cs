using SephiriaBuildOverlay.Core.Runtime;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class ExecutorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task NonOwnedPlayerOrPendingGameRequestNeverSends(bool owned, bool pending)
    {
        var snapshot = Snapshot(candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var current = new RunSnapshot(snapshot.RunId, snapshot.LocalPlayerId, snapshot.Revision, snapshot.Screen,
            snapshot.Candidates, snapshot.Inventory, null, null, 0, 0, owned, pending);
        var gateway = new FakeGateway(current);
        var result = await new ConfirmedActionExecutor(gateway).ConfirmOnceAsync(Action(current));
        Assert.Equal(owned ? ActionExecutionStatus.Busy : ActionExecutionStatus.Rejected, result.Status);
        Assert.Equal(0, gateway.SendCount);
    }

    [Fact]
    public async Task ExplicitRequestRejectionDoesNotWaitOrRetry()
    {
        var snapshot = Snapshot(candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var gateway = new FakeGateway(snapshot) { AcceptRequest = false };
        var result = await new ConfirmedActionExecutor(gateway).ConfirmOnceAsync(Action(snapshot));
        Assert.Equal(ActionExecutionStatus.Rejected, result.Status);
        Assert.Equal(1, gateway.SendCount);
        Assert.Equal(0, gateway.WaitCount);
    }

    [Fact]
    public async Task CancellationBeforeConfirmationSendsNothingAndReleasesLatch()
    {
        var snapshot = Snapshot(candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var gateway = new FakeGateway(snapshot);
        var executor = new ConfirmedActionExecutor(gateway);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ConfirmOnceAsync(Action(snapshot), cancellation.Token));
        Assert.Equal(0, gateway.SendCount);
        Assert.True((await executor.ConfirmOnceAsync(Action(snapshot))).Succeeded);
    }

    [Fact]
    public async Task ChangedLocalPlayerAndUnselectableCandidateFailBeforeSend()
    {
        var snapshot = Snapshot(candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a", isSelectable: false) });
        var gateway = new FakeGateway(snapshot);
        var executor = new ConfirmedActionExecutor(gateway);
        Assert.Equal(ActionExecutionStatus.Stale, (await executor.ConfirmOnceAsync(Action(snapshot))).Status);
        var otherPlayer = new RecommendedAction(ActionKind.Select, "target", "", new SnapshotIdentity(snapshot.RunId, "other-player", snapshot.Revision));
        Assert.Equal(ActionExecutionStatus.Stale, (await executor.ConfirmOnceAsync(otherPlayer)).Status);
        Assert.Equal(0, gateway.SendCount);
    }

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

    [Fact]
    public async Task PluginShutdownCancelsServerWaitAndPreventsNextSend()
    {
        var snapshot = Snapshot(screen: ScreenKind.ArtifactReward,
            candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var gateway = new FakeGateway(snapshot) { HoldConfirmation = true };
        var executor = new ConfirmedActionExecutor(gateway);
        using var lifetime = new SephiriaBuildOverlay.Plugin.PluginLifetime();
        var pending = executor.ConfirmOnceAsync(Action(snapshot), lifetime.Token);
        await gateway.Sent.Task;
        lifetime.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ConfirmOnceAsync(Action(snapshot), lifetime.Token));
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
        public bool AcceptRequest { get; set; } = true;
        public int WaitCount { get; private set; }
        public TaskCompletionSource<bool> Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RunSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }
        public Task<GameActionReceipt> SendThroughNormalRequestPathAsync(RecommendedAction action, CancellationToken cancellationToken)
        {
            SendCount++; Sent.TrySetResult(true);
            return Task.FromResult(new GameActionReceipt("request", AcceptRequest, "test rejection"));
        }
        public Task<bool> WaitForServerConfirmationAsync(GameActionReceipt receipt, CancellationToken cancellationToken)
        {
            WaitCount++;
            return HoldConfirmation ? _confirmation.Task.WaitAsync(cancellationToken) : Task.FromResult(true);
        }
        public void Confirm() => _confirmation.TrySetResult(true);
    }
}
