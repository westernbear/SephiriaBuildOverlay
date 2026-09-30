using SephiriaBuildOverlay.Core.Solvers;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class TabletRotationTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 0)]
    public void EachRequestExpectsExactlyOneNativeQuarterTurn(int before, int after)
    {
        var target = new TabletRotationTarget("instance", "tablet", new GridPoint(2, 2), before);
        Assert.Equal(after, target.NextRotation);
        Assert.True(TabletRotationGuard.CanRequest(target, "instance", "tablet", target.Position, before, true, true, true, true, true));
        Assert.True(TabletRotationGuard.IsObserved(target, "instance", "tablet", target.Position, after, true));
        Assert.False(TabletRotationGuard.IsObserved(target, "instance", "tablet", target.Position, before, true));
        Assert.NotEqual(target.Token, new TabletRotationTarget("instance", "tablet", target.Position, after).Token);
    }

    [Fact]
    public void ChangedItemPositionRotationAuthorityOrPointerRejectsTheRequest()
    {
        var target = new TabletRotationTarget("instance", "tablet", new GridPoint(2, 2), 0);
        Assert.False(TabletRotationGuard.CanRequest(target, "other", "tablet", target.Position, 0, true, true, true, true, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "other", target.Position, 0, true, true, true, true, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "tablet", new GridPoint(2, 3), 0, true, true, true, true, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "tablet", target.Position, 1, true, true, true, true, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "tablet", target.Position, 0, false, true, true, true, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "tablet", target.Position, 0, true, false, true, true, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "tablet", target.Position, 0, true, true, false, true, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "tablet", target.Position, 0, true, true, true, false, true));
        Assert.False(TabletRotationGuard.CanRequest(target, "instance", "tablet", target.Position, 0, true, true, true, true, false));
    }

    [Fact]
    public void AnotherTabletOrUnrelatedStateChangeCannotConfirmRotation()
    {
        var target = new TabletRotationTarget("instance", "tablet", new GridPoint(2, 2), 0);
        Assert.False(TabletRotationGuard.IsObserved(target, "other", "tablet", target.Position, 1, true));
        Assert.False(TabletRotationGuard.IsObserved(target, "instance", "other", target.Position, 1, true));
        Assert.False(TabletRotationGuard.IsObserved(target, "instance", "tablet", new GridPoint(2, 3), 1, true));
        Assert.False(TabletRotationGuard.IsObserved(target, "instance", "tablet", target.Position, 2, true));
        Assert.False(TabletRotationGuard.IsObserved(target, "instance", "tablet", target.Position, 1, false));
    }
}
