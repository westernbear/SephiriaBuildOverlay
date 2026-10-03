using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ResumableOptionCacheTests
{
    [Fact]
    public void InterleavedTabletsKeepProgressAndParseEachOptionOnce()
    {
        var cache = new ResumableOptionCache<int>(); var calls = new List<string>();
        IReadOnlyList<int>? Slice(string key, int allowance)
        {
            var used = 0;
            return cache.Advance(key, 4, i => { calls.Add(key + i); used++; return i; }, () => used >= allowance);
        }
        Assert.Null(Slice("inventory", 2)); Assert.Null(Slice("reward", 1));
        Assert.Null(Slice("inventory", 1)); Assert.Null(Slice("reward", 2));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Slice("inventory", 1));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Slice("reward", 1));
        Assert.Equal(8, calls.Count); Assert.Equal(8, calls.Distinct().Count());
        Assert.Equal(new[] { 0, 1, 2, 3 }, Slice("reward", 0)); Assert.Equal(8, calls.Count);
    }

    [Fact]
    public void EmptySliceDoesNoWorkAndChangedQueryUsesIndependentKey()
    {
        var cache = new ResumableOptionCache<int>(); var calls = 0;
        Assert.Null(cache.Advance("query A", 2, i => { calls++; return i; }, () => true)); Assert.Equal(0, calls);
        Assert.Equal(new[] { 10, 11 }, cache.Advance("query B", 2, i => i + 10, () => false));
        Assert.Equal(new[] { 0, 1 }, cache.Advance("query A", 2, i => i, () => false));
    }

    [Fact]
    public void ExceptionDoesNotCacheAnIncompleteResultAndCancellationCanStopSlice()
    {
        var cache = new ResumableOptionCache<int>();
        Assert.Throws<OperationCanceledException>(() => cache.Advance("a", 3, i => i == 1 ? throw new OperationCanceledException() : i, () => false));
        Assert.False(cache.TryGet("a", out _)); var calls = new List<int>();
        Assert.Equal(new[] { 0, 1, 2 }, cache.Advance("a", 3, i => { calls.Add(i); return i; }, () => false));
        Assert.Equal(new[] { 1, 2 }, calls);
    }

    [Fact]
    public void CapacityAndClearReleaseOldMaps()
    {
        var cache = new ResumableOptionCache<int>(2);
        foreach (var key in new[] { "a", "b", "c" }) cache.Advance(key, 1, _ => 1, () => false);
        Assert.False(cache.TryGet("a", out _)); Assert.False(cache.TryGet("b", out _)); Assert.True(cache.TryGet("c", out _));
        cache.Clear(); Assert.False(cache.TryGet("c", out _));
    }
}
