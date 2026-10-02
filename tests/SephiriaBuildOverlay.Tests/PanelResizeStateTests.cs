using SephiriaBuildOverlay.Plugin;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public sealed class PanelResizeStateTests
{
    [Fact]
    public void CornerDragScalesWholePanelProportionally()
    {
        var state = new PanelResizeState(1); Assert.True(state.Begin(-1, 100, 100, 1, 1, 2));
        state.Drag(-1, 121, 92.8f); Assert.Equal(1.1f, state.Scale, 3);
        Assert.True(state.End(-1)); Assert.False(state.Drag(-1, 200, 50));
    }
    [Fact]
    public void SingleMoveFromPressOriginKeepsEntireFastDrag()
    {
        var state = new PanelResizeState(1);
        Assert.True(state.Begin(-1, 1152, 486, 1, 1, 2));
        Assert.Equal(-1, state.ActivePointer);
        Assert.True(state.Drag(-1, 1257, 450));
        Assert.True(state.End(-1));
        Assert.Null(state.ActivePointer);
        Assert.Equal(1.5f, state.Scale, 3);
    }
    [Fact]
    public void OnlyOwningPointerCanResizeOrFinish()
    {
        var state = new PanelResizeState(1); state.Begin(-1, 0, 0, 1, 1, 2);
        Assert.False(state.Begin(2, 0, 0, 1, 1, 2)); Assert.False(state.Drag(2, 200, -200)); Assert.False(state.End(2)); Assert.Equal(1, state.Scale);
    }
    [Fact]
    public void ScreenFitAndMinimumBoundAreEnforced()
    {
        var state = new PanelResizeState(1); state.Begin(-1, 0, 0, 1, 1, 1.2f);
        state.Drag(-1, 10000, -10000); Assert.Equal(1.2f, state.Scale, 3);
        state.Drag(-1, -10000, 10000); Assert.Equal(PanelResizeState.MinimumScale, state.Scale);
    }
    [Fact]
    public void UiScaleDoesNotChangePanelOnlyPreference()
    {
        var state = new PanelResizeState(1); state.Begin(-1, 0, 0, 1.5f, 1.5f, 3);
        state.Drag(-1, 31.5f, -10.8f); Assert.Equal(1.1f, state.Scale, 3);
    }
    [Fact]
    public void InvalidCoordinatesAndCancelledDragAreIgnored()
    {
        var state = new PanelResizeState(float.NaN); Assert.Equal(1, state.Scale);
        Assert.False(state.Begin(-1, float.NaN, 0, 1, 1, 2)); state.Begin(-1, 0, 0, 1, 1, 2);
        Assert.False(state.Drag(-1, float.PositiveInfinity, 0)); state.Cancel(); Assert.False(state.End(-1));
    }
    [Fact]
    public void SmallViewportNeverPersistsAnInvalidSetting()
    {
        var state = new PanelResizeState(1); state.Begin(-1, 0, 0, .4f, 1, .4f); state.Drag(-1, 100, -100);
        Assert.Equal(PanelResizeState.MinimumScale, state.Scale); Assert.True(state.End(-1));
        Assert.Equal(state.Scale, new PanelResizeState(state.Scale).Scale);
    }
}
