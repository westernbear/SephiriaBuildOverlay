using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class SlotMoveGuardTests
{
    [Fact]
    public void AnOwnedUniqueInstanceCanMoveWithoutPretendingToAcquireAnotherCopy()
    {
        var item = new GhostItem("owned", "1008", new GridPoint(0, 0), 4, true);
        Assert.True(SlotMoveGuard.CanMove(item, "owned", "1008", true, true, true, true, false));
        Assert.False(SlotMoveGuard.CanMove(item, "replacement", "1008", true, true, true, true, false));
        Assert.False(SlotMoveGuard.CanMove(item, "owned", "other", true, true, true, true, false));
        Assert.False(SlotMoveGuard.CanMove(item, "owned", "1008", false, true, true, true, false));
        Assert.False(SlotMoveGuard.CanMove(item, "owned", "1008", true, false, true, true, false));
        Assert.False(SlotMoveGuard.CanMove(item, "owned", "1008", true, true, true, false, false));
        Assert.False(SlotMoveGuard.CanMove(item, "owned", "1008", true, true, true, true, true));
    }
}
