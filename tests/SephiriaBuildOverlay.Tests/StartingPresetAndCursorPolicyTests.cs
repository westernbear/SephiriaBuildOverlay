using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class StartingPresetAndCursorPolicyTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(null, true, false)]
    [InlineData(null, null, false)]
    [InlineData(0, null, false)]
    public void NativeMissingLobbyEnvironmentRequiresKnownNotStartedRun(int? inDungeon, bool? runStarted, bool expected) =>
        Assert.Equal(expected, StartingPresetPolicy.IsLobby(inDungeon, runStarted));

    [Fact]
    public void TitleImportWaitsForFirstSafeLobbyAndNativePanel()
    {
        var pending = new DeferredStartingPreset(null);
        Assert.Equal(DeferredPresetDecision.Wait, pending.Observe(null, false, false));
        Assert.Equal(DeferredPresetDecision.Wait, pending.Observe("lobby", false, false));
        Assert.Equal("lobby", pending.Context);
        Assert.Equal(DeferredPresetDecision.Apply, pending.Observe("lobby", false, true));
        Assert.Equal(DeferredPresetDecision.Cancel, pending.Observe("lobby", false, true)); // Once per load.
    }

    [Fact]
    public void EnteringDungeonJoiningClientOrChangingBoundContextCancelsPendingPreset()
    {
        Assert.Equal(DeferredPresetDecision.Cancel, new DeferredStartingPreset(null).Observe(null, true, true));
        Assert.Equal(DeferredPresetDecision.Cancel, new DeferredStartingPreset("a").Observe("b", false, true));
        Assert.Equal(DeferredPresetDecision.Cancel, new DeferredStartingPreset("a").Observe(null, false, true));
        var cancelled = new DeferredStartingPreset("a");
        Assert.Equal(DeferredPresetDecision.Cancel, cancelled.Observe("b", false, true));
        Assert.Equal(DeferredPresetDecision.Cancel, cancelled.Observe("a", false, true)); // Cannot revive a stale request.
    }

    [Fact]
    public void SettingsPanelUsesSystemCursorIndependentlyOfNativeSpriteOrImguiDepth()
    {
        Assert.Equal(ModalCursorSurface.NativeCanvas, ModalCursorPresentation.Choose(true, false, false, true, true));
        Assert.Equal(ModalCursorSurface.System, ModalCursorPresentation.Choose(true, true, false, true, true));
        Assert.Equal(ModalCursorSurface.System, ModalCursorPresentation.Choose(true, true, false, true, false));
        Assert.Equal(ModalCursorSurface.System, ModalCursorPresentation.Choose(true, false, false, true, false));
        Assert.True(OverlayUiTokens.CursorSortingOrder > OverlayUiTokens.OverlaySortingOrder);
    }

    [Theory]
    [InlineData(false, false, true, true, 0)]
    [InlineData(true, true, true, true, 0)]
    [InlineData(true, false, false, true, 0)]
    [InlineData(true, false, true, false, 1)]
    [InlineData(true, false, true, true, 1)]
    public void CursorRestoresOnCloseGamepadFocusLossAndUsesOsFallback(bool panel, bool gamepad, bool focused, bool sprite, int expected) =>
        Assert.Equal((ModalCursorSurface)expected, ModalCursorPresentation.Choose(panel, true, gamepad, focused, sprite));
}
