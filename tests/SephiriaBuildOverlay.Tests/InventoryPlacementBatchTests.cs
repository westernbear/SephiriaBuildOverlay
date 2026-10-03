using SephiriaBuildOverlay.Core.Runtime;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class InventoryPlacementBatchTests
{
    private static readonly Guid Build = Guid.NewGuid();
    private static BoardLayout Layout(int x, int rotation = 0) => new(new Dictionary<string, GridPoint> { ["a"] = new(x, 0) }, new Dictionary<string, int> { ["t"] = rotation });
    private static RecommendedAction Action(RunSnapshot s, ActionKind kind = ActionKind.Move, string token = "opt:move:a", int money = 0, int dice = 0, bool automatic = true) => new(kind, token, "", s.Identity, money, dice, automaticBindingAllowed: automatic);
    private static readonly ActionExecutionResult Success = new(ActionExecutionStatus.Succeeded, "ok");

    [Fact]
    public void OneStartCompletesMoveAndThreeForwardRotationsWithoutFurtherConfirmation()
    {
        var snapshot = Snapshot(screen: ScreenKind.Inventory); var batch = new InventoryPlacementBatch();
        var goal = Layout(1, 3); batch.Start(Build, snapshot, 0);
        var layouts = new[] { Layout(0), Layout(1), Layout(1, 1), Layout(1, 2), goal };
        for (var i = 0; i < layouts.Length - 1; i++)
        {
            var action = Action(snapshot, i == 0 ? ActionKind.Move : ActionKind.Rotate, i == 0 ? "opt:move:a" : "opt:rotate:t");
            Assert.Same(action, batch.Next(Build, snapshot, true, "model", layouts[i], goal, action, layouts[i + 1], false, null, i));
            Assert.Null(batch.Next(Build, snapshot, true, "model", layouts[i], goal, action, layouts[i + 1], false, null, i));
            batch.Acknowledge(Success, i + .1);
        }
        Assert.Null(batch.Next(Build, snapshot, true, "model", goal, null, null, null, true, null, 5));
        Assert.False(batch.Active); Assert.Equal(4, batch.CompletedSteps); Assert.Equal("자동배치 완료", batch.EndReason);
    }

    [Theory]
    [InlineData(ActionKind.Select, "opt:select", 0, 0, true)]
    [InlineData(ActionKind.Buy, "opt:buy", 0, 0, true)]
    [InlineData(ActionKind.Reroll, "opt:reroll", 0, 0, true)]
    [InlineData(ActionKind.AbandonOrConvert, "opt:convert", 0, 0, true)]
    [InlineData(ActionKind.Rotate, "rotate:manual", 0, 0, true)]
    [InlineData(ActionKind.Move, "opt:move", 1, 0, true)]
    [InlineData(ActionKind.Move, "opt:move", 0, 1, true)]
    [InlineData(ActionKind.Move, "opt:move", 0, 0, false)]
    public void NeverAuthorizesConsumptiveOrManualActions(ActionKind kind, string token, int money, int dice, bool automatic) =>
        Assert.False(InventoryPlacementBatch.Eligible(Action(Snapshot(), kind, token, money, dice, automatic)));

    [Theory]
    [InlineData("model")]
    [InlineData("run")]
    [InlineData("player")]
    [InlineData("build")]
    [InlineData("manual-layout")]
    [InlineData("goal")]
    [InlineData("ownership")]
    [InlineData("focus-or-window")]
    public void StateChangesStopBeforeAnotherDispatch(string change)
    {
        var s = Snapshot(screen: ScreenKind.Inventory); var batch = new InventoryPlacementBatch(); batch.Start(Build, s, 0);
        var goal = Layout(2); batch.Next(Build, s, true, "model", Layout(0), goal, Action(s), Layout(1), false, null, 0); batch.Acknowledge(Success, .1);
        var after = new RunSnapshot(change == "run" ? "other-run" : s.RunId, change == "player" ? "other-player" : s.LocalPlayerId, s.Revision,
            s.Screen, s.Candidates, s.Inventory, null, null, 0, 0, isLocalPlayerOwned: change != "ownership");
        var current = change == "manual-layout" ? Layout(4) : Layout(1);
        Assert.Null(batch.Next(change == "build" ? Guid.NewGuid() : Build, after, change != "focus-or-window", change == "model" ? "changed" : "model",
            current, change == "goal" ? Layout(3) : goal, Action(after), goal, false, null, 1));
        Assert.False(batch.Active); Assert.Contains("중단", batch.EndReason);
    }

    [Theory]
    [InlineData(ActionExecutionStatus.Rejected)]
    [InlineData(ActionExecutionStatus.Stale)]
    [InlineData(ActionExecutionStatus.TimedOut)]
    public void RejectionOrTimeoutStopsWithoutRetry(ActionExecutionStatus status)
    {
        var s = Snapshot(); var batch = new InventoryPlacementBatch(); batch.Start(Build, s, 0);
        batch.Acknowledge(new ActionExecutionResult(status, "failed"), 1); Assert.False(batch.Active);
    }

    [Fact]
    public void TemporaryMapsWaitButDoNotWaitForeverOrClaimOffscreenCompletion()
    {
        var s = Snapshot(); var batch = new InventoryPlacementBatch(); batch.Start(Build, s, 0);
        Assert.Null(batch.Next(Build, s, true, null, null, null, null, null, true, null, 1)); Assert.True(batch.Active);
        batch.Next(Build, s, true, null, null, null, null, null, true, null, 11); Assert.False(batch.Active);
        batch.Start(Build, s, 0);
        batch.Next(Build, s, true, "model", Layout(0), Layout(2), null, null, false, "슬롯이 화면 밖에 있습니다", 1);
        Assert.Contains("화면 밖", batch.EndReason);
    }

    [Fact]
    public void PostSelectionPlacementOnlyFollowsConfirmedArtifactRewardNotPurchasesRerollsOrWeapon()
    {
        var s = Snapshot(); var artifact = new ScreenCandidate("target", CandidateKind.Artifact, "a");
        var selection = Action(s, ActionKind.Select, "target");
        Assert.True(InventoryPlacementBatch.FollowsArtifactSelection(ScreenKind.ArtifactReward, selection, artifact, Success));
        Assert.False(InventoryPlacementBatch.FollowsArtifactSelection(ScreenKind.Shop, selection, artifact, Success));
        Assert.False(InventoryPlacementBatch.FollowsArtifactSelection(ScreenKind.ArtifactReward, Action(s, ActionKind.Buy, "target"), artifact, Success));
        Assert.False(InventoryPlacementBatch.FollowsArtifactSelection(ScreenKind.ArtifactReward, selection, artifact, new(ActionExecutionStatus.TimedOut, "")));
        Assert.False(InventoryPlacementBatch.FollowsArtifactSelection(ScreenKind.ArtifactReward, selection, new("target", CandidateKind.Weapon, "a"), Success));
    }

    [Fact]
    public void CancellationPreventsInFlightAcknowledgementFromRearmingAndNextConfirmCanStartAgain()
    {
        var s = Snapshot(); var batch = new InventoryPlacementBatch(); batch.Start(Build, s, 0);
        batch.Stop("cancel"); batch.Acknowledge(Success, 1); Assert.False(batch.Active);
        batch.Start(Build, s, 2); Assert.True(batch.Active); Assert.Equal(0, batch.CompletedSteps);
    }
}
