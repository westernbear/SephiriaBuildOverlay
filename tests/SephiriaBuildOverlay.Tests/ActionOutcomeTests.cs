using SephiriaBuildOverlay.Core.Runtime;
using static SephiriaBuildOverlay.Tests.ReviewAndProgressTests;

namespace SephiriaBuildOverlay.Tests;

public sealed class ActionOutcomeTests
{
    [Fact]
    public void NewlyAcquiredMiracleCanBeSecondInOwnedList()
    {
        var before = new RunSnapshot("run", "player", 1, ScreenKind.MiracleChoice,
            new[] { new ScreenCandidate("target", CandidateKind.Miracle, "goal") }, Array.Empty<InventoryArtifact>(),
            null, "existing", 0, 1);
        var observer = new ActionOutcomeObserver(before, new(ActionKind.Select, "target", "", before.Identity));
        var keys = new List<string> { "existing", "goal" };
        var after = new RunSnapshot("run", "player", 2, ScreenKind.None, Array.Empty<ScreenCandidate>(),
            Array.Empty<InventoryArtifact>(), null, "existing", 0, 1, miracleKeys: keys);
        keys.Clear();
        Assert.Equal(2, after.MiracleKeys.Count);
        Assert.Equal(ObservedActionOutcome.Succeeded, observer.Observe(after));
    }

    [Fact]
    public void SnapshotDoesNotShareMutableInputLists()
    {
        var candidates = new List<ScreenCandidate> { new("one", CandidateKind.Artifact, "a") };
        var inventory = new List<InventoryArtifact> { new("held", "a") };
        var snapshot = Snapshot(candidates: candidates, inventory: inventory);
        candidates.Clear(); inventory.Clear();
        Assert.Single(snapshot.Candidates);
        Assert.Single(snapshot.Inventory);
    }

    [Fact]
    public void ArbitraryRevisionAndResourceChangeAreNotAcknowledgements()
    {
        var before = Snapshot(screen: ScreenKind.ArtifactReward,
            candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var observer = new ActionOutcomeObserver(before, new(ActionKind.Select, "target", "", before.Identity));
        Assert.Equal(ObservedActionOutcome.Pending, observer.Observe(Snapshot(revision: 2, money: 9)));
        observer.RecordReward("outside");
        Assert.Equal(ObservedActionOutcome.Pending, observer.Observe(Snapshot(revision: 3)));
        observer.RecordReward("a");
        // A wisdom merge can leave the inventory instance count unchanged.
        Assert.Equal(ObservedActionOutcome.Succeeded, observer.Observe(Snapshot(revision: 4)));
    }

    [Fact]
    public void OwnershipAndRunChangesInvalidatePendingRequest()
    {
        var before = Snapshot(candidates: new[] { new ScreenCandidate("target", CandidateKind.Artifact, "a") });
        var observer = new ActionOutcomeObserver(before, new(ActionKind.Select, "target", "", before.Identity));
        observer.RecordReward("a");
        Assert.Equal(ObservedActionOutcome.Invalidated, observer.Observe(Snapshot(run: "next")));
        Assert.Equal(ObservedActionOutcome.Invalidated, observer.Observe(Snapshot(owned: false)));
    }

    [Fact]
    public void RerollRequiresChangedOfferSetNotJustDiceConsumption()
    {
        var offers = new[] { new ScreenCandidate("reroll", CandidateKind.Reroll, null),
            new ScreenCandidate("offer", CandidateKind.Artifact, "a") };
        var before = Snapshot(screen: ScreenKind.ArtifactReward, candidates: offers);
        var observer = new ActionOutcomeObserver(before, new(ActionKind.Reroll, "reroll", "", before.Identity));
        Assert.Equal(ObservedActionOutcome.Pending, observer.Observe(Snapshot(revision: 2,
            screen: before.Screen, candidates: offers, dice: 1)));
        Assert.Equal(ObservedActionOutcome.Succeeded, observer.Observe(Snapshot(revision: 3,
            screen: before.Screen, candidates: new[] { new ScreenCandidate("offer", CandidateKind.Artifact, "b") })));
    }
}
