using SephiriaBuildOverlay.Plugin;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public sealed class NotificationQueueTests
{
    [Fact]
    public void ExpiresAndGivesNextNoticeItsFullReadingPeriod()
    {
        var q = new NotificationQueue(); q.Enqueue("loaded", NotificationKind.Success); q.Enqueue("warning", NotificationKind.Warning);
        q.Advance(0, true); Assert.Equal("loaded", q.Current!.Text);
        q.Advance(100, true); Assert.Equal("warning", q.Current!.Text); Assert.Equal(1, q.Opacity);
        q.Advance(NotificationQueue.WarningSeconds, true); Assert.Null(q.Current);
    }

    [Fact]
    public void UnfocusedGameDoesNotStartOrExpireNotifications()
    {
        var q = new NotificationQueue(); q.Enqueue("loaded", NotificationKind.Success);
        q.Advance(100, false); Assert.Null(q.Current); Assert.Equal(1, q.PendingCount);
        q.Advance(0, true); q.Advance(100, false); Assert.Equal("loaded", q.Current!.Text);
    }

    [Fact]
    public void RepeatedNoticeDoesNotExtendLifetime()
    {
        var q = new NotificationQueue(); q.Enqueue("loaded", NotificationKind.Success); q.Advance(0, true);
        q.Advance(4, true); q.Enqueue("loaded", NotificationKind.Success);
        q.Advance(1, true); Assert.Null(q.Current); Assert.Equal(0, q.PendingCount);
    }

    [Fact]
    public void PendingDuplicatesAndBlanksAreIgnored()
    {
        var q = new NotificationQueue(); q.Enqueue("  ", NotificationKind.Warning);
        q.Enqueue(" hello ", NotificationKind.Warning); q.Enqueue("hello", NotificationKind.Warning);
        Assert.Equal(1, q.PendingCount);
    }

    [Fact]
    public void OverflowDiscardsInformationBeforeWarnings()
    {
        var q = new NotificationQueue(); q.Enqueue("old info", NotificationKind.Information);
        for (var i = 0; i < 4; i++) q.Enqueue("warning " + i, NotificationKind.Warning);
        Assert.Equal(NotificationQueue.MaximumPending, q.PendingCount);
        q.Advance(0, true); Assert.Equal("warning 0", q.Current!.Text);
    }

    [Fact]
    public void SuccessCannotDisplaceFullWarningQueue()
    {
        var q = new NotificationQueue();
        for (var i = 0; i < 4; i++) q.Enqueue("warning " + i, NotificationKind.Warning);
        q.Enqueue("success", NotificationKind.Success);
        for (var i = 0; i < 4; i++) { q.Advance(100, true); Assert.Equal("warning " + i, q.Current!.Text); }
        q.Advance(100, true); Assert.Null(q.Current);
    }

    [Fact]
    public void FadeOnlyChangesOpacity()
    {
        var q = new NotificationQueue(); q.Enqueue("loaded", NotificationKind.Success); q.Advance(0, true);
        q.Advance(NotificationQueue.ReadingSeconds - NotificationQueue.FadeSeconds / 2, true);
        Assert.Equal(.5f, q.Opacity, 3); q.Advance(NotificationQueue.FadeSeconds / 2, true); Assert.Equal(0, q.Opacity);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)]
    public void InvalidDeltaDoesNotConsumeQueue(float seconds)
    { var q = new NotificationQueue(); q.Enqueue("notice", NotificationKind.Warning); q.Advance(seconds, true); Assert.Null(q.Current); }

    [Fact]
    public void ClearRemovesCurrentAndPending()
    {
        var q = new NotificationQueue(); q.Enqueue("one", NotificationKind.Warning); q.Advance(0, true);
        q.Enqueue("two", NotificationKind.Error); q.Clear(); q.Advance(0, true);
        Assert.Null(q.Current); Assert.Equal(0, q.PendingCount); Assert.Equal(0, q.Opacity);
    }
}
