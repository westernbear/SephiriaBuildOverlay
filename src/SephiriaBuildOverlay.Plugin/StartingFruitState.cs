using SephiriaBuildOverlay.Core.Import;

namespace SephiriaBuildOverlay.Plugin;

internal static class StartingFruitState
{
    public static bool Matches(NativePreset expected, int? adaptive, IReadOnlyList<(string Category, int Value)>? fruits)
    {
        if (adaptive != expected.Adaptive || fruits is null) return false;
        // Multiplicity and +/- weights matter. An empty first-stage state or a
        // deduplicated response must not report the completed preset as applied.
        var counts = expected.Fruits.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        var observed = fruits.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        return counts.Count == observed.Count && counts.All(x => observed.TryGetValue(x.Key, out var count) && count == x.Value);
    }
}
