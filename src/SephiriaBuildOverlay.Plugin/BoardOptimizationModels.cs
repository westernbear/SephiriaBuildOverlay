using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal enum BoardEffectKind { Add, Disable, IgnoreCriteria, Multiply }
internal enum BoardConditionKind { None, AnyItem, Charm, Placed }
internal enum ArtifactCondition { None, Top, Bottom, Side, Outline, Inside, BothCharms, BothEmpty, NeighborsFull, NearMagic, External, Unknown }

internal sealed class BoardEffect
{
    public BoardEffect(GridPoint position, BoardEffectKind kind, int value = 1) { Position = position; Kind = kind; Value = value; }
    public GridPoint Position { get; }
    public BoardEffectKind Kind { get; }
    public int Value { get; }
}
internal sealed class BoardCondition
{
    public BoardCondition(GridPoint position, BoardConditionKind kind) { Position = position; Kind = kind; }
    public GridPoint Position { get; }
    public BoardConditionKind Kind { get; }
}
internal sealed class BoardTabletOption
{
    public BoardTabletOption(GridPoint position, int rotation, IEnumerable<BoardEffect> effects, IEnumerable<BoardCondition>? conditions = null)
    { Position = position; Rotation = rotation; Effects = effects.ToArray(); Conditions = (conditions ?? Array.Empty<BoardCondition>()).ToArray(); }
    public GridPoint Position { get; }
    public int Rotation { get; }
    public IReadOnlyList<BoardEffect> Effects { get; }
    public IReadOnlyList<BoardCondition> Conditions { get; }
}
internal sealed class BoardTablet
{
    public BoardTablet(string id, string key, GridPoint position, int rotation, bool movable, IEnumerable<BoardTabletOption> options)
    { Id = id; Key = key; Position = position; Rotation = rotation; Movable = movable; Options = options as IReadOnlyList<BoardTabletOption> ?? options.ToArray(); }
    public string Id { get; }
    public string Key { get; }
    public GridPoint Position { get; }
    public int Rotation { get; }
    public bool Movable { get; }
    public IReadOnlyList<BoardTabletOption> Options { get; }
}
internal sealed class BoardArtifact
{
    public BoardArtifact(string id, string key, GridPoint position, int maximum, int enchant, bool movable,
        ArtifactCondition condition = ArtifactCondition.None, bool externalActive = true, bool magic = false, bool conditionActive = true)
    { Id = id; Key = key; Position = position; Maximum = maximum; Enchant = enchant; Movable = movable; Condition = condition; ExternalActive = externalActive; Magic = magic; ConditionActive = conditionActive; }
    public string Id { get; }
    public string Key { get; }
    public GridPoint Position { get; }
    public int Maximum { get; }
    public int Enchant { get; }
    public bool Movable { get; }
    public ArtifactCondition Condition { get; }
    public bool ExternalActive { get; }
    public bool Magic { get; }
    public bool ConditionActive { get; }
}
internal sealed class BoardCell
{
    public BoardCell(GridPoint position, int level, int disabled = 0, int ignore = 0, int multiplier = 0)
    { Position = position; Level = level; Disabled = disabled; Ignore = ignore; Multiplier = multiplier; }
    public GridPoint Position { get; }
    public int Level { get; }
    public int Disabled { get; }
    public int Ignore { get; }
    public int Multiplier { get; }
}
internal sealed class BoardOptimizationInput
{
    public BoardOptimizationInput(int width, int height, int storage, IEnumerable<BoardCell> cells,
        IEnumerable<BoardArtifact> artifacts, IEnumerable<BoardTablet> tablets, IReadOnlyDictionary<string, GridPoint> items,
        IEnumerable<ArtifactTarget> goals, string? unavailable = null, bool globallyActive = true)
    { Width = width; Height = height; Storage = storage; Cells = cells.ToArray(); Artifacts = artifacts.ToArray(); Tablets = tablets.ToArray();
      Items = new Dictionary<string, GridPoint>(items); Goals = goals.Where(x => x.Role is TargetRole.Required or TargetRole.Recommended).ToArray(); Unavailable = unavailable; GloballyActive = globallyActive; }
    public int Width { get; }
    public int Height { get; }
    public int Storage { get; }
    public IReadOnlyList<BoardCell> Cells { get; }
    public IReadOnlyList<BoardArtifact> Artifacts { get; }
    public IReadOnlyList<BoardTablet> Tablets { get; }
    public IReadOnlyDictionary<string, GridPoint> Items { get; }
    public IReadOnlyList<ArtifactTarget> Goals { get; }
    public string? Unavailable { get; }
    public bool GloballyActive { get; }
}
internal sealed class BoardLayout
{
    public BoardLayout(IReadOnlyDictionary<string, GridPoint> positions, IReadOnlyDictionary<string, int> rotations)
    { Positions = new Dictionary<string, GridPoint>(positions); Rotations = new Dictionary<string, int>(rotations); }
    public IReadOnlyDictionary<string, GridPoint> Positions { get; }
    public IReadOnlyDictionary<string, int> Rotations { get; }
}
internal sealed class BoardOptimizationResult
{
    public BoardOptimizationResult(BoardLayout layout, BoardObjective before, BoardObjective after, int evaluations, bool budgetReached, string? unavailable = null, bool provenOptimal = false)
    { Layout = layout; Before = before; After = after; Evaluations = evaluations; BudgetReached = budgetReached; Unavailable = unavailable; ProvenOptimal = provenOptimal; }
    public BoardLayout Layout { get; }
    public BoardObjective Before { get; }
    public BoardObjective After { get; }
    public int Evaluations { get; }
    public bool BudgetReached { get; }
    public string? Unavailable { get; }
    public bool ProvenOptimal { get; }
    public bool Improved => Unavailable is null && After.CompareBenefits(Before) > 0;
}
internal readonly struct BoardObjective : IComparable<BoardObjective>
{
    public BoardObjective(long requiredActive, long requiredLevel, long recommendedActive, long recommendedLevel, int movement, int rotations,
        long requiredPriority = 0, long recommendedPriority = 0)
    { RequiredActive = requiredActive; RequiredLevel = requiredLevel; RecommendedActive = recommendedActive; RecommendedLevel = recommendedLevel; Movement = movement; Rotations = rotations;
      RequiredPriority = requiredPriority; RecommendedPriority = recommendedPriority; }
    public long RequiredActive { get; }
    public long RequiredLevel { get; }
    public long RecommendedActive { get; }
    public long RecommendedLevel { get; }
    public int Movement { get; }
    public int Rotations { get; }
    public long RequiredPriority { get; }
    public long RecommendedPriority { get; }
    public int CompareBenefits(BoardObjective other)
    {
        var c = RequiredActive.CompareTo(other.RequiredActive); if (c != 0) return c;
        c = RequiredLevel.CompareTo(other.RequiredLevel); if (c != 0) return c;
        c = RequiredPriority.CompareTo(other.RequiredPriority); if (c != 0) return c;
        c = RecommendedActive.CompareTo(other.RecommendedActive); if (c != 0) return c;
        c = RecommendedLevel.CompareTo(other.RecommendedLevel); if (c != 0) return c;
        return RecommendedPriority.CompareTo(other.RecommendedPriority);
    }
    public int CompareTo(BoardObjective other)
    { var c = CompareBenefits(other); if (c != 0) return c; c = other.Movement.CompareTo(Movement); return c != 0 ? c : other.Rotations.CompareTo(Rotations); }
}
