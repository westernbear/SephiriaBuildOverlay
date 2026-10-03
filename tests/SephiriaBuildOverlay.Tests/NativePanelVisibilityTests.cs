using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class NativePanelVisibilityTests
{
    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, null, false, false)]
    [InlineData(true, true, null, true, true)]
    [InlineData(true, true, null, null, true)]
    public void UIBaseOpenedWinsOverUnusedShowing(bool active, bool enabled, bool? opened, bool? showing, bool expected) =>
        Assert.Equal(expected, NativePanelVisibility.IsVisible(active, enabled, opened, showing));
}
