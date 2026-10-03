using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class BoardOptimizationStep
{
    public BoardOptimizationStep(string id, GridPoint from, GridPoint to, string? swappedId, int? rotation, BoardLayout expected)
    { Id = id; From = from; To = to; SwappedId = swappedId; Rotation = rotation; Expected = expected; }
    public string Id { get; }
    public GridPoint From { get; }
    public GridPoint To { get; }
    public string? SwappedId { get; }
    public int? Rotation { get; }
    public BoardLayout Expected { get; }
    public static BoardOptimizationStep? Next(BoardOptimizationInput input, BoardLayout target, Func<GridPoint, GridPoint, bool>? visible = null)
    {
        var current = JointBoardPlanner.Current(input);
        var movable = new HashSet<string>(input.Tablets.Where(x => x.Movable).Select(x => x.Id).Concat(input.Artifacts.Where(x => x.Movable).Select(x => x.Id)));
        foreach (var id in movable.OrderBy(x => input.Tablets.Any(t => t.Id == x) ? 0 : 1).ThenBy(x => x, StringComparer.Ordinal))
        {
            if (!target.Positions.TryGetValue(id, out var destination) || destination.Equals(current.Positions[id])) continue;
            if (visible is not null && !visible(current.Positions[id], destination)) continue;
            var occupant = input.Items.Keys.FirstOrDefault(x => x != id && current.Positions[x].Equals(destination));
            if (occupant is not null && !movable.Contains(occupant)) continue;
            var positions = current.Positions.ToDictionary(x => x.Key, x => x.Value);
            positions[id] = destination;
            if (occupant is not null) positions[occupant] = current.Positions[id];
            return new BoardOptimizationStep(id, current.Positions[id], destination, occupant, null, new BoardLayout(positions, current.Rotations));
        }
        foreach (var tablet in input.Tablets.Where(x => x.Movable).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (!target.Rotations.TryGetValue(tablet.Id, out var angle) || angle == tablet.Rotation) continue;
            if (visible is not null && !visible(tablet.Position, tablet.Position)) continue;
            // Native rotates forward only: never pretend an inverse turn costs
            // one request. A batch waits for each native forward turn.
            var rotations = current.Rotations.ToDictionary(x => x.Key, x => x.Value);
            rotations[tablet.Id] = (tablet.Rotation + 1) % 4;
            return new BoardOptimizationStep(tablet.Id, tablet.Position, tablet.Position, null, tablet.Rotation, new BoardLayout(current.Positions, rotations));
        }
        return null;
    }
}
