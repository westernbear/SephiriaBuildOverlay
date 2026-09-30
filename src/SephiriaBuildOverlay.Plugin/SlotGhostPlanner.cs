using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class GhostSlot
{
    public GhostSlot(GridPoint position, int level, bool disabled) { Position = position; Level = level; Disabled = disabled; }
    public GridPoint Position { get; }
    public int Level { get; }
    public bool Disabled { get; }
}
internal sealed class GhostItem
{
    public GhostItem(string instanceId, string key, GridPoint position, int maxLevel, bool canRelocate)
    { InstanceId = instanceId; Key = key; Position = position; MaxLevel = maxLevel; CanRelocate = canRelocate; }
    public string InstanceId { get; }
    public string Key { get; }
    public GridPoint Position { get; }
    public int MaxLevel { get; }
    public bool CanRelocate { get; }
}

// Fixed-tablet preview: use the game's observed cell levels, not an invented
// effect map. Unknown item criteria and conditional tablets stay manual.
// This deliberately does not claim to optimize tablet rotations or combos.
internal sealed class SlotGhostPlanner
{
    public IReadOnlyList<ArtifactAssignment> Solve(IReadOnlyList<GhostItem> items, IReadOnlyList<GhostSlot> slots,
        IReadOnlyList<ArtifactTarget> goals, bool conditionalTablets, CancellationToken cancellationToken = default)
    {
        if (conditionalTablets) return Array.Empty<ArtifactAssignment>();
        var keys = new HashSet<string>(goals.Select(x => x.CatalogKey), StringComparer.Ordinal);
        var movable = items.Where(x => x.CanRelocate && keys.Contains(x.Key)).OrderBy(x => x.InstanceId, StringComparer.Ordinal).ToArray();
        var blocked = new HashSet<GridPoint>(items.Except(movable).Select(x => x.Position));
        var free = slots.Where(x => !x.Disabled && !blocked.Contains(x.Position)).ToList();
        var destinations = new List<ArtifactDestination>();
        foreach (var goal in goals.OrderBy(x => x.Role == TargetRole.Required ? 0 : 1).ThenBy(x => x.Priority).ThenBy(x => x.CatalogKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copies = movable.Where(x => x.Key == goal.CatalogKey).ToArray();
            if (copies.Length == 0) continue;
            var maximum = copies.Min(x => x.MaxLevel);
            var selected = free.OrderByDescending(x => Math.Min(Math.Max(0, x.Level), maximum))
                .ThenBy(x => copies.Min(item => item.Position.ManhattanDistance(x.Position)))
                .ThenBy(x => x.Position.Y).ThenBy(x => x.Position.X).Take(copies.Length).ToArray();
            foreach (var slot in selected)
            {
                free.Remove(slot);
                destinations.Add(new ArtifactDestination(slot.Position, goal.CatalogKey,
                    goal.Role == TargetRole.Required ? 1 : 0, Math.Min(Math.Max(0, slot.Level), maximum), goal.Role == TargetRole.Recommended ? 1 : 0, 0));
            }
        }
        var assignments = new ArtifactAssignmentSolver().Solve(movable.Select(x => new MovableArtifact(x.InstanceId, x.Key, x.Position)).ToArray(), destinations, cancellationToken).Assignments;
        // Never move an item merely for a different tie-break location. A ghost
        // must show a real observed level benefit under the fixed effect map.
        var levels = slots.ToDictionary(x => x.Position, x => x.Disabled ? 0 : Math.Max(0, x.Level));
        return assignments.Where(x =>
        {
            var item = movable.First(i => i.InstanceId == x.InstanceId);
            return levels.TryGetValue(x.From, out var before) && levels.TryGetValue(x.To, out var after) && Math.Min(after, item.MaxLevel) > Math.Min(before, item.MaxLevel);
        }).ToArray();
    }
}
