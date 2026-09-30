using SephiriaBuildOverlay.Core.Runtime;

namespace SephiriaBuildOverlay.Tests;

public sealed class ShortcutTests
{
    [Fact]
    public void HeldKeyAndDuplicateGuiEventsNeverConfirmTwice()
    {
        var latch = new ShortcutLatch();
        Assert.True(latch.Press(289));
        Assert.False(latch.Press(289));
        Assert.False(latch.Press(289));
        latch.Release(289);
        Assert.True(latch.Press(289));
    }

    [Fact]
    public void ReleasingAnotherShortcutDoesNotRearmConfirmation()
    {
        var latch = new ShortcutLatch();
        Assert.True(latch.Press(289));
        latch.Release(287);
        Assert.False(latch.Press(289));
    }
}
