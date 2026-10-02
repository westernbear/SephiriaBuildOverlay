using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ScreenSpaceUiMetricsTests
{
    [Theory]
    [InlineData(6, 20, 5)]
    [InlineData(30, 100, 5)]
    [InlineData(2, 100, 1)]
    [InlineData(0, 100, 1)]
    public void SliceBordersFitCompactPanel(float border, float ppu, float expected)
    {
        var multiplier = ScreenSpaceUiMetrics.SliceMultiplier(border, ppu);
        Assert.Equal(expected, multiplier, precision: 4);
        Assert.True(border / ppu * ScreenSpaceUiMetrics.ReferencePixelsPerUnit / multiplier <= ScreenSpaceUiMetrics.PanelBorderPixels + .001f);
    }

    [Theory]
    [InlineData(float.NaN, 100)]
    [InlineData(6, 0)]
    [InlineData(-1, 20)]
    [InlineData(6, float.PositiveInfinity)]
    public void InvalidSpriteMetricsUseSafeDefault(float border, float ppu) =>
        Assert.Equal(1, ScreenSpaceUiMetrics.SliceMultiplier(border, ppu));

    [Fact]
    public void CursorRendersAbovePanelsAndPassiveNotifications()
    {
        Assert.True(OverlayUiTokens.CursorSortingOrder > OverlayUiTokens.NotificationSortingOrder);
        Assert.True(OverlayUiTokens.NotificationSortingOrder > OverlayUiTokens.OverlaySortingOrder);
        Assert.InRange(OverlayUiTokens.CursorSortingOrder, 0, short.MaxValue);
    }
}
