namespace SephiriaBuildOverlay.Plugin;

// Main-thread, bounded pure-data work. Different tablet maps may interleave
// without discarding the previous map's progress or parsing a row twice.
internal sealed class ResumableOptionCache<T>
{
    private readonly int _capacity;
    private readonly Dictionary<string, List<T>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<T>> _completed = new(StringComparer.Ordinal);
    public ResumableOptionCache(int capacity = 128)
    { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); _capacity = capacity; }
    public bool TryGet(string key, out IReadOnlyList<T> result) => _completed.TryGetValue(key, out result!);
    public IReadOnlyList<T>? Advance(string key, int count, Func<int, T> build, Func<bool> yield)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (TryGet(key, out var cached)) return cached;
        if (!_pending.TryGetValue(key, out var values))
        {
            if (_pending.Count + _completed.Count >= _capacity) Clear();
            _pending[key] = values = new List<T>();
        }
        while (values.Count < count)
        {
            if (yield()) return null;
            values.Add(build(values.Count));
        }
        var result = Array.AsReadOnly(values.ToArray());
        _completed[key] = result; _pending.Remove(key); return result;
    }
    public void Clear() { _pending.Clear(); _completed.Clear(); }
}
