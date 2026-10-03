using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class SpecialArtifactEvaluation
{
    public SpecialArtifactEvaluation(BoardOptimizationInput input, BoardLayout layout, IReadOnlyDictionary<GridPoint, BoardCell> cells)
    {
        _input = input; _layout = layout; _cells = cells;
        _at = input.Artifacts.ToDictionary(a => layout.Positions[a.Id]);
    }
    private readonly BoardOptimizationInput _input;
    private readonly BoardLayout _layout;
    private readonly IReadOnlyDictionary<GridPoint, BoardCell> _cells;
    private readonly Dictionary<GridPoint, BoardArtifact> _at;
    private readonly Dictionary<string, IReadOnlyList<string>> _categories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
    public bool CategoriesReliable { get; private set; } = true;
    public BoardArtifact? At(GridPoint p) => _at.TryGetValue(p, out var a) ? a : null;
    public (bool Active, int Level) Base(BoardArtifact a) => JointBoardPlanner.Value(_input, _layout, a, _layout.Positions[a.Id], _cells[_layout.Positions[a.Id]]);
    public BoardArtifact? DirectedTarget(BoardArtifact a)
    {
        var effect = a.PlacementEffect;
        if (!effect.Directed) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal) { a.Id };
        var next = At(_layout.Positions[a.Id].Add(effect.Offset));
        if (effect.Kind != ArtifactPlacementKind.Needle) return next?.Magic == true ? next : null;
        // Native category inheritance follows every needle's OWN direction,
        // including disabled intermediate needles. Cycles/holes have no target.
        while (next is not null && seen.Add(next.Id))
        {
            if (next.PlacementEffect.Kind != ArtifactPlacementKind.Needle)
                return next.PlacementEffect.Attackable ? next : null;
            next = At(_layout.Positions[next.Id].Add(next.PlacementEffect.Offset));
        }
        return null;
    }
    public IReadOnlyList<string> Categories(BoardArtifact a)
    {
        if (_categories.TryGetValue(a.Id, out var known)) return known;
        if (!_visiting.Add(a.Id)) { CategoriesReliable = false; return Array.Empty<string>(); }
        var e = a.PlacementEffect; var p = _layout.Positions[a.Id];
        IReadOnlyList<string> result = e.Categories;
        if (e.Kind == ArtifactPlacementKind.RowCategory)
            result = e.RowCategories.Count == 0 ? Array.Empty<string>() : new[] { e.RowCategories[p.Y % e.RowCategories.Count] };
        else if (e.Kind == ArtifactPlacementKind.Needle)
        {
            var target = DirectedTarget(a);
            result = target is null ? Array.Empty<string>() : Categories(target);
        }
        else if (e.Kind == ArtifactPlacementKind.WhitePaper)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var neighbor in new[] { At(p.Add(new GridPoint(-1, 0))), At(p.Add(new GridPoint(1, 0))) })
                if (neighbor is not null) foreach (var category in Categories(neighbor))
                    counts[category] = (counts.TryGetValue(category, out var n) ? n : 0) + 1;
            result = counts.Where(x => x.Value >= e.PaperMatch).Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        _visiting.Remove(a.Id); _categories[a.Id] = result; return result;
    }
    public IReadOnlyDictionary<string, int> ComboCounts()
    {
        var result = _input.Combos.Offsets.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        // Native combos count category membership, not activation/level.
        foreach (var artifact in _input.Artifacts) foreach (var category in Categories(artifact))
            result[category] = (result.TryGetValue(category, out var n) ? n : 0) + 1;
        return result;
    }
    public (bool Active, int Level, long Effect) Value(BoardArtifact a)
    {
        var value = Base(a); var e = a.PlacementEffect;
        if (!value.Active) return (false, 0, 0);
        double effect = 0;
        if (e.Directed)
        {
            var target = DirectedTarget(a);
            if (target is null || !Base(target).Active) return (false, 0, 0);
            effect = e.At(value.Level);
            if (e.Kind == ArtifactPlacementKind.Needle && e.DependencyCondition && target.PlacementEffect.Rarity <= e.MaximumRarity)
                effect += e.At(value.Level, extra: true);
            // The units are native percentage bonuses, NOT estimated DPS.
            if (e.Kind == ArtifactPlacementKind.ManaSupport) effect = Math.Min(100, effect);
        }
        else if (e.Kind == ArtifactPlacementKind.NeighborLevels)
        {
            // Native uses displayed (even disabled/negative) neighbor levels,
            // capped only above by their maximum, then floors the sum.
            float total = 0;
            foreach (var offset in e.NeighborOffsets)
            {
                var other = At(_layout.Positions[a.Id].Add(offset));
                if (other is not null) total += Math.Min(other.Maximum, _cells[_layout.Positions[other.Id]].Level) * (float)e.At(value.Level);
            }
            effect = Math.Floor(total);
        }
        else if (e.Kind == ArtifactPlacementKind.PlanetSupport)
            effect = Neighbors(a).Count(b => b.PlacementEffect.SummonPlanet && b.PlacementEffect.Categories.Contains("PLANET") && Base(b).Active);
        else if (e.Kind == ArtifactPlacementKind.RowCompanions)
            effect = _input.Artifacts.Count(b => b.PlacementEffect.Companion && _layout.Positions[b.Id].Y == _layout.Positions[a.Id].Y && Base(b).Active);
        return (true, value.Level, (long)Math.Round(effect * 1000, MidpointRounding.ToEven));
    }
    private IEnumerable<BoardArtifact> Neighbors(BoardArtifact a)
    {
        var p = _layout.Positions[a.Id];
        return _input.Artifacts.Where(b => b.Id != a.Id && Math.Abs(_layout.Positions[b.Id].X - p.X) <= 1 && Math.Abs(_layout.Positions[b.Id].Y - p.Y) <= 1);
    }
}

internal static class SpecialArtifactRules
{
    public static bool PreservesSides(BoardOptimizationInput input, BoardLayout layout) => input.Artifacts.All(a =>
        !a.PlacementEffect.PreserveSide || (a.Position.X <= 2) == (layout.Positions[a.Id].X <= 2));
    public static bool PreservesProtectedCombos(BoardOptimizationInput input, BoardLayout layout)
    {
        if (input.Combos.ProtectedCategories.Count == 0 && !input.Artifacts.Any(a => a.PlacementEffect.DynamicCategory)) return true;
        var start = JointBoardPlanner.Current(input);
        var before = new SpecialArtifactEvaluation(input, start, JointBoardPlanner.Cells(input, start).ToDictionary(x => x.Position));
        var after = new SpecialArtifactEvaluation(input, layout, JointBoardPlanner.Cells(input, layout).ToDictionary(x => x.Position));
        var b = before.ComboCounts(); var a = after.ComboCounts();
        return RefreshCategoriesReliable(input, before, after) && input.Combos.ProtectedCategories.All(k => Count(b, k) == Count(a, k));
    }
    private static bool RefreshCategoriesReliable(BoardOptimizationInput input, SpecialArtifactEvaluation before, SpecialArtifactEvaluation after)
    {
        if (!after.CategoriesReliable) return false;
        // Native needles run before papers. A needle inheriting a paper whose
        // categories change in this refresh would see the previous category
        // set, not our final-layout prediction. Do not execute such a layout.
        foreach (var needle in input.Artifacts.Where(a => a.PlacementEffect.Kind == ArtifactPlacementKind.Needle))
        {
            var target = after.DirectedTarget(needle);
            if (target?.PlacementEffect.Kind == ArtifactPlacementKind.WhitePaper &&
                !new HashSet<string>(before.Categories(target), StringComparer.Ordinal).SetEquals(after.Categories(target))) return false;
        }
        return after.CategoriesReliable;
    }
    public static int Count(IReadOnlyDictionary<string, int> counts, string key) => counts.TryGetValue(key, out var n) ? n : 0;
    public static bool Allows(BoardOptimizationInput input, BoardLayout layout, SpecialArtifactEvaluation? after = null)
    {
        if (!PreservesSides(input, layout)) return false;
        if (!input.Artifacts.Any(a => a.PlacementEffect.Kind != ArtifactPlacementKind.Ordinary)) return true;
        var start = JointBoardPlanner.Current(input);
        var before = new SpecialArtifactEvaluation(input, start, JointBoardPlanner.Cells(input, start).ToDictionary(x => x.Position));
        after ??= new SpecialArtifactEvaluation(input, layout, JointBoardPlanner.Cells(input, layout).ToDictionary(x => x.Position));
        var b = before.ComboCounts(); var c = after.ComboCounts();
        if (!RefreshCategoriesReliable(input, before, after) || input.Combos.ProtectedCategories.Any(k => Count(b, k) != Count(c, k))) return false;
        foreach (var artifact in input.Artifacts)
        {
            // A working directed support must keep its exact target, not swap
            // to a different attacker simply to harvest a higher helper level.
            if (!artifact.PlacementEffect.Directed || !before.Base(artifact).Active) continue;
            var target = before.DirectedTarget(artifact);
            if (target is null || !before.Base(target).Active) continue;
            if (after.DirectedTarget(artifact)?.Id != target.Id || !after.Base(artifact).Active || !after.Base(target).Active) return false;
        }
        foreach (var support in input.Artifacts.Where(a => a.PlacementEffect.Kind is ArtifactPlacementKind.PlanetSupport or ArtifactPlacementKind.RowCompanions))
        {
            if (!before.Base(support).Active) continue;
            var origin = start.Positions[support.Id]; var destination = layout.Positions[support.Id];
            foreach (var target in input.Artifacts)
            {
                var p = start.Positions[target.Id]; var q = layout.Positions[target.Id];
                var planet = support.PlacementEffect.Kind == ArtifactPlacementKind.PlanetSupport;
                var eligible = planet ? target.PlacementEffect.SummonPlanet && target.PlacementEffect.Categories.Contains("PLANET") : target.PlacementEffect.Companion;
                var connected = planet ? target.Id != support.Id && Math.Abs(origin.X - p.X) <= 1 && Math.Abs(origin.Y - p.Y) <= 1 : origin.Y == p.Y;
                if (!eligible || !connected || !before.Base(target).Active) continue;
                var remains = planet ? Math.Abs(destination.X - q.X) <= 1 && Math.Abs(destination.Y - q.Y) <= 1 : destination.Y == q.Y;
                if (!remains || !after.Base(support).Active || !after.Base(target).Active) return false;
            }
        }
        return true;
    }

    // Translate an entire directed component in one candidate, then displace
    // outside occupants into the vacated cells. This can escape the local
    // optimum where moving either half of a working pair alone is forbidden.
    public static BoardLayout? RelocateGroup(BoardOptimizationInput input, BoardLayout layout, IReadOnlyDictionary<string, GridPoint> destinations)
    {
        var movable = new HashSet<string>(input.Artifacts.Where(a => a.Movable).Select(a => a.Id).Concat(input.Tablets.Where(t => t.Movable).Select(t => t.Id)));
        if (destinations.Keys.Any(id => !movable.Contains(id)) || destinations.Values.Distinct().Count() != destinations.Count ||
            destinations.Values.Any(p => !input.Cells.Any(c => c.Position.Equals(p)))) return null;
        var vacated = destinations.Keys.Select(id => layout.Positions[id]).Except(destinations.Values).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
        var displaced = layout.Positions.Where(x => input.Items.ContainsKey(x.Key) && !destinations.ContainsKey(x.Key) && destinations.Values.Contains(x.Value))
            .OrderBy(x => x.Value.Y).ThenBy(x => x.Value.X).ToArray();
        if (displaced.Length > vacated.Length || displaced.Any(x => !movable.Contains(x.Key))) return null;
        var positions = layout.Positions.ToDictionary(x => x.Key, x => x.Value);
        foreach (var d in destinations) positions[d.Key] = d.Value;
        for (var i = 0; i < displaced.Length; i++) positions[displaced[i].Key] = vacated[i];
        var result = new BoardLayout(positions, layout.Rotations);
        return PreservesSides(input, result) && input.Tablets.All(t => t.Options.Any(o => o.Position.Equals(result.Positions[t.Id]) && o.Rotation == result.Rotations[t.Id])) ? result : null;
    }
    public static IEnumerable<BoardLayout> DirectedCandidates(BoardOptimizationInput input, BoardLayout layout, CancellationToken token)
    {
        if (!input.Artifacts.Any(a => a.Movable && a.PlacementEffect.Directed)) yield break;
        var state = new SpecialArtifactEvaluation(input, layout, JointBoardPlanner.Cells(input, layout).ToDictionary(x => x.Position));
        var edges = input.Artifacts.Where(a => a.PlacementEffect.Directed).Select(a =>
            (Source: a, Target: state.At(layout.Positions[a.Id].Add(a.PlacementEffect.Offset))))
            .Where(x => x.Target is not null && (x.Source.PlacementEffect.Kind == ArtifactPlacementKind.Needle
                ? x.Target.PlacementEffect.Kind == ArtifactPlacementKind.Needle || x.Target.PlacementEffect.Attackable : x.Target.Magic)).ToArray();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in input.Artifacts.Where(a => a.Movable && a.PlacementEffect.Directed).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var group = new HashSet<string>(StringComparer.Ordinal) { source.Id };
            bool expanded;
            do { expanded = false; foreach (var edge in edges) if (group.Contains(edge.Source.Id) || group.Contains(edge.Target!.Id))
                { expanded |= group.Add(edge.Source.Id); expanded |= group.Add(edge.Target!.Id); } } while (expanded);
            if (group.Count > 1 && !group.Any(id => visited.Contains(id)))
            {
                foreach (var id in group) visited.Add(id);
                foreach (var cell in input.Cells)
                {
                    token.ThrowIfCancellationRequested();
                    var delta = new GridPoint(cell.Position.X - layout.Positions[source.Id].X, cell.Position.Y - layout.Positions[source.Id].Y);
                    var candidate = RelocateGroup(input, layout, group.ToDictionary(id => id, id => layout.Positions[id].Add(delta)));
                    if (candidate is not null) yield return candidate;
                }
            }
            // Also connect an orphan helper to an eligible target. Exact board
            // scoring rejects sacrifices to working connections or build goals.
            if (state.DirectedTarget(source) is not null) continue;
            foreach (var target in input.Artifacts.Where(a => a.Movable && a.Id != source.Id &&
                (source.PlacementEffect.Kind == ArtifactPlacementKind.Needle ? a.PlacementEffect.Attackable : a.Magic)))
            foreach (var cell in input.Cells)
            {
                token.ThrowIfCancellationRequested();
                var candidate = RelocateGroup(input, layout, new Dictionary<string, GridPoint>
                    { [source.Id] = cell.Position, [target.Id] = cell.Position.Add(source.PlacementEffect.Offset) });
                if (candidate is not null) yield return candidate;
            }
        }
    }
}
