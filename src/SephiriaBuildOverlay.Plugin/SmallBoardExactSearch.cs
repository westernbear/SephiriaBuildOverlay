using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal static class SmallBoardExactSearch
{
    // Enumerate only when a conservative all-different bound fits the budget.
    // This covers conditional effects and duplicate quantity caps exactly;
    // large boards never enter an unbounded factorial search.
    public static BoardOptimizationResult? Solve(BoardOptimizationInput input, CancellationToken token, int budget)
    {
        var start = JointBoardPlanner.Current(input);
        var movable = new HashSet<string>(input.Artifacts.Where(x => x.Movable).Select(x => x.Id)
            .Concat(input.Tablets.Where(x => x.Movable).Select(x => x.Id)));
        var pinned = new HashSet<GridPoint>(input.Items.Where(x => !movable.Contains(x.Key)).Select(x => x.Value));
        var variables = movable.OrderBy(x => x, StringComparer.Ordinal).Select(id =>
        {
            var tablet = input.Tablets.FirstOrDefault(x => x.Id == id);
            var domain = tablet is null
                ? input.Cells.Where(x => !pinned.Contains(x.Position)).Select(x => (Position: x.Position, Rotation: (int?)null)).ToArray()
                : tablet.Options.Where(x => !pinned.Contains(x.Position)).Select(x => (Position: x.Position, Rotation: (int?)x.Rotation)).Distinct().ToArray();
            return (Id: id, Domain: domain);
        }).OrderBy(x => x.Domain.Length).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var freePositions = variables.SelectMany(v => v.Domain.Select(d => d.Position)).Distinct().Count();
        long bound = 1;
        for (var depth = 0; depth < variables.Length; depth++)
        {
            var v = variables[depth];
            if (v.Domain.Length == 0 || freePositions <= depth) return null;
            // Every prefix occupies depth DIFFERENT cells, even when a tablet
            // has several rotations at a cell. Both bounds are conservative;
            // taking their minimum admits 7! without mistaking rotations for
            // additional free cells or excluding pinned occupancy.
            var rotationsPerPosition = v.Domain.GroupBy(d => d.Position).Max(g => g.Count());
            var choices = Math.Min(v.Domain.Length, (long)(freePositions - depth) * rotationsPerPosition);
            if (bound > budget / choices) return null;
            bound *= choices;
        }
        if (bound > budget) return null;
        var before = JointBoardPlanner.Evaluate(input, start); var score = before; var best = start; var count = 0;
        var positions = start.Positions.ToDictionary(x => x.Key, x => x.Value);
        var rotations = start.Rotations.ToDictionary(x => x.Key, x => x.Value);
        var occupied = new HashSet<GridPoint>(pinned);
        void Visit(int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth == variables.Length)
            {
                var candidate = new BoardLayout(positions, rotations);
                count++;
                if (!SpecialArtifactRules.Allows(input, candidate)) return;
                var value = JointBoardPlanner.Evaluate(input, candidate);
                if (value.CompareTo(score) > 0) { best = candidate; score = value; }
                return;
            }
            var variable = variables[depth];
            foreach (var option in variable.Domain)
            {
                if (!occupied.Add(option.Position)) continue;
                positions[variable.Id] = option.Position;
                if (option.Rotation.HasValue) rotations[variable.Id] = option.Rotation.Value;
                Visit(depth + 1); occupied.Remove(option.Position);
            }
        }
        Visit(0);
        return new BoardOptimizationResult(best, before, score, count, false, provenOptimal: true);
    }
}
