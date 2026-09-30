using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ControllerInputTests
{
    [Fact]
    public void OneFreshDirectionPerConfirmationNotModifierReleaseOrHold()
    {
        var input = Ready();
        Assert.Equal(PadCommand.Confirm, Poll(input, PadButtons.Modifier | PadButtons.Right));
        for (var i = 0; i < 100; i++) Assert.Equal(PadCommand.None, Poll(input, PadButtons.Modifier | PadButtons.Right));
        Assert.Equal(PadCommand.None, Poll(input, PadButtons.Right));
        Assert.Equal(PadCommand.None, Poll(input, PadButtons.Modifier | PadButtons.Right));
        Poll(input, PadButtons.Modifier);
        Assert.Equal(PadCommand.Confirm, Poll(input, PadButtons.Modifier | PadButtons.Right));
    }

    [Fact]
    public void KeyboardModeNeverUsesConnectedPad()
    {
        var input = Ready();
        Assert.Equal(PadCommand.None, input.Update(false, true, "1", PadButtons.Modifier | PadButtons.Right, false));
        Assert.Equal(PadCommand.None, input.Update(true, true, "1", PadButtons.Modifier | PadButtons.Right, false));
        Assert.Equal(PadCommand.None, Poll(input, PadButtons.Modifier | PadButtons.Right));
        Poll(input, PadButtons.None);
        Assert.Equal(PadCommand.Confirm, Poll(input, PadButtons.Modifier | PadButtons.Right));
    }

    [Theory]
    [InlineData(false, "1")]
    [InlineData(true, null)]
    [InlineData(true, "2")]
    public void FocusLossDisconnectOrDeviceChangeDoesNotConfirm(bool focused, string? device)
    {
        var input = Ready();
        Assert.Equal(PadCommand.None, input.Update(true, focused, device, PadButtons.Modifier | PadButtons.Right, false));
        Assert.Equal(PadCommand.None, Poll(input, PadButtons.Modifier | PadButtons.Right));
        Poll(input, PadButtons.None);
        Assert.Equal(PadCommand.Confirm, Poll(input, PadButtons.Modifier | PadButtons.Right));
    }

    [Fact]
    public void NormalFaceButtonsAndDirectionsCannotConfirmGameAction()
    {
        var input = Ready();
        Assert.Equal(PadCommand.None, Poll(input, PadButtons.Submit | PadButtons.Right));
        Poll(input, PadButtons.None);
        Assert.Equal(PadCommand.None, Poll(input, PadButtons.Cancel));
    }

    [Fact]
    public void ModalNavigationDoesNotLeakIntoConfirmationAndEntryRequiresFreshPress()
    {
        var input = Ready();
        Assert.Equal(PadCommand.None, input.Update(true, true, "1", PadButtons.Submit, true));
        Assert.Equal(PadCommand.None, input.Update(true, true, "1", PadButtons.Submit, true));
        input.Update(true, true, "1", PadButtons.None, true);
        Assert.Equal(PadCommand.Next, input.Update(true, true, "1", PadButtons.Right, true));
        input.Update(true, true, "1", PadButtons.None, true);
        Assert.Equal(PadCommand.Submit, input.Update(true, true, "1", PadButtons.Submit, true));
        Assert.Equal(PadCommand.Cancel, input.Update(true, true, "1", PadButtons.Cancel, true));
    }

    [Fact]
    public void DiagonalChordHasOneNonDestructiveCommand()
    {
        Assert.Equal(PadCommand.Import, Poll(Ready(), PadButtons.Modifier | PadButtons.Up | PadButtons.Right));
        Assert.Equal(PadCommand.Overlay, Poll(Ready(), PadButtons.Modifier | PadButtons.Left | PadButtons.Right));
    }

    [Fact]
    public void ReviewMenuFocusWrapsAndRemovedControlCannotActivateStaleCallback()
    {
        var menu = new ControllerMenu(); var called = 0;
        menu.BeginFrame(); menu.Add("first", () => called++); menu.Add("second", () => called += 10); menu.EndFrame();
        menu.Navigate(-1); Assert.True(menu.IsSelected("second")); menu.Activate(); Assert.Equal(10, called);
        menu.BeginFrame(); menu.Add("first", () => called++); menu.EndFrame();
        Assert.True(menu.IsSelected("first")); menu.Activate(); Assert.Equal(11, called);
        menu.Reset(); menu.Activate(); Assert.Equal(11, called);
    }

    private static ControllerInputState Ready()
    {
        var input = new ControllerInputState(); Poll(input, PadButtons.None); return input;
    }
    private static PadCommand Poll(ControllerInputState input, PadButtons buttons) => input.Update(true, true, "1", buttons, false);

    [Fact]
    public void PageChangesRetainFocusButDropOldActionsBeforeRedraw()
    {
        var menu = new ControllerMenu(); var called = 0;
        menu.BeginFrame(); menu.Add("next", () => called++); menu.EndFrame();
        menu.Invalidate(); menu.Activate(); Assert.Equal(0, called);
        Assert.True(menu.IsSelected("next"));
        menu.BeginFrame(); menu.Add("next", () => called += 10); menu.EndFrame();
        menu.Activate(); Assert.Equal(10, called);
    }
}
