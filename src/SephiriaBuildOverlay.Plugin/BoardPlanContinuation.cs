namespace SephiriaBuildOverlay.Plugin;

// A confirmed move can pass through incomplete native effect maps. Keep the
// destination, not a runnable action, until a validated snapshot arrives.
internal sealed class BoardPlanContinuation
{
    private string? _invariant;
    private BoardLayout? _observed;
    private BoardLayout? _expected;
    private BoardOptimizationResult? _result;
    public BoardLayout? Expected => _expected;

    public void Hold(string invariant, BoardLayout current, BoardOptimizationResult result)
    {
        Clear();
        if (!result.Improved) return;
        _invariant = invariant; _observed = current; _result = result;
    }

    public void Expect(BoardLayout expected) => _expected = _result is null ? null : expected;

    public BoardOptimizationResult? Resume(string invariant, BoardLayout current)
    {
        if (_result is null) return null;
        if (_invariant != invariant || !Same(current, _observed!) && (_expected is null || !Same(current, _expected)))
        { Clear(); return null; }
        if (_expected is not null && Same(current, _expected)) { _observed = current; _expected = null; }
        if (Same(current, _result.Layout)) { Clear(); return null; }
        return _result;
    }

    public void Clear() { _invariant = null; _observed = null; _expected = null; _result = null; }

    internal static bool Same(BoardLayout a, BoardLayout b) => a.Positions.Count == b.Positions.Count && a.Rotations.Count == b.Rotations.Count &&
        a.Positions.All(x => b.Positions.TryGetValue(x.Key, out var p) && p.Equals(x.Value)) &&
        a.Rotations.All(x => b.Rotations.TryGetValue(x.Key, out var r) && r == x.Value);
}
