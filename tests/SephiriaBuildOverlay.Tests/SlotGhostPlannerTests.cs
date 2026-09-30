using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class SlotGhostPlannerTests
{
    [Fact]
    public void RequiredGoalGetsBestAvailableCellAndFixedItemsAreNeverDisplaced()
    {
        var items = new[] { new GhostItem("r", "required", new GridPoint(0, 0), 5, true), new GhostItem("o", "optional", new GridPoint(1, 0), 5, true), new GhostItem("f", "other", new GridPoint(3, 0), 5, false) };
        var slots = Enumerable.Range(0, 4).Select(x => new GhostSlot(new GridPoint(x, 0), x + 1, false)).ToArray();
        var goals = new[] { new ArtifactTarget("optional", 1, TargetRole.Recommended, 0), new ArtifactTarget("required", 1, TargetRole.Required, 1) };
        var result = new SlotGhostPlanner().Solve(items, slots, goals, false);
        Assert.Contains(result, x => x.InstanceId == "r" && x.To.Equals(new GridPoint(2, 0)));
        Assert.DoesNotContain(result, x => x.To.Equals(new GridPoint(3, 0)) || x.InstanceId == "f");
    }
    [Fact]
    public void ConditionalTabletAndUnknownItemCriteriaDoNotProduceGuessedMoves()
    {
        var items = new[] { new GhostItem("a", "key", new GridPoint(0, 0), 5, false) };
        var slots = new[] { new GhostSlot(new GridPoint(0, 0), 1, false), new GhostSlot(new GridPoint(1, 0), 5, false) };
        var goals = new[] { new ArtifactTarget("key", 1, TargetRole.Required, 0) };
        Assert.Empty(new SlotGhostPlanner().Solve(items, slots, goals, false));
        Assert.Empty(new SlotGhostPlanner().Solve(new[] { new GhostItem("a", "key", new GridPoint(0, 0), 5, true) }, slots, goals, true));
    }
    [Fact]
    public void DuplicateAssignmentsAreDistinctAndCappedLevelTiesDoNotCauseMovement()
    {
        var items = new[] { new GhostItem("a", "key", new GridPoint(0, 0), 3, true), new GhostItem("b", "key", new GridPoint(1, 0), 3, true) };
        var slots = new[] { new GhostSlot(new GridPoint(0, 0), 3, false), new GhostSlot(new GridPoint(1, 0), 1, false), new GhostSlot(new GridPoint(2, 0), 5, false) };
        var goals = new[] { new ArtifactTarget("key", 2, TargetRole.Required, 0) };
        var result = new SlotGhostPlanner().Solve(items, slots, goals, false);
        Assert.Single(result);
        Assert.Equal("b", result[0].InstanceId);
        Assert.Equal(new GridPoint(2, 0), result[0].To);
    }
}
