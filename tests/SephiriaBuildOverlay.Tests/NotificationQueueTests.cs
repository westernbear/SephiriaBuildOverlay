using SephiriaBuildOverlay.Plugin;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public sealed class NotificationQueueTests
{
    [Theory]
    [InlineData((int)NotificationKind.Information)]
    [InlineData((int)NotificationKind.Success)]
    [InlineData((int)NotificationKind.Warning)]
    [InlineData((int)NotificationKind.Error)]
    public void NewEventImmediatelyReplacesEvenAnUnexpiredWarning(int kindValue)
    {
        var kind = (NotificationKind)kindValue;
        var q = new NotificationQueue(); q.Enqueue("old warning", NotificationKind.Warning);
        q.Advance(.1f, true); q.Enqueue("new event", kind);
        Assert.Equal("new event", q.Current!.Text); Assert.Equal(kind, q.Current.Kind); Assert.Equal(1, q.Opacity);
        q.Advance(100, true); Assert.Null(q.Current);
    }

    [Fact]
    public void UnfocusedGameStillReplacesButDoesNotExpireNotifications()
    {
        var q = new NotificationQueue(); q.Enqueue("loaded", NotificationKind.Success);
        q.Advance(100, false); Assert.Equal("loaded", q.Current!.Text);
        q.Enqueue("new warning", NotificationKind.Warning); q.Advance(100, false);
        Assert.Equal("new warning", q.Current!.Text); q.Advance(NotificationQueue.WarningSeconds, true); Assert.Null(q.Current);
    }

    [Fact]
    public void RepeatedNoticeDoesNotExtendLifetime()
    {
        var q = new NotificationQueue(); q.Enqueue("loaded", NotificationKind.Success); q.Advance(0, true);
        q.Advance(4, true); q.Enqueue("loaded", NotificationKind.Success);
        q.Advance(1, true); Assert.Null(q.Current);
    }

    [Fact]
    public void DuplicatesAndBlanksAreIgnored()
    {
        var q = new NotificationQueue(); q.Enqueue("  ", NotificationKind.Warning);
        q.Enqueue(" hello ", NotificationKind.Warning); q.Enqueue("hello", NotificationKind.Warning);
        Assert.Equal("hello", q.Current!.Text);
    }

    [Fact]
    public void BurstDisplaysLatestWithoutWaitingForAnyAdvance()
    {
        var q = new NotificationQueue();
        for (var i = 0; i < 100; i++) q.Enqueue("event " + i, NotificationKind.Warning);
        Assert.Equal("event 99", q.Current!.Text);
        q.Advance(NotificationQueue.WarningSeconds, true); Assert.Null(q.Current);
    }

    [Fact]
    public void SameTextWithNewSeverityIsANewNotice()
    {
        var q = new NotificationQueue(); q.Enqueue("notice", NotificationKind.Success); q.Advance(4, true);
        q.Enqueue("notice", NotificationKind.Error); q.Advance(7.9f, true); Assert.NotNull(q.Current);
        q.Advance(.2f, true); Assert.Null(q.Current);
    }

    [Fact]
    public void ReplacementDuringFadeIsImmediatelyOpaque()
    {
        var q = new NotificationQueue(); q.Enqueue("loaded", NotificationKind.Success); q.Advance(0, true);
        q.Advance(NotificationQueue.ReadingSeconds - NotificationQueue.FadeSeconds / 2, true);
        Assert.Equal(.5f, q.Opacity, 3);
        q.Enqueue("next", NotificationKind.Success); Assert.Equal(1, q.Opacity);
        q.Advance(NotificationQueue.ReadingSeconds, true); Assert.Equal(0, q.Opacity);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)]
    public void InvalidDeltaDoesNotConsumeQueue(float seconds)
    { var q = new NotificationQueue(); q.Enqueue("notice", NotificationKind.Warning); q.Advance(seconds, true); Assert.Equal("notice", q.Current!.Text); Assert.Equal(1, q.Opacity); }

    [Fact]
    public void ClearRemovesCurrentAndPending()
    {
        var q = new NotificationQueue(); q.Enqueue("one", NotificationKind.Warning); q.Advance(0, true);
        q.Enqueue("two", NotificationKind.Error); q.Clear(); q.Advance(0, true);
        Assert.Null(q.Current); Assert.Equal(0, q.Opacity);
    }
}
