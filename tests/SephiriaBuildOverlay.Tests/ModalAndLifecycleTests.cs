using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ModalAndLifecycleTests
{
    [Fact]
    public void OpenCapturesRegardlessOfDeviceOrHeldInput()
    {
        var state = new ModalInputCapture();
        Assert.False(state.Capturing);
        state.SetVisible(true);
        state.Tick(1, true, false);
        state.Tick(500, false, true);
        Assert.True(state.Visible);
        Assert.True(state.Capturing);
    }

    [Fact]
    public void ClosingMousePressAndReleaseFrameNeverClickThrough()
    {
        var state = new ModalInputCapture();
        state.SetVisible(true); state.SetVisible(false);
        state.Tick(10, true, true); state.Tick(999, true, true);
        Assert.True(state.Capturing);
        state.Tick(1000, true, false); Assert.True(state.Capturing);
        state.Tick(1000, true, false); Assert.True(state.Capturing);
        state.Tick(1001, true, false); Assert.True(state.Capturing);
        state.Tick(1002, true, false); Assert.False(state.Capturing);
    }

    [Fact]
    public void FocusLossNewPressOrReopenResetsClosingDrain()
    {
        var state = new ModalInputCapture();
        state.SetVisible(true); state.SetVisible(false);
        state.Tick(1, true, false); state.Tick(2, false, false);
        state.Tick(5, true, false); Assert.True(state.Capturing);
        state.Tick(6, true, true); state.Tick(7, true, false);
        Assert.True(state.Capturing);
        state.SetVisible(true); state.Tick(20, true, false);
        Assert.True(state.Capturing);
        state.SetVisible(false); state.Tick(21, true, false);
        state.Tick(23, true, false); Assert.False(state.Capturing);
    }

    [Theory]
    [InlineData(6, false, 9)]
    [InlineData(10, false, 10)]
    [InlineData(6, true, 6)]
    [InlineData(9, false, 9)]
    public void DefaultF6MigratesOnceButCustomKeysArePreserved(int key, bool migrated, int expected) =>
        Assert.Equal(expected, ImportShortcutMigration.Migrate(key, 6, 9, migrated));

    [Fact]
    public async Task ShutdownCancelsPendingWorkAndIsIdempotent()
    {
        var lifetime = new PluginLifetime();
        var token = lifetime.Token;
        var pending = Task.Delay(Timeout.Infinite, token);
        Assert.True(lifetime.Stop());
        Assert.False(lifetime.Stop());
        Assert.True(token.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        lifetime.Dispose(); lifetime.Dispose();
        Assert.True(lifetime.Stopped);
        Assert.True(lifetime.Token.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false, 30f, true, false, false, false)]
    [InlineData(true, 14f, true, false, false, false)]
    [InlineData(true, 30f, false, false, false, false)]
    [InlineData(true, 30f, true, true, false, false)]
    [InlineData(true, 30f, true, false, true, false)]
    [InlineData(true, 30f, true, false, false, true)]
    public void ExitSmokeOnlyQuitsExplicitSafeTitle(bool requested, float elapsed, bool title, bool avatar, bool pending, bool expected) =>
        Assert.Equal(expected, TitleExitSmokePolicy.CanQuit(requested, elapsed, title, avatar, pending));

    [Fact]
    public void ExitSmokeRequiresExactCliFlag()
    {
        Assert.False(TitleExitSmokePolicy.Enabled(new[] { "--sbo-exit-smoke=false" }));
        Assert.True(TitleExitSmokePolicy.Enabled(new[] { "Sephiria.exe", "--sbo-exit-smoke" }));
    }
}
