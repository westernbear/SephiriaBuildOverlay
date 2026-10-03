using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal enum ArtifactPlacementKind { Ordinary, Needle, ManaSupport, CooldownSupport, NeighborLevels, PlanetSupport, RowCompanions, RowCategory, WhitePaper }

// Immutable native metadata, copied on the Unity thread. No game objects,
// callbacks, foreign catalogs or combat/DPS estimates enter the solver.
internal sealed class ArtifactPlacementEffect
{
    public static readonly ArtifactPlacementEffect None = new();
    public static bool SupportsCategoryCallback(string? declaringType) => declaringType is
        "Charm_Basic" or "Charm_3Elemental_ByRow" or "Charm_UpCharmDamage" or "Charm_WhitePaper";
    // These native post-refresh effects have additional spell-specific links
    // outside the modeled profiles. Never misclassify them as ordinary stats.
    public static bool RequiresManualPlacement(string nativeType) => nativeType is "Charm_AutoMagic" or "Charm_NearMagicBullet";
    public ArtifactPlacementEffect(ArtifactPlacementKind kind = ArtifactPlacementKind.Ordinary,
        IEnumerable<string>? categories = null, bool attackable = false, bool companion = false, bool summonPlanet = false,
        bool preserveSide = false, GridPoint offset = default, IEnumerable<double>? amounts = null,
        IEnumerable<double>? dependencyAmounts = null, bool dependencyCondition = false, int maximumRarity = 0, int rarity = 0,
        IEnumerable<string>? rowCategories = null, int paperMatch = 2, IEnumerable<GridPoint>? neighborOffsets = null)
    {
        Kind = kind; Categories = (categories ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        Attackable = attackable; Companion = companion; SummonPlanet = summonPlanet; PreserveSide = preserveSide; Offset = offset;
        Amounts = (amounts ?? Array.Empty<double>()).ToArray(); DependencyAmounts = (dependencyAmounts ?? Array.Empty<double>()).ToArray();
        DependencyCondition = dependencyCondition; MaximumRarity = maximumRarity; Rarity = rarity;
        RowCategories = (rowCategories ?? Array.Empty<string>()).ToArray(); PaperMatch = paperMatch;
        NeighborOffsets = (neighborOffsets ?? Enumerable.Range(-1, 3).SelectMany(y => Enumerable.Range(-1, 3)
            .Where(x => x != 0 || y != 0).Select(x => new GridPoint(x, y)))).ToArray();
    }
    public ArtifactPlacementKind Kind { get; }
    public IReadOnlyList<string> Categories { get; }
    public bool Attackable { get; }
    public bool Companion { get; }
    public bool SummonPlanet { get; }
    public bool PreserveSide { get; }
    public GridPoint Offset { get; }
    public IReadOnlyList<double> Amounts { get; }
    public IReadOnlyList<double> DependencyAmounts { get; }
    public bool DependencyCondition { get; }
    public int MaximumRarity { get; }
    public int Rarity { get; }
    public IReadOnlyList<string> RowCategories { get; }
    public int PaperMatch { get; }
    public IReadOnlyList<GridPoint> NeighborOffsets { get; }
    public bool Directed => Kind is ArtifactPlacementKind.Needle or ArtifactPlacementKind.ManaSupport or ArtifactPlacementKind.CooldownSupport;
    public bool DynamicCategory => Kind is ArtifactPlacementKind.RowCategory or ArtifactPlacementKind.WhitePaper or ArtifactPlacementKind.Needle;
    public double At(int level, bool extra = false)
    {
        var table = extra ? DependencyAmounts : Amounts;
        return table.Count == 0 ? 0 : table[Math.Min(table.Count - 1, Math.Max(0, level))];
    }
}

internal sealed class BoardComboModel
{
    public static readonly BoardComboModel Empty = new();
    public BoardComboModel(IEnumerable<string>? goals = null, IReadOnlyDictionary<string, int>? offsets = null,
        IEnumerable<string>? protectedCategories = null)
    {
        Goals = (goals ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        Offsets = new Dictionary<string, int>(offsets ?? new Dictionary<string, int>(), StringComparer.Ordinal);
        ProtectedCategories = (protectedCategories ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
    }
    public IReadOnlyList<string> Goals { get; }
    public IReadOnlyDictionary<string, int> Offsets { get; }
    // Random engraving/unknown inventory-modifying combos cannot be simulated
    // from a fixed residual matrix. Preserve their counts, even during swaps.
    public IReadOnlyList<string> ProtectedCategories { get; }
}
