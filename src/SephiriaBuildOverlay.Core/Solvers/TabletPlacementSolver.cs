namespace SephiriaBuildOverlay.Core.Solvers;

public sealed class TabletEffect
{
    public TabletEffect(string category, int minimumCovered, bool required, int level, int comboValue = 0)
    {
        Category = category;
        MinimumCovered = minimumCovered;
        Required = required;
        Level = level;
        ComboValue = comboValue;
    }
    public string Category { get; }
    public int MinimumCovered { get; }
    public bool Required { get; }
    public int Level { get; }
    public int ComboValue { get; }
}

public sealed class TabletPiece
{
    public TabletPiece(string id, IReadOnlyList<GridPoint> shape, IReadOnlyList<int> allowedRotations,
        IReadOnlyList<TabletEffect> effects, GridPoint? currentOrigin = null, int currentRotation = 0, bool fixedEngraving = false)
    {
        if (fixedEngraving && !currentOrigin.HasValue)
            throw new ArgumentException("A fixed engraving requires its current origin.", nameof(currentOrigin));
        Id = id;
        Shape = shape;
        AllowedRotations = fixedEngraving ? new[] { NormalizeRotation(currentRotation) } : allowedRotations.Select(NormalizeRotation).Distinct().ToArray();
        Effects = effects;
        CurrentOrigin = currentOrigin;
        CurrentRotation = NormalizeRotation(currentRotation);
        FixedEngraving = fixedEngraving;
    }
    public string Id { get; }
    public IReadOnlyList<GridPoint> Shape { get; }
    public IReadOnlyList<int> AllowedRotations { get; }
    public IReadOnlyList<TabletEffect> Effects { get; }
    public GridPoint? CurrentOrigin { get; }
    public int CurrentRotation { get; }
    public bool FixedEngraving { get; }
    private static int NormalizeRotation(int rotation) => ((rotation % 4) + 4) % 4;
}

public sealed class TabletBoardArtifact
{
    public TabletBoardArtifact(GridPoint position, string category) { Position = position; Category = category; }
    public GridPoint Position { get; }
    public string Category { get; }
}

public sealed class TabletBoard
{
    public TabletBoard(int width, int height, IEnumerable<GridPoint>? blocked, IEnumerable<TabletBoardArtifact>? artifacts)
    {
        Width = width;
        Height = height;
        Blocked = new HashSet<GridPoint>(blocked ?? Array.Empty<GridPoint>());
        Artifacts = (artifacts ?? Array.Empty<TabletBoardArtifact>()).ToArray();
    }
    public int Width { get; }
    public int Height { get; }
    public HashSet<GridPoint> Blocked { get; }
    public IReadOnlyList<TabletBoardArtifact> Artifacts { get; }
    public bool Contains(GridPoint point) => point.X >= 0 && point.X < Width && point.Y >= 0 && point.Y < Height;
}

public sealed class TabletPlacement
{
    public TabletPlacement(string tabletId, GridPoint origin, int rotation, IReadOnlyList<GridPoint> occupiedCells)
    {
        TabletId = tabletId; Origin = origin; Rotation = rotation; OccupiedCells = occupiedCells;
    }
    public string TabletId { get; }
    public GridPoint Origin { get; }
    public int Rotation { get; }
    public IReadOnlyList<GridPoint> OccupiedCells { get; }
}

public readonly struct PlacementScore : IComparable<PlacementScore>
{
    public PlacementScore(int requiredActivated, int requiredEffectLevel, int recommendedActivated, int comboValue, int movementCost, int rotationCost)
    {
        RequiredActivated = requiredActivated;
        RequiredEffectLevel = requiredEffectLevel;
        RecommendedActivated = recommendedActivated;
        ComboValue = comboValue;
        MovementCost = movementCost;
        RotationCost = rotationCost;
    }
    public int RequiredActivated { get; }
    public int RequiredEffectLevel { get; }
    public int RecommendedActivated { get; }
    public int ComboValue { get; }
    public int MovementCost { get; }
    public int RotationCost { get; }
    public int CompareTo(PlacementScore other)
    {
        var values = new[]
        {
            RequiredActivated.CompareTo(other.RequiredActivated),
            RequiredEffectLevel.CompareTo(other.RequiredEffectLevel),
            RecommendedActivated.CompareTo(other.RecommendedActivated),
            ComboValue.CompareTo(other.ComboValue),
            other.MovementCost.CompareTo(MovementCost),
            other.RotationCost.CompareTo(RotationCost)
        };
        return values.FirstOrDefault(x => x != 0);
    }
}

public sealed class TabletPlacementResult
{
    public TabletPlacementResult(IReadOnlyList<TabletPlacement> placements, PlacementScore score, bool allPlaced)
    {
        Placements = placements; Score = score; AllPlaced = allPlaced;
    }
    public IReadOnlyList<TabletPlacement> Placements { get; }
    public PlacementScore Score { get; }
    public bool AllPlaced { get; }
}

public sealed class TabletPlacementSolver
{
    public TabletPlacementResult Solve(TabletBoard board, IReadOnlyList<TabletPiece> tablets, CancellationToken cancellationToken = default)
    {
        TabletPlacementResult? best = null;
        Search(0, new HashSet<GridPoint>(board.Blocked), new List<TabletPlacement>());
        return best ?? new TabletPlacementResult(Array.Empty<TabletPlacement>(), default, tablets.Count == 0);

        void Search(int index, HashSet<GridPoint> occupied, List<TabletPlacement> current)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index == tablets.Count)
            {
                var result = new TabletPlacementResult(current.ToArray(), Score(board, tablets, current), current.Count == tablets.Count);
                if (best is null || result.Score.CompareTo(best.Score) > 0 ||
                    (result.Score.CompareTo(best.Score) == 0 && string.Compare(StableKey(result), StableKey(best), StringComparison.Ordinal) < 0))
                    best = result;
                return;
            }

            var tablet = tablets[index];
            // Skipping makes a full or impossible board return the best partial plan instead of failing.
            if (!tablet.FixedEngraving) Search(index + 1, occupied, current);
            foreach (var rotation in tablet.AllowedRotations.OrderBy(x => x))
            {
                var shape = Rotate(tablet.Shape, rotation);
                for (var y = 0; y < board.Height; y++)
                for (var x = 0; x < board.Width; x++)
                {
                    var origin = new GridPoint(x, y);
                    if (tablet.FixedEngraving && !origin.Equals(tablet.CurrentOrigin!.Value)) continue;
                    var cells = shape.Select(p => p.Add(origin)).ToArray();
                    if (cells.Any(p => !board.Contains(p) || occupied.Contains(p))) continue;
                    foreach (var cell in cells) occupied.Add(cell);
                    current.Add(new TabletPlacement(tablet.Id, origin, rotation, cells));
                    Search(index + 1, occupied, current);
                    current.RemoveAt(current.Count - 1);
                    foreach (var cell in cells) occupied.Remove(cell);
                }
            }
        }
    }

    private static PlacementScore Score(TabletBoard board, IReadOnlyList<TabletPiece> pieces, IReadOnlyList<TabletPlacement> placements)
    {
        var required = 0; var requiredLevel = 0; var recommended = 0; var combo = 0; var movement = 0; var rotations = 0;
        foreach (var placement in placements)
        {
            var piece = pieces.First(x => x.Id == placement.TabletId);
            foreach (var effect in piece.Effects)
            {
                var covered = board.Artifacts.Count(x => x.Category == effect.Category && placement.OccupiedCells.Contains(x.Position));
                if (covered < effect.MinimumCovered) continue;
                if (effect.Required) { required++; requiredLevel += effect.Level; } else recommended++;
                combo += effect.ComboValue;
            }
            if (piece.CurrentOrigin.HasValue) movement += piece.CurrentOrigin.Value.ManhattanDistance(placement.Origin);
            rotations += RotationDistance(piece.CurrentRotation, placement.Rotation);
        }
        return new PlacementScore(required, requiredLevel, recommended, combo, movement, rotations);
    }

    private static IReadOnlyList<GridPoint> Rotate(IReadOnlyList<GridPoint> shape, int rotations)
    {
        var result = shape.ToArray();
        for (var step = 0; step < rotations; step++)
            for (var index = 0; index < result.Length; index++) result[index] = result[index].Rotate90();
        var minX = result.Min(x => x.X); var minY = result.Min(x => x.Y);
        return result.Select(x => new GridPoint(x.X - minX, x.Y - minY)).Distinct().ToArray();
    }

    private static int RotationDistance(int a, int b)
    {
        var difference = Math.Abs(a - b);
        return Math.Min(difference, 4 - difference);
    }

    private static string StableKey(TabletPlacementResult result) => string.Join(";", result.Placements
        .OrderBy(x => x.TabletId, StringComparer.Ordinal)
        .Select(x => $"{x.TabletId}:{x.Origin.X}:{x.Origin.Y}:{x.Rotation}"));
}
