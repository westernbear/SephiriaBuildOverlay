using System.Numerics;
using System.Text;
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

    public BoardOptimizationResult Solve(BoardOptimizationInput input, CancellationToken cancellationToken = default, int evaluationBudget = 6000, bool useExactSearch = true, bool useMatchingCache = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = Current(input);
        if (input.Unavailable is not null || input.Cells.Count > 64 || input.Artifacts.Any(x => x.Condition == ArtifactCondition.Unknown))
            return new BoardOptimizationResult(start, default, default, 0, false, input.Unavailable ?? "지원하지 않는 보드/아티팩트 조건");
        if (input.Artifacts.Any(a => a.PlacementEffect.DynamicCategory))
        {
            var categories = new SpecialArtifactEvaluation(input, start, Cells(input, start).ToDictionary(x => x.Position));
            categories.ComboCounts();
            if (!categories.CategoriesReliable || input.Artifacts.Any(a => a.PlacementEffect.Kind == ArtifactPlacementKind.RowCategory && a.PlacementEffect.RowCategories.Count == 0))
                return new BoardOptimizationResult(start, default, default, 0, false, "예측할 수 없는 특수 아티팩트 분류는 수동 확인 필요");
        }
        var exact = useExactSearch ? SmallBoardExactSearch.Solve(input, cancellationToken, evaluationBudget) : null;
        if (exact is not null) return exact;
        var baseline = Evaluate(input, start);
        var best = start; var bestScore = baseline; var evaluations = 0;
        var movable = new HashSet<string>(input.Artifacts.Where(x => x.Movable).Select(x => x.Id)
            .Concat(input.Tablets.Where(x => x.Movable).Select(x => x.Id)), StringComparer.Ordinal);
        var orderedTablets = input.Tablets.Where(x => x.Movable).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var orderedArtifacts = input.Artifacts.Where(x => x.Movable).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var limit = orderedTablets.Length >= 2 && evaluationBudget >= 32 ? evaluationBudget - evaluationBudget / 4 : evaluationBudget;
        var beams = orderedTablets.ToDictionary(x => x.Id, _ => new List<(BoardTabletOption Option, BoardObjective Score)>());
        var matchingCache = new Dictionary<string, BoardLayout>(StringComparer.Ordinal);
        var positionIds = start.Positions.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var rotationIds = start.Rotations.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        BoardLayout Match(BoardLayout layout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!useMatchingCache) return MatchArtifacts(input, layout, cancellationToken);
            var key = new StringBuilder();
            foreach (var id in positionIds) { var p = layout.Positions[id]; key.Append(p.X).Append(',').Append(p.Y).Append(';'); }
            key.Append('|'); foreach (var id in rotationIds) key.Append(layout.Rotations[id]).Append(';');
            var signature = key.ToString();
            if (!matchingCache.TryGetValue(signature, out var result))
                matchingCache[signature] = result = MatchArtifacts(input, layout, cancellationToken);
            return result;
        }
        bool Try(BoardLayout? candidate, BoardTabletOption? beamOption = null, string? tabletId = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate is null || evaluations >= limit) return false;
            evaluations++;
            if (!SpecialArtifactRules.Allows(input, candidate)) return false;
            var score = Evaluate(input, candidate);
            if (beamOption is not null && tabletId is not null)
            {
                var beam = beams[tabletId];
                if (!beam.Any(x => ReferenceEquals(x.Option, beamOption))) beam.Add((beamOption, score));
                beam.Sort((a, b) => { var c = b.Score.CompareTo(a.Score); if (c != 0) return c;
                    c = a.Option.Position.Y.CompareTo(b.Option.Position.Y); if (c != 0) return c;
                    c = a.Option.Position.X.CompareTo(b.Option.Position.X); return c != 0 ? c : a.Option.Rotation.CompareTo(b.Option.Rotation); });
                if (beam.Count > 4) beam.RemoveAt(beam.Count - 1);
            }
            if (score.CompareTo(bestScore) <= 0) return false;
            best = candidate; bestScore = score; return true;
        }
        for (var pass = 0; pass < 6 && evaluations < limit; pass++)
        {
            var changed = false;
            var supportSeed = best;
            // Reserve work for coupled support/attacker moves before independent
            // assignment can exhaust the budget. All trials share the same cap.
            var supportLimit = Math.Min(limit, evaluations + Math.Max(1, evaluationBudget / 4));
            foreach (var candidate in SpecialArtifactRules.DirectedCandidates(input, supportSeed, cancellationToken))
            {
                if (evaluations >= supportLimit) break;
                changed |= Try(candidate);
            }
            changed |= Try(Match(best));
            foreach (var tablet in orderedTablets)
            {
                var seed = best;
                foreach (var option in tablet.Options.OrderBy(x => seed.Positions[tablet.Id].ManhattanDistance(x.Position))
                    .ThenBy(x => (x.Rotation - seed.Rotations[tablet.Id] + 4) % 4).ThenBy(x => x.Position.Y).ThenBy(x => x.Position.X))
                {
                    if (evaluations >= limit) break;
                    var moved = Relocate(input, seed, tablet.Id, option.Position, movable);
                    if (moved is null) continue;
                    var rotations = moved.Rotations.ToDictionary(x => x.Key, x => x.Value);
                    rotations[tablet.Id] = option.Rotation;
                    var candidate = new BoardLayout(moved.Positions, rotations);
                    // Remember promising options even when they are neutral
                    // alone: a second tablet can satisfy their conditions.
                    changed |= Try(candidate, option, tablet.Id);
                    // A tablet may create a useful slot that is empty in the
                    // current layout. Score the joint assignment as well, even
                    // when moving/rotating the tablet alone gives no benefit.
                    if (evaluations < limit) changed |= Try(Match(candidate));
                }
            }
            changed |= Try(Match(best));
            // Exact full-board scoring for neighbor and conditional-tablet
            // changes, including occupied destinations/full-board swaps.
            foreach (var artifact in orderedArtifacts)
            {
                var seed = best;
                foreach (var cell in input.Cells.OrderBy(x => x.Position.Y).ThenBy(x => x.Position.X))
                {
                    if (evaluations >= limit) break;
                    changed |= Try(Relocate(input, seed, artifact.Id, cell.Position, movable));
                }
            }
            if (!changed) break;
        }
        // Exact joint rotations at the incumbent positions, ONLY when their
        // complete product fits the reserved budget. Three or more individually
        // neutral rotations can remove stacked disable effects together; the
        // top-four single-tablet beams cannot represent that transition.
        limit = evaluationBudget;
        var rotationSeed = best;
        var rotationDomains = orderedTablets.Select(t => (t.Id, Options: t.Options
            .Where(o => o.Position.Equals(rotationSeed.Positions[t.Id]))
            .GroupBy(o => o.Rotation).Select(g => g.First())
            .OrderBy(o => (o.Rotation - rotationSeed.Rotations[t.Id] + 4) % 4).ToArray()))
            .Where(t => t.Options.Length > 1).ToArray();
        long rotationBound = 1;
        foreach (var domain in rotationDomains)
        {
            if (rotationBound > (limit - evaluations) / 2 / domain.Options.Length) { rotationBound = limit + 1L; break; }
            rotationBound *= domain.Options.Length;
        }
        if (rotationDomains.Length >= 2 && rotationBound <= (limit - evaluations) / 2)
        {
            var rotations = rotationSeed.Rotations.ToDictionary(x => x.Key, x => x.Value);
            void VisitRotations(int depth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (depth == rotationDomains.Length)
                {
                    var candidate = new BoardLayout(rotationSeed.Positions, rotations);
                    Try(candidate);
                    if (evaluations < limit) Try(Match(candidate));
                    return;
                }
                var domain = rotationDomains[depth];
                foreach (var option in domain.Options)
                { rotations[domain.Id] = option.Rotation; VisitRotations(depth + 1); }
            }
            VisitRotations(0);
        }
        // Remaining budget: bounded two-tablet POSITION lookahead. Neither this
        // nor the joint-rotation phase claims a global optimum on large boards.
        for (var i = 0; i < orderedTablets.Length && evaluations < limit; i++)
        for (var j = i + 1; j < orderedTablets.Length && evaluations < limit; j++)
        {
            var first = orderedTablets[i]; var second = orderedTablets[j]; var seed = best;
            foreach (var a in beams[first.Id])
            foreach (var b in beams[second.Id])
            {
                if (evaluations >= limit) break;
                var one = Relocate(input, seed, first.Id, a.Option.Position, movable);
                if (one is null) continue;
                var two = Relocate(input, one, second.Id, b.Option.Position, movable);
                if (two is null || !two.Positions[first.Id].Equals(a.Option.Position)) continue;
                var rotations = two.Rotations.ToDictionary(x => x.Key, x => x.Value);
                rotations[first.Id] = a.Option.Rotation; rotations[second.Id] = b.Option.Rotation;
                var candidate = new BoardLayout(two.Positions, rotations);
                if (input.Tablets.Any(t => Option(t, candidate) is null)) continue;
                Try(candidate);
                if (evaluations < limit) Try(Match(candidate));
            }
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
        return SpecialArtifactRules.PreservesSides(input, result) && input.Tablets.All(t => Option(t, result) is not null) ? result : null;
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

    internal static (bool Active, int Level) Value(BoardOptimizationInput input, BoardLayout layout, BoardArtifact artifact, GridPoint position, BoardCell cell)
    {
        var active = cell.Disabled <= 0 && input.GloballyActive && cell.Level >= 0 && artifact.ExternalActive &&
            (cell.Ignore > 0 || Criteria(input, layout, artifact, position));
        return (active, active ? Math.Min(artifact.Maximum, cell.Level) : 0);
    }

    internal static BoardObjective Evaluate(BoardOptimizationInput input, BoardLayout layout)
    {
        var cells = Cells(input, layout).ToDictionary(x => x.Position);
        var special = new SpecialArtifactEvaluation(input, layout, cells);
        long requiredActive = 0, requiredLevel = 0, recommendedActive = 0, recommendedLevel = 0, requiredPriority = 0, recommendedPriority = 0;
        long requiredEffects = 0, recommendedEffects = 0;
        var requiredCore = new long[input.RequiredTiers * 3]; var recommendedCore = new long[input.RecommendedTiers * 3];
        foreach (var binding in input.GoalArtifacts)
        {
            var goal = binding.Goal;
            var values = binding.Artifacts
                .Select(special.Value)
                .OrderByDescending(x => x.Active).ThenByDescending(x => x.Level).ThenByDescending(x => x.Effect).Take(goal.DesiredAcquisitions).ToArray();
            var level = values.Sum(x => (long)x.Level);
            var priority = level * binding.Weight;
            var tier = goal.Role == TargetRole.Required ? requiredCore : recommendedCore;
            tier[binding.Tier] += values.Count(x => x.Active); tier[binding.Tier + 1] += level; tier[binding.Tier + 2] += values.Sum(x => x.Effect);
            if (goal.Role == TargetRole.Required) { requiredActive += values.Count(x => x.Active); requiredLevel += level; requiredPriority += priority; requiredEffects += values.Sum(x => x.Effect); }
            else { recommendedActive += values.Count(x => x.Active); recommendedLevel += level; recommendedPriority += priority; recommendedEffects += values.Sum(x => x.Effect); }
        }
        var comboCounts = input.Combos.Goals.Count > 0 ? special.ComboCounts() : null;
        var combo = comboCounts is null ? 0L : input.Combos.Goals.Sum(k => (long)SpecialArtifactRules.Count(comboCounts, k));
        var movement = input.Items.Sum(x => x.Value.ManhattanDistance(layout.Positions[x.Key]));
        var rotations = input.Tablets.Sum(x => (layout.Rotations[x.Id] - x.Rotation + 4) % 4);
        var negativePenalty = input.Artifacts.Where(a => a.Movable && input.Goals.Any(g => g.CatalogKey == a.Key))
            .Sum(a => NegativePenalty(a, cells[layout.Positions[a.Id]]));
        return new BoardObjective(requiredActive, requiredLevel, recommendedActive, recommendedLevel, movement, rotations, requiredPriority, recommendedPriority, negativePenalty,
            requiredEffects, recommendedEffects, combo,
            requiredCore, recommendedCore);
    }

    private static long NegativePenalty(BoardArtifact artifact, BoardCell cell) => Math.Min(int.MaxValue,
        Math.Max(0L, -(long)cell.Level + artifact.Enchant * (long)(cell.Multiplier == 0 ? 1 : cell.Multiplier)));

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
        BigInteger negativeWeight = movementBound;
        BigInteger suffix = ((BigInteger)artifacts.Length * int.MaxValue + 1) * negativeWeight;
        var weights = new Dictionary<(TargetRole Role, int Priority), (BigInteger Active, BigInteger Level)>();
        // Exact mixed-radix priority tiers: no number of lower-priority levels
        // may outweigh one core level. Coupled special effects are re-evaluated
        // on the whole board; matching remains a proposal, never an authority.
        foreach (var tier in input.GoalArtifacts.Select(b => (b.Goal.Role, b.Goal.Priority)).Distinct()
            .OrderByDescending(g => g.Role == TargetRole.Required).ThenBy(g => g.Priority).Reverse())
        {
            var levelWeight = suffix; suffix *= levelBound;
            var activeWeight = suffix; suffix *= n + 1;
            weights[tier] = (activeWeight, levelWeight);
        }
        var requiredActiveWeight = suffix;
        // Rectangular matching avoids solving dummy rows for every empty slot.
        var costs = new BigInteger[artifacts.Length, n]; var infinity = requiredActiveWeight * (n + 2) * 4;
        var residentEnchants = input.Artifacts.ToDictionary(a => layout.Positions[a.Id], a => a.Enchant);
        for (var i = 0; i < artifacts.Length; i++)
        {
            var artifact = artifacts[i];
            var goal = input.Goals.FirstOrDefault(x => x.CatalogKey == artifact.Key);
            for (var j = 0; j < n; j++)
            {
                var cell = cells[j];
                if (artifact.PlacementEffect.PreserveSide && (artifact.Position.X <= 2) != (cell.Position.X <= 2))
                { costs[i, j] = infinity; continue; }
                // Correct the per-instance enchant: the level map includes the
                // occupant's enchant, which must not be carried with the slot.
                residentEnchants.TryGetValue(cell.Position, out var residentEnchant);
                var multiplier = cell.Multiplier == 0 ? 1 : cell.Multiplier;
                var projected = new BoardCell(cell.Position, checked(cell.Level + (artifact.Enchant - residentEnchant) * multiplier), cell.Disabled, cell.Ignore, cell.Multiplier);
                var value = Value(input, layout, artifact, cell.Position, projected);
                var benefit = BigInteger.Zero;
                if (goal is not null)
                {
                    var weight = weights[(goal.Role, goal.Priority)];
                    benefit = (value.Active ? weight.Active : 0) + value.Level * weight.Level;
                    if (goal.Role == TargetRole.Required && value.Active) benefit += requiredActiveWeight;
                }
                costs[i, j] = artifact.Position.ManhattanDistance(cell.Position) + (goal is null ? 0 : NegativePenalty(artifact, projected) * negativeWeight) - benefit;
            }
        }
        var assignment = RectangularAssignment.Match(costs, infinity, cancellationToken);
        var positions = layout.Positions.ToDictionary(x => x.Key, x => x.Value);
        for (var i = 0; i < artifacts.Length; i++) positions[artifacts[i].Id] = cells[assignment[i]].Position;
        return new BoardLayout(positions, layout.Rotations);
    }

}
