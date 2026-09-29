namespace SephiriaBuildOverlay.Core.Solvers;

public sealed class MovableArtifact
{
    public MovableArtifact(string instanceId, string catalogKey, GridPoint position)
    {
        InstanceId = instanceId; CatalogKey = catalogKey; Position = position;
    }
    public string InstanceId { get; }
    public string CatalogKey { get; }
    public GridPoint Position { get; }
}

public sealed class ArtifactDestination
{
    public ArtifactDestination(GridPoint position, string catalogKey, int requiredQuantityValue, int requiredEffectValue,
        int recommendedValue, int comboValue)
    {
        Position = position; CatalogKey = catalogKey; RequiredQuantityValue = requiredQuantityValue;
        RequiredEffectValue = requiredEffectValue; RecommendedValue = recommendedValue; ComboValue = comboValue;
    }
    public GridPoint Position { get; }
    public string CatalogKey { get; }
    public int RequiredQuantityValue { get; }
    public int RequiredEffectValue { get; }
    public int RecommendedValue { get; }
    public int ComboValue { get; }
}

public sealed class ArtifactAssignment
{
    public ArtifactAssignment(string instanceId, GridPoint from, GridPoint to, int movementCost)
    {
        InstanceId = instanceId; From = from; To = to; MovementCost = movementCost;
    }
    public string InstanceId { get; }
    public GridPoint From { get; }
    public GridPoint To { get; }
    public int MovementCost { get; }
}

public sealed class ArtifactAssignmentResult
{
    public ArtifactAssignmentResult(IReadOnlyList<ArtifactAssignment> assignments, int totalMovementCost, int unmatchedRequired)
    {
        Assignments = assignments; TotalMovementCost = totalMovementCost; UnmatchedRequired = unmatchedRequired;
    }
    public IReadOnlyList<ArtifactAssignment> Assignments { get; }
    public int TotalMovementCost { get; }
    public int UnmatchedRequired { get; }
}

public sealed class ArtifactAssignmentSolver
{
    private const long Impossible = 4_000_000_000_000L;

    public ArtifactAssignmentResult Solve(IReadOnlyList<MovableArtifact> artifacts, IReadOnlyList<ArtifactDestination> destinations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var size = Math.Max(artifacts.Count, destinations.Count);
        if (size == 0) return new ArtifactAssignmentResult(Array.Empty<ArtifactAssignment>(), 0, 0);
        var costs = new long[size, size];
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
        {
            if (row >= artifacts.Count || column >= destinations.Count) { costs[row, column] = 0; continue; }
            var artifact = artifacts[row]; var destination = destinations[column];
            if (artifact.CatalogKey != destination.CatalogKey) { costs[row, column] = Impossible; continue; }
            // Fixed positional weights encode the documented lexicographic objective; normal boards cannot overflow a lower tier.
            var benefit = destination.RequiredQuantityValue * 1_000_000_000L
                        + destination.RequiredEffectValue * 1_000_000L
                        + destination.RecommendedValue * 10_000L
                        + destination.ComboValue * 100L;
            costs[row, column] = artifact.Position.ManhattanDistance(destination.Position) - benefit;
        }

        var assignment = Hungarian(costs, cancellationToken);
        var moves = new List<ArtifactAssignment>();
        var matchedSlots = new HashSet<int>();
        for (var row = 0; row < artifacts.Count; row++)
        {
            var column = assignment[row];
            if (column < 0 || column >= destinations.Count || costs[row, column] >= Impossible) continue;
            // Non-beneficial slots are allowed only when they describe an exact already-desired location.
            var from = artifacts[row].Position; var to = destinations[column].Position;
            moves.Add(new ArtifactAssignment(artifacts[row].InstanceId, from, to, from.ManhattanDistance(to)));
            matchedSlots.Add(column);
        }
        var missingRequired = destinations.Select((x, index) => new { x, index })
            .Count(x => x.x.RequiredQuantityValue > 0 && !matchedSlots.Contains(x.index));
        return new ArtifactAssignmentResult(moves.OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToArray(), moves.Sum(x => x.MovementCost), missingRequired);
    }

    // O(n^3) minimum-cost perfect assignment, deterministic by row/column order.
    private static int[] Hungarian(long[,] cost, CancellationToken cancellationToken)
    {
        var n = cost.GetLength(0);
        var u = new long[n + 1]; var v = new long[n + 1]; var p = new int[n + 1]; var way = new int[n + 1];
        for (var i = 1; i <= n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            p[0] = i;
            var j0 = 0;
            var min = Enumerable.Repeat(long.MaxValue / 4, n + 1).ToArray();
            var used = new bool[n + 1];
            do
            {
                used[j0] = true;
                var i0 = p[j0]; var delta = long.MaxValue / 4; var j1 = 0;
                for (var j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    var current = cost[i0 - 1, j - 1] - u[i0] - v[j];
                    if (current < min[j]) { min[j] = current; way[j] = j0; }
                    if (min[j] < delta) { delta = min[j]; j1 = j; }
                }
                for (var j = 0; j <= n; j++)
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else min[j] -= delta;
                j0 = j1;
            } while (p[j0] != 0);
            do
            {
                var j1 = way[j0]; p[j0] = p[j1]; j0 = j1;
            } while (j0 != 0);
        }
        var result = Enumerable.Repeat(-1, n).ToArray();
        for (var j = 1; j <= n; j++) if (p[j] != 0) result[p[j] - 1] = j - 1;
        return result;
    }
}
