using System.Numerics;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

// Bounded deterministic coordinate search, not an exhaustive/global optimum.
// Native query parsing happens before this class: no Unity objects/delegates.
internal sealed class JointBoardPlanner
{
    public static BoardLayout Current(BoardOptimizationInput input)
    {
        var positions = input.Items.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var tablet in input.Tablets) positions[tablet.Id] = tablet.Position;
        return new BoardLayout(positions, input.Tablets.ToDictionary(x => x.Id, x => x.Rotation, StringComparer.Ordinal));
    }

    public BoardOptimizationResult Solve(BoardOptimizationInput input, CancellationToken cancellationToken = default, int evaluationBudget = 6000)
    {
        var start = Current(input);
        if (input.Unavailable is not null || input.Cells.Count > 64 || input.Artifacts.Any(x => x.Condition == ArtifactCondition.Unknown))
            return new BoardOptimizationResult(start, default, default, 0, false, input.Unavailable ?? "지원하지 않는 보드/아티팩트 조건");
        var baseline = Evaluate(input, start);
        var best = start; var bestScore = baseline; var evaluations = 0;
        var movable = new HashSet<string>(input.Artifacts.Where(x => x.Movable).Select(x => x.Id)
            .Concat(input.Tablets.Where(x => x.Movable).Select(x => x.Id)), StringComparer.Ordinal);
        var orderedTablets = input.Tablets.Where(x => x.Movable).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var orderedArtifacts = input.Artifacts.Where(x => x.Movable).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        bool Try(BoardLayout? candidate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate is null || evaluations >= evaluationBudget) return false;
            evaluations++;
            var score = Evaluate(input, candidate);
            if (score.CompareTo(bestScore) <= 0) return false;
            best = candidate; bestScore = score; return true;
        }
        for (var pass = 0; pass < 6 && evaluations < evaluationBudget; pass++)
        {
            var changed = Try(MatchArtifacts(input, best, cancellationToken));
            foreach (var tablet in orderedTablets)
            {
                var seed = best;
                foreach (var option in tablet.Options.OrderBy(x => seed.Positions[tablet.Id].ManhattanDistance(x.Position))
                    .ThenBy(x => (x.Rotation - seed.Rotations[tablet.Id] + 4) % 4).ThenBy(x => x.Position.Y).ThenBy(x => x.Position.X))
                {
                    if (evaluations >= evaluationBudget) break;
                    var moved = Relocate(input, seed, tablet.Id, option.Position, movable);
                    if (moved is null) continue;
                    var rotations = moved.Rotations.ToDictionary(x => x.Key, x => x.Value);
                    rotations[tablet.Id] = option.Rotation;
                    var candidate = new BoardLayout(moved.Positions, rotations);
                    changed |= Try(candidate);
                    // A tablet may create a useful slot that is empty in the
                    // current layout. Score the joint assignment as well, even
                    // when moving/rotating the tablet alone gives no benefit.
                    if (evaluations < evaluationBudget) changed |= Try(MatchArtifacts(input, candidate, cancellationToken));
                }
            }
            changed |= Try(MatchArtifacts(input, best, cancellationToken));
            // Exact full-board scoring for neighbor and conditional-tablet
            // changes, including occupied destinations/full-board swaps.
            foreach (var artifact in orderedArtifacts)
            {
                var seed = best;
                foreach (var cell in input.Cells.OrderBy(x => x.Position.Y).ThenBy(x => x.Position.X))
                {
                    if (evaluations >= evaluationBudget) break;
                    changed |= Try(Relocate(input, seed, artifact.Id, cell.Position, movable));
                }
            }
            if (!changed) break;
        }
        return new BoardOptimizationResult(best, baseline, bestScore, evaluations, evaluations >= evaluationBudget);
    }

    private static BoardLayout? Relocate(BoardOptimizationInput input, BoardLayout layout, string id, GridPoint destination, HashSet<string> movable)
    {
        var from = layout.Positions[id];
        var occupant = input.Items.Keys.FirstOrDefault(x => x != id && layout.Positions[x].Equals(destination));
        if (occupant is not null && !movable.Contains(occupant)) return null;
        var positions = layout.Positions.ToDictionary(x => x.Key, x => x.Value);
        positions[id] = destination;
        if (occupant is not null) positions[occupant] = from;
        var result = new BoardLayout(positions, layout.Rotations);
        return input.Tablets.All(t => Option(t, result) is not null) ? result : null;
    }

    private static BoardTabletOption? Option(BoardTablet tablet, BoardLayout layout) => tablet.Options.FirstOrDefault(x =>
        x.Position.Equals(layout.Positions[tablet.Id]) && x.Rotation == layout.Rotations[tablet.Id]);

    internal static IReadOnlyList<BoardCell> Cells(BoardOptimizationInput input, BoardLayout layout)
    {
        var positions = input.Items.Keys.ToDictionary(x => layout.Positions[x], x => x);
        var charms = new HashSet<GridPoint>(input.Artifacts.Select(x => layout.Positions[x.Id]));
        var values = input.Cells.ToDictionary(x => x.Position, x => new[] { x.Level, x.Disabled, x.Ignore, x.Multiplier });
        foreach (var artifact in input.Artifacts)
            if (values.TryGetValue(layout.Positions[artifact.Id], out var cell)) cell[0] = checked(cell[0] + artifact.Enchant);
        foreach (var tablet in input.Tablets)
        {
            var option = Option(tablet, layout) ?? throw new InvalidOperationException("Invalid tablet placement");
            var hasPlaced = false; var placedHit = false; var active = true;
            foreach (var condition in option.Conditions)
            {
                switch (condition.Kind)
                {
                    case BoardConditionKind.AnyItem: active &= positions.ContainsKey(condition.Position); break;
                    case BoardConditionKind.Charm: active &= charms.Contains(condition.Position); break;
                    case BoardConditionKind.Placed: hasPlaced = true; placedHit |= option.Position.Equals(condition.Position); break;
                    case BoardConditionKind.None: placedHit = true; break;
                }
            }
            if (!active || hasPlaced && !placedHit) continue;
            foreach (var effect in option.Effects)
            {
                if (!values.TryGetValue(effect.Position, out var cell)) continue;
                var index = effect.Kind == BoardEffectKind.Add ? 0 : effect.Kind == BoardEffectKind.Disable ? 1 : effect.Kind == BoardEffectKind.IgnoreCriteria ? 2 : 3;
                cell[index] = checked(cell[index] + effect.Value);
            }
        }
        return values.Select(x => new BoardCell(x.Key, x.Value[3] == 0 ? x.Value[0] : checked(x.Value[0] * x.Value[3]), x.Value[1], x.Value[2], x.Value[3])).ToArray();
    }

    internal static bool Criteria(BoardOptimizationInput input, BoardLayout layout, BoardArtifact artifact, GridPoint p)
    {
        bool Has(int dx, int dy, bool charm = false, bool magic = false) => charm || magic
            ? input.Artifacts.Any(x => layout.Positions[x.Id].Equals(p.Add(new GridPoint(dx, dy))) && (!magic || x.Magic))
            : input.Items.Keys.Any(x => layout.Positions[x].Equals(p.Add(new GridPoint(dx, dy))));
        var index = p.Y * input.Width + p.X;
        switch (artifact.Condition)
        {
            case ArtifactCondition.None: return true;
            case ArtifactCondition.Top: return p.Y == 0;
            case ArtifactCondition.Bottom: return index >= input.Storage - 6;
            case ArtifactCondition.Side: return p.X == 0 || p.X == 5; // Native GetCriteria, not its inconsistent preview method.
            case ArtifactCondition.Outline: return p.X <= 0 || p.Y <= 0 || p.X >= input.Width - 1 || index >= input.Storage - 6;
            case ArtifactCondition.Inside: return p.X > 0 && p.Y > 0 && p.X < input.Width - 1 && index + 7 <= input.Storage - 1;
            case ArtifactCondition.BothCharms: return p.X > 0 && p.X < input.Width - 1 && Has(-1, 0, charm: true) && Has(1, 0, charm: true);
            case ArtifactCondition.BothEmpty:
                var remainder = input.Storage % input.Width;
                return p.X > 0 && p.X < input.Width - 1 && (remainder == 0 || p.Y < input.Height - 1 || p.X < remainder - 1) && !Has(-1, 0) && !Has(1, 0);
            case ArtifactCondition.NeighborsFull:
                for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++) if ((x != 0 || y != 0) && !Has(x, y)) return false;
                return true;
            case ArtifactCondition.NearMagic:
                for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++) if ((x != 0 || y != 0) && Has(x, y, magic: true)) return true;
                return false;
            case ArtifactCondition.External: return artifact.ConditionActive;
            default: return false;
        }
    }

    private static (bool Active, int Level) Value(BoardOptimizationInput input, BoardLayout layout, BoardArtifact artifact, GridPoint position, BoardCell cell)
    {
        var active = cell.Disabled <= 0 && input.GloballyActive && cell.Level >= 0 && artifact.ExternalActive &&
            (cell.Ignore > 0 || Criteria(input, layout, artifact, position));
        return (active, active ? Math.Min(artifact.Maximum, cell.Level) : 0);
    }

    internal static BoardObjective Evaluate(BoardOptimizationInput input, BoardLayout layout)
    {
        var cells = Cells(input, layout).ToDictionary(x => x.Position);
        long requiredActive = 0, requiredLevel = 0, recommendedActive = 0, recommendedLevel = 0;
        foreach (var goal in input.Goals)
        {
            var values = input.Artifacts.Where(x => x.Key == goal.CatalogKey)
                .Select(x => Value(input, layout, x, layout.Positions[x.Id], cells[layout.Positions[x.Id]]))
                .OrderByDescending(x => x.Active).ThenByDescending(x => x.Level).Take(goal.DesiredAcquisitions).ToArray();
            if (goal.Role == TargetRole.Required) { requiredActive += values.Count(x => x.Active); requiredLevel += values.Sum(x => (long)x.Level); }
            else { recommendedActive += values.Count(x => x.Active); recommendedLevel += values.Sum(x => (long)x.Level); }
        }
        // Category combos depend on owned instances, not their slots/rotation;
        // ownership is constant throughout this solver, so that tier is constant.
        var movement = input.Items.Sum(x => x.Value.ManhattanDistance(layout.Positions[x.Key]));
        var rotations = input.Tablets.Sum(x => (layout.Rotations[x.Id] - x.Rotation + 4) % 4);
        return new BoardObjective(requiredActive, requiredLevel, recommendedActive, recommendedLevel, movement, rotations);
    }

    private static BoardLayout MatchArtifacts(BoardOptimizationInput input, BoardLayout layout, CancellationToken cancellationToken)
    {
        var artifacts = input.Artifacts.Where(x => x.Movable).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        if (artifacts.Length == 0) return layout;
        var ids = new HashSet<string>(artifacts.Select(x => x.Id));
        var blocked = new HashSet<GridPoint>(input.Items.Keys.Where(x => !ids.Contains(x)).Select(x => layout.Positions[x]));
        var cells = Cells(input, layout).Where(x => !blocked.Contains(x.Position)).OrderBy(x => x.Position.Y).ThenBy(x => x.Position.X).ToArray();
        if (cells.Length < artifacts.Length) return layout;
        var n = cells.Length;
        var levelBound = artifacts.Sum(x => (long)x.Maximum) + 1;
        var movementBound = (long)n * (input.Width + input.Height) + 1;
        BigInteger recommendedLevelWeight = movementBound;
        var recommendedActiveWeight = levelBound * recommendedLevelWeight;
        var requiredLevelWeight = (n + 1) * recommendedActiveWeight;
        var requiredActiveWeight = levelBound * requiredLevelWeight;
        var costs = new BigInteger[n, n]; var infinity = requiredActiveWeight * (n + 2) * 4;
        for (var i = 0; i < artifacts.Length; i++)
        for (var j = 0; j < n; j++)
        {
            var artifact = artifacts[i]; var cell = cells[j];
            var goal = input.Goals.FirstOrDefault(x => x.CatalogKey == artifact.Key);
            // Correct the per-instance enchant: the level map includes the
            // occupant's enchant, which must not be carried with the slot.
            var resident = input.Artifacts.FirstOrDefault(x => layout.Positions[x.Id].Equals(cell.Position));
            var multiplier = cell.Multiplier == 0 ? 1 : cell.Multiplier;
            var projected = new BoardCell(cell.Position, checked(cell.Level + (artifact.Enchant - (resident?.Enchant ?? 0)) * multiplier), cell.Disabled, cell.Ignore, cell.Multiplier);
            var value = Value(input, layout, artifact, cell.Position, projected);
            var benefit = goal?.Role == TargetRole.Required
                ? (value.Active ? requiredActiveWeight : 0) + value.Level * requiredLevelWeight
                : (value.Active ? recommendedActiveWeight : 0) + value.Level * recommendedLevelWeight;
            costs[i, j] = artifact.Position.ManhattanDistance(cell.Position) - benefit;
        }
        var assignment = MinimumCostMatching(costs, infinity, cancellationToken);
        var positions = layout.Positions.ToDictionary(x => x.Key, x => x.Value);
        for (var i = 0; i < artifacts.Length; i++) positions[artifacts[i].Id] = cells[assignment[i]].Position;
        return new BoardLayout(positions, layout.Rotations);
    }

    // Hungarian O(n^3), all rows share the SAME destination columns. Thus
    // different keys and duplicate instances cannot claim the same cell.
    private static int[] MinimumCostMatching(BigInteger[,] costs, BigInteger infinity, CancellationToken token)
    {
        var n = costs.GetLength(0); var u = new BigInteger[n + 1]; var v = new BigInteger[n + 1]; var p = new int[n + 1]; var way = new int[n + 1];
        for (var i = 1; i <= n; i++)
        {
            token.ThrowIfCancellationRequested(); p[0] = i;
            var j0 = 0; var min = Enumerable.Repeat(infinity, n + 1).ToArray(); var used = new bool[n + 1];
            do
            {
                used[j0] = true; var i0 = p[j0]; var delta = infinity; var j1 = 0;
                for (var j = 1; j <= n; j++) if (!used[j])
                {
                    var current = costs[i0 - 1, j - 1] - u[i0] - v[j];
                    if (current < min[j]) { min[j] = current; way[j] = j0; }
                    if (min[j] < delta) { delta = min[j]; j1 = j; }
                }
                for (var j = 0; j <= n; j++) if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else min[j] -= delta;
                j0 = j1;
            } while (p[j0] != 0);
            do { var j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
        }
        var result = new int[n]; for (var j = 1; j <= n; j++) result[p[j] - 1] = j - 1;
        return result;
    }
}
