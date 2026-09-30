using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ModalCursorTests
{
    [Fact]
    public void NativeCursorDoesNotChangeSystemCursorVisibility()
    {
        Assert.Null(new ModalCursorVisibility().Resolve(true, false, true, true, false));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackRestoresPreModalVisibilityAfterManyFrames(bool previous)
    {
        var state = new ModalCursorVisibility();
        Assert.True(state.Resolve(true, false, true, false, previous));
        Assert.True(state.Resolve(true, false, true, false, true));
        Assert.Equal(previous, state.Resolve(false, false, true, false, true));
        Assert.Null(state.Resolve(false, false, true, false, previous));
    }
    [Fact]
    public void NativeCursorRecoveryReleasesFallback()
    {
        var state = new ModalCursorVisibility();
        state.Resolve(true, false, true, false, false);
        Assert.False(state.Resolve(true, false, true, true, true));
    }
    [Fact]
    public void FocusLossDoesNotHideNativeSystemPointer()
    {
        var state = new ModalCursorVisibility();
        state.Resolve(true, false, true, false, false);
        Assert.Null(state.Resolve(true, false, false, false, true));
    }
    [Fact]
    public void SwitchingToGamepadHidesFallbackWithoutStartingNewLease()
    {
        var state = new ModalCursorVisibility();
        state.Resolve(true, false, true, false, false);
        Assert.False(state.Resolve(true, true, true, false, true));
        Assert.Null(state.Resolve(true, true, true, false, false));
    }
}
