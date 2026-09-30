using System.Numerics;

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
    public ArtifactAssignmentResult Solve(IReadOnlyList<MovableArtifact> artifacts, IReadOnlyList<ArtifactDestination> destinations,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var size = Math.Max(artifacts.Count, destinations.Count);
        if (size == 0) return new ArtifactAssignmentResult(Array.Empty<ArtifactAssignment>(), 0, 0);
        if (destinations.Any(x => x.RequiredQuantityValue < 0 || x.RequiredEffectValue < 0 || x.RecommendedValue < 0 || x.ComboValue < 0))
            throw new ArgumentException("Objective values must be nonnegative.", nameof(destinations));
        // Each radix exceeds the maximum total contribution of every lower tier.
        // BigInteger avoids overflow for large boards or manually edited priorities.
        var movementBound = (BigInteger)size * (artifacts.Count == 0 || destinations.Count == 0 ? 0 :
            artifacts.Max(a => destinations.Max(d => a.Position.ManhattanDistance(d.Position))));
        BigInteger Sum(Func<ArtifactDestination, int> selector) => destinations.Aggregate(BigInteger.Zero, (total, d) => total + selector(d));
        var comboWeight = movementBound + 1;
        var recommendedWeight = Sum(d => d.ComboValue) * comboWeight + movementBound + 1;
        var effectWeight = Sum(d => d.RecommendedValue) * recommendedWeight + recommendedWeight;
        var quantityWeight = Sum(d => d.RequiredEffectValue) * effectWeight + effectWeight;
        var totalBenefit = Sum(d => d.RequiredQuantityValue) * quantityWeight + quantityWeight;
        var impossible = totalBenefit + movementBound + 1;
        var costs = new BigInteger[size, size];
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
        {
            if (row >= artifacts.Count || column >= destinations.Count) { costs[row, column] = 0; continue; }
            var artifact = artifacts[row]; var destination = destinations[column];
            if (artifact.CatalogKey != destination.CatalogKey) { costs[row, column] = impossible; continue; }
            var benefit = destination.RequiredQuantityValue * quantityWeight
                        + destination.RequiredEffectValue * effectWeight
                        + destination.RecommendedValue * recommendedWeight
                        + destination.ComboValue * comboWeight;
            costs[row, column] = artifact.Position.ManhattanDistance(destination.Position) - benefit;
        }

        var assignment = Hungarian(costs, (impossible + totalBenefit + movementBound + 1) * (size + 2), cancellationToken);
        var moves = new List<ArtifactAssignment>();
        var matchedSlots = new HashSet<int>();
        for (var row = 0; row < artifacts.Count; row++)
        {
            var column = assignment[row];
            if (column < 0 || column >= destinations.Count || costs[row, column] >= impossible) continue;
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
    private static int[] Hungarian(BigInteger[,] cost, BigInteger infinity, CancellationToken cancellationToken)
    {
        var n = cost.GetLength(0);
        var u = new BigInteger[n + 1]; var v = new BigInteger[n + 1]; var p = new int[n + 1]; var way = new int[n + 1];
        for (var i = 1; i <= n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            p[0] = i;
            var j0 = 0;
            var min = Enumerable.Repeat(infinity, n + 1).ToArray();
            var used = new bool[n + 1];
            do
            {
                used[j0] = true;
                var i0 = p[j0]; var delta = infinity; var j1 = 0;
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
