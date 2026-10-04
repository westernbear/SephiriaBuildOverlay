using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Plugin;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class MultiplayerTests
{
    private static RunSnapshot Capture(string session = "room-a", string player = "local", NetworkRole role = NetworkRole.Host,
        bool owned = true, long revision = 1, ScreenKind screen = ScreenKind.ArtifactReward, IReadOnlyList<InventoryArtifact>? inventory = null) =>
        new("same-seed", player, revision, screen, new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a", sourceInstanceId: "source") },
            inventory ?? Array.Empty<InventoryArtifact>(), null, null, 10, 2, owned,
            network: new NetworkContext(session, role, role != NetworkRole.Disconnected));

    [Theory]
    [InlineData(NetworkRole.Host)]
    [InlineData(NetworkRole.Client)]
    public void ServerRoleDoesNotAuthorizeOtherPlayersScreens(NetworkRole role)
    {
        var local = new object(); var foreign = new object();
        Assert.True(LocalOwnershipPolicy.Allows(true, local, local));
        Assert.False(LocalOwnershipPolicy.Allows(true, local, foreign));
        Assert.False(LocalOwnershipPolicy.Allows(false, local, local));
        Assert.False(LocalOwnershipPolicy.Allows(true, local, null));
        var plan = Plan(); var snapshot = Capture(role: role, owned: false);
        Assert.Null(new RecommendationEngine().Recommend(plan, ActiveBuildState.Activate(plan, snapshot), snapshot).Action);
    }

    [Theory]
    [InlineData("room-b", "local")]
    [InlineData("room-a", "another-character")]
    public void SameSeedCannotReuseAnotherRoomOrPlayersProgress(string session, string player)
    {
        var plan = Plan(); var first = Capture(); var prior = ActiveBuildState.Activate(plan, first);
        prior.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired);
        var after = Capture(session, player);
        Assert.False(first.Identity.Equals(after.Identity));
        Assert.False(prior.Matches(after));
        Assert.Null(new RecommendationEngine().Recommend(plan, prior, after).Action);
        Assert.Equal(0, ActiveBuildState.Activate(plan, after, prior).EffectiveAcquisitions("a"));
    }

    [Fact]
    public void ProgressStoreKeepsScopesSeparateAndDoesNotAliasSanitizedNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sbo-multi-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ActiveStateStore(dir); var plan = Plan();
            var first = Capture("room:a"); var state = ActiveBuildState.Activate(plan, first);
            state.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired, AcquisitionEvidence.ClientAddition);
            state.RecordUnprovenMerge("a"); store.Save(state);
            Assert.Null(store.Load(plan.SourceBuildId, Capture("room_a")));
            Assert.Null(store.Load(plan.SourceBuildId, Capture(player: "foreign")));
            var restored = store.Load(plan.SourceBuildId, first)!;
            Assert.True(restored.Artifacts["a"].IsUncertain);
            Assert.Equal(AcquisitionEvidence.ClientMergeUnproven, restored.Artifacts["a"].LastEvidence);
            Assert.Equal(1, restored.EffectiveAcquisitions("a"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ConnectionCharacterAuthorityAndExplicitDisconnectCreateNewScopes()
    {
        var tracker = new NetworkSessionTracker();
        var initial = tracker.Observe("connection", "avatar", true, NetworkRole.Client);
        Assert.Same(initial, tracker.Observe("connection", "avatar", true, NetworkRole.Client));
        var other = tracker.Observe("connection", "other-avatar", true, NetworkRole.Client);
        Assert.NotEqual(initial.SessionId, other.SessionId);
        var lost = tracker.Observe("connection", "other-avatar", false, NetworkRole.Client);
        Assert.NotEqual(other.SessionId, lost.SessionId);
        var reconnect = tracker.Observe("new-connection", "other-avatar", true, NetworkRole.Client);
        Assert.NotEqual(lost.SessionId, reconnect.SessionId);
        tracker.Disconnect();
        Assert.NotEqual(reconnect.SessionId, tracker.Observe("new-connection", "other-avatar", true, NetworkRole.Client).SessionId);
    }

    [Theory]
    [InlineData(NetworkRole.Host)]
    [InlineData(NetworkRole.Client)]
    public void NativeRoomChangeIsolatesSameConnectionPlayerAndSeed(NetworkRole role)
    {
        var tracker = new NetworkSessionTracker();
        var firstNetwork = tracker.Observe("persistent-connection", "same-avatar", true, role, "steam:100");
        Assert.Same(firstNetwork, tracker.Observe("persistent-connection", "same-avatar", true, role, "steam:100"));
        var nextNetwork = tracker.Observe("persistent-connection", "same-avatar", true, role, "steam:200");
        Assert.Equal("steam:100", firstNetwork.LobbyId);
        Assert.Equal("steam:200", nextNetwork.LobbyId);
        Assert.NotEqual(firstNetwork.SessionId, nextNetwork.SessionId);
        var first = Capture(firstNetwork.SessionId, role: role);
        var after = Capture(nextNetwork.SessionId, role: role);
        var plan = Plan(); var prior = ActiveBuildState.Activate(plan, first);
        prior.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired);
        Assert.Equal(0, ActiveBuildState.Activate(plan, after, prior).EffectiveAcquisitions("a"));
        var observer = new ActionOutcomeObserver(first, new(ActionKind.Select, "target", "", first.Identity));
        observer.RecordReward("a", "source");
        Assert.Equal(ObservedActionOutcome.Invalidated, observer.Observe(after));
    }

    [Fact]
    public void NativeLeaveInvalidatesSameLobbyRejoinWithoutMirrorDisconnect()
    {
        var tracker = new NetworkSessionTracker();
        var first = tracker.Observe("persistent", "same-avatar", true, NetworkRole.Host, "steam:100");
        tracker.Disconnect();
        var rejoined = tracker.Observe("persistent", "same-avatar", true, NetworkRole.Host, "steam:100");
        Assert.NotEqual(first.SessionId, rejoined.SessionId);
        var local = tracker.Observe("persistent", "same-avatar", true, NetworkRole.Host, "local");
        Assert.NotEqual(rejoined.SessionId, local.SessionId);
    }

    [Theory]
    [InlineData(NetworkRole.Host, AcquisitionEvidence.ServerAddition)]
    [InlineData(NetworkRole.Client, AcquisitionEvidence.ClientAddition)]
    public void AcquisitionDuplicatesOnLaterFramesNeverCountTwice(NetworkRole role, AcquisitionEvidence evidence)
    {
        var snapshot = Capture(role: role); var state = ActiveBuildState.Activate(Plan(), snapshot); var tracker = new AcquisitionTracker(); tracker.Bind(snapshot);
        Assert.Equal(AcquisitionObservation.Confirmed, tracker.Observe(state, snapshot, "source", "a", evidence));
        Assert.Equal(AcquisitionObservation.Ignored, tracker.Observe(state, Capture(role: role, revision: 30), "source", "a", evidence));
        Assert.Equal(1, state.EffectiveAcquisitions("a"));
        Assert.Equal(evidence, state.Artifacts["a"].LastEvidence);
    }

    [Fact]
    public void HostRpcEchoIsIgnoredAndDifferentServerMergeSourcesCountSeparately()
    {
        var snapshot = Capture(); var state = ActiveBuildState.Activate(Plan(), snapshot); var tracker = new AcquisitionTracker(); tracker.Bind(snapshot);
        tracker.Observe(state, snapshot, "source1", "a", AcquisitionEvidence.ServerAddition);
        tracker.Observe(state, snapshot, "source2", "a", AcquisitionEvidence.ServerAddition);
        Assert.Equal(AcquisitionObservation.Ignored, tracker.Observe(state, snapshot, "destination", "a", AcquisitionEvidence.ClientMergeUnproven));
        Assert.Equal(AcquisitionObservation.Ignored, tracker.Observe(state, snapshot, "source1", "a", AcquisitionEvidence.ClientAddition));
        Assert.Equal(2, state.EffectiveAcquisitions("a"));
    }

    [Fact]
    public void ReloadedHostDoesNotCountAlreadyHeldSourceReplay()
    {
        var snapshot = Capture(inventory: new[] { new InventoryArtifact("held", "a") });
        var state = ActiveBuildState.Activate(Plan(), snapshot); var tracker = new AcquisitionTracker(); tracker.Bind(snapshot);
        Assert.Equal(AcquisitionObservation.Ignored, tracker.Observe(state, snapshot, "held", "a", AcquisitionEvidence.ServerAddition));
        Assert.Equal(1, state.EffectiveAcquisitions("a"));
    }

    [Fact]
    public void UnprovenClientMergePreservesMinimumAndCannotAcknowledgeSelection()
    {
        var snapshot = Capture(role: NetworkRole.Client, inventory: new[] { new InventoryArtifact("held", "a") });
        var state = ActiveBuildState.Activate(Plan(), snapshot); var tracker = new AcquisitionTracker(); tracker.Bind(snapshot);
        Assert.Equal(AcquisitionObservation.Uncertain, tracker.Observe(state, snapshot, "held", "a", AcquisitionEvidence.ClientMergeUnproven));
        Assert.Equal(1, state.EffectiveAcquisitions("a")); Assert.True(state.Artifacts["a"].IsUncertain);
        var observer = new ActionOutcomeObserver(snapshot, new(ActionKind.Select, "target", "", snapshot.Identity));
        observer.RecordReward("a", "held");
        Assert.Equal(ObservedActionOutcome.Pending, observer.Observe(snapshot));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AltarOrRejectedAdditionNeverIncreasesProgress(bool reward, bool successful)
    {
        var snapshot = Capture(); var state = ActiveBuildState.Activate(Plan(), snapshot); var tracker = new AcquisitionTracker(); tracker.Bind(snapshot);
        Assert.Equal(AcquisitionObservation.Ignored, tracker.Observe(state, snapshot, "source", "a", AcquisitionEvidence.ServerAddition, reward, successful));
        Assert.Equal(0, state.EffectiveAcquisitions("a"));
    }

    [Theory]
    [InlineData("room-b", "local", true, NetworkRole.Client)]
    [InlineData("room-a", "foreign", true, NetworkRole.Client)]
    [InlineData("room-a", "local", false, NetworkRole.Client)]
    [InlineData("room-a", "local", true, NetworkRole.Disconnected)]
    public void LateAcquisitionAndActionResultCannotEscapeTheirScope(string session, string player, bool owned, NetworkRole role)
    {
        var before = Capture(role: NetworkRole.Client); var state = ActiveBuildState.Activate(Plan(), before);
        var tracker = new AcquisitionTracker(); tracker.Bind(before);
        var observer = new ActionOutcomeObserver(before, new(ActionKind.Select, "target", "", before.Identity)); observer.RecordReward("a", "source");
        var after = Capture(session, player, role, owned);
        Assert.Equal(ObservedActionOutcome.Invalidated, observer.Observe(after));
        Assert.Equal(AcquisitionObservation.Ignored, tracker.Observe(state, after, "source", "a", AcquisitionEvidence.ClientAddition));
        Assert.Equal(0, state.EffectiveAcquisitions("a"));
    }

    [Fact]
    public void SameCatalogOtherInstanceOrClosedDialogDoesNotProveRequestedAction()
    {
        var before = Capture(); var observer = new ActionOutcomeObserver(before, new(ActionKind.Select, "target", "", before.Identity));
        observer.RecordReward("a", "unrelated");
        Assert.Equal(ObservedActionOutcome.Pending, observer.Observe(Capture(inventory: new[] { new InventoryArtifact("unrelated", "a") })));
        Assert.Equal(ObservedActionOutcome.Succeeded, observer.Observe(Capture(inventory: new[] { new InventoryArtifact("source", "a") })));
        var conversion = new RunSnapshot("same-seed", "local", 1, ScreenKind.ArtifactReward,
            new[] { new ScreenCandidate("convert", CandidateKind.AbandonOrConvert, null) }, Array.Empty<InventoryArtifact>(), null, null, 10, 2, network: before.Network);
        var convertObserver = new ActionOutcomeObserver(conversion, new(ActionKind.AbandonOrConvert, "convert", "", conversion.Identity));
        Assert.Equal(ObservedActionOutcome.Pending, convertObserver.Observe(Capture(screen: ScreenKind.None)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void LobbyPresetsAllowLocalHostAndClientWithAnyConnectionCount(bool server, bool remote) =>
        Assert.True(StartingPresetPolicy.CanApply("session:local:lobby", "session:local:lobby", "1", "2", true, server, remote, false, false));

    [Fact]
    public void PresetNeedsServerLoadoutAndExactCostumeAndPocketMultiplicity()
    {
        Assert.False(StartingLoadoutState.PocketMatches(new[] { 10, 10 }, new[] { 10, 10 }, false));
        Assert.False(StartingLoadoutState.PocketMatches(new[] { 10, 10 }, new[] { 10 }, true));
        Assert.True(StartingLoadoutState.PocketMatches(new[] { 10, 11, 10 }, new[] { 10, 10, 11 }, true));
        Assert.False(StartingLoadoutState.CostumeMatches("Squirrel", "skin", "Squirrel", "wrong"));
        Assert.True(StartingLoadoutState.CostumeMatches("Squirrel", "skin", "Squirrel", "skin"));
    }

    [Fact]
    public void ReconnectedPlacementWaitsForNoAdditionalMovesOrRotations()
    {
        var before = Capture(screen: ScreenKind.Inventory); var batch = new InventoryPlacementBatch(); var build = Guid.NewGuid();
        batch.Start(build, before, 0);
        Assert.Null(batch.Next(build, Capture("room-b", screen: ScreenKind.Inventory), true, null, null, null, null, null, true, null, 1));
        Assert.False(batch.Active);
        batch.Acknowledge(new(ActionExecutionStatus.Succeeded, "late"), 2);
        Assert.Equal(0, batch.CompletedSteps);
    }

    [Fact]
    public async Task BackgroundCancellationDropsOldSessionEvenIfCalculationIgnoresCancellation()
    {
        using var planner = new BackgroundPlanner<string>();
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var emitted = new TaskCompletionSource<string>();
        planner.ResultReady += x => emitted.TrySetResult(x);
        planner.Submit("old-room", (scope, _) => { started.SetResult(); release.Task.GetAwaiter().GetResult(); return scope; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        planner.Cancel();
        planner.Submit("new-room", (scope, _) => scope);
        Assert.Equal("new-room", await emitted.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        release.SetResult();
    }

    [Theory]
    [InlineData(NetworkRole.Host)]
    [InlineData(NetworkRole.Client)]
    public async Task TimedOutReceiptCannotCompleteNewRequestAndReconnectedRecommendationIsStale(NetworkRole role)
    {
        var gateway = new DelayedGateway { Snapshot = Capture(role: role) };
        var executor = new ConfirmedActionExecutor(gateway, TimeSpan.FromMilliseconds(100));
        RecommendedAction Action() => new(ActionKind.Select, "target", "", gateway.Snapshot.Identity);
        var oldAction = Action();
        Assert.Equal(ActionExecutionStatus.TimedOut, (await executor.ConfirmOnceAsync(oldAction)).Status);
        gateway.Snapshot = Capture("new-room", role: role);
        Assert.Equal(ActionExecutionStatus.Stale, (await executor.ConfirmOnceAsync(oldAction)).Status);
        var next = executor.ConfirmOnceAsync(Action());
        Assert.Equal(2, gateway.Receipts.Count);
        gateway.Receipts[0].TrySetResult(true);
        Assert.False(next.IsCompleted);
        Assert.Equal(ActionExecutionStatus.Busy, (await executor.ConfirmOnceAsync(Action())).Status);
        gateway.Receipts[1].TrySetResult(true);
        Assert.True((await next).Succeeded);
    }

    private sealed class DelayedGateway : IGameActionGateway
    {
        public RunSnapshot Snapshot { get; set; } = Capture();
        public List<TaskCompletionSource<bool>> Receipts { get; } = new();
        public Task<RunSnapshot> CaptureSnapshotAsync(CancellationToken token) => Task.FromResult(Snapshot);
        public Task<GameActionReceipt> SendThroughNormalRequestPathAsync(RecommendedAction action, CancellationToken token)
        {
            Receipts.Add(new(TaskCreationOptions.RunContinuationsAsynchronously));
            return Task.FromResult(new GameActionReceipt((Receipts.Count - 1).ToString(), true));
        }
        public Task<bool> WaitForServerConfirmationAsync(GameActionReceipt receipt, CancellationToken token) =>
            Receipts[int.Parse(receipt.RequestId)].Task.WaitAsync(token);
    }
}
