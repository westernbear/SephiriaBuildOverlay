using SephiriaBuildOverlay.Core.Import;

namespace SephiriaBuildOverlay.Plugin;

internal static class StartingPresetComparison
{
    public static IReadOnlyList<string> ChangedSections(NativePreset requested, NativePreset applied)
    {
        var changed = new List<string>();
        if (requested.Weapon != applied.Weapon) changed.Add("시작 무기");
        if (requested.Costume != applied.Costume) changed.Add("의상");
        if (requested.Skin != applied.Skin) changed.Add("스킨");
        if (!new HashSet<int>(requested.Favorites).SetEquals(applied.Favorites)) changed.Add("즐겨찾기");
        if (!SameCounts(requested.Passives.Where(x => x.Points > 0), applied.Passives.Where(x => x.Points > 0))) changed.Add("특성 포인트");
        // Imported -1 instance IDs become local IDs; that is not an exclusion.
        if (!SameCounts(requested.Pocket.Select(x => (x.Entity, x.Quantity)), applied.Pocket.Select(x => (x.Entity, x.Quantity)))) changed.Add("시작 주머니");
        if (requested.Adaptive != applied.Adaptive || !SameCounts(requested.Fruits, applied.Fruits)) changed.Add("과일꼬치");
        return changed;
    }

    private static bool SameCounts<T>(IEnumerable<T> left, IEnumerable<T> right) where T : notnull
    {
        var counts = left.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        var other = right.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        return counts.Count == other.Count && counts.All(x => other.TryGetValue(x.Key, out var count) && count == x.Value);
    }
}
