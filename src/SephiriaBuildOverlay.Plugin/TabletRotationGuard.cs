using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

// Pure validation shared by recommendation, pre-send validation and tests.
// A rotation is an explicit pointer-targeted request, not an optimizer result.
internal sealed class TabletRotationTarget
{
    public TabletRotationTarget(string instanceId, string key, GridPoint position, int rotation)
    { InstanceId = instanceId; Key = key; Position = position; Rotation = rotation; }
    public string InstanceId { get; }
    public string Key { get; }
    public GridPoint Position { get; }
    public int Rotation { get; }
    public int NextRotation => (Rotation + 1) % 4;
    public string Token => $"rotate:{InstanceId}:{Key}:{Position.X}:{Position.Y}:{Rotation}";
}

internal static class TabletRotationGuard
{
    public static bool CanRequest(TabletRotationTarget expected, string? instanceId, string? key,
        GridPoint position, int? rotation, bool sameInventory, bool normalMode, bool nativeRotatable,
        bool visible, bool pointerOver) => expected.Rotation is >= 0 and <= 3 &&
        expected.InstanceId == instanceId && expected.Key == key && expected.Position.Equals(position) &&
        expected.Rotation == rotation && sameInventory && normalMode && nativeRotatable && visible && pointerOver;

    public static bool IsObserved(TabletRotationTarget expected, string? instanceId, string? key,
        GridPoint position, int? rotation, bool sameInventory) => sameInventory &&
        expected.InstanceId == instanceId && expected.Key == key && expected.Position.Equals(position) &&
        rotation == expected.NextRotation;
}
