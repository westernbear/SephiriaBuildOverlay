namespace SephiriaBuildOverlay.Plugin;

internal static class StartingLoadoutState
{
    public static bool CostumeMatches(string costume, string skin, string? currentCostume, string? currentSkin) =>
        currentCostume == costume && currentSkin == skin;

    public static bool PocketMatches(IReadOnlyList<int> expected, IReadOnlyList<int>? observed, bool serverObserved) =>
        serverObserved && observed is not null && expected.OrderBy(x => x).SequenceEqual(observed.OrderBy(x => x));
}
