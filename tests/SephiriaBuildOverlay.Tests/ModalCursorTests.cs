using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ModalCursorTests
{
    [Fact]
    public void PanelAlwaysUsesSystemCursorRegardlessOfNativeCursor()
    {
        Assert.True(new ModalCursorVisibility().Resolve(true, false, true, false));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackRestoresPreModalVisibilityAfterManyFrames(bool previous)
    {
        var state = new ModalCursorVisibility();
        Assert.True(state.Resolve(true, false, true, previous));
        Assert.True(state.Resolve(true, false, true, true));
        Assert.Equal(previous, state.Resolve(false, false, true, true));
        Assert.Null(state.Resolve(false, false, true, previous));
    }
    [Fact]
    public void NativeUiHidingCursorDoesNotReleaseModalLease()
    {
        var state = new ModalCursorVisibility();
        state.Resolve(true, false, true, false);
        Assert.True(state.Resolve(true, false, true, false));
        Assert.False(state.Resolve(false, false, true, true));
    }
    [Fact]
    public void FocusLossDoesNotHideNativeSystemPointer()
    {
        var state = new ModalCursorVisibility();
        state.Resolve(true, false, true, false);
        Assert.Null(state.Resolve(true, false, false, true));
    }
    [Fact]
    public void SwitchingToGamepadHidesFallbackWithoutStartingNewLease()
    {
        var state = new ModalCursorVisibility();
        state.Resolve(true, false, true, false);
        Assert.False(state.Resolve(true, true, true, true));
        Assert.Null(state.Resolve(true, true, true, false));
    }
}
