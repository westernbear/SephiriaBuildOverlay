namespace SephiriaBuildOverlay.Plugin;

internal static class SlotMoveGuard
{
    public static bool CanMove(GhostItem expected, string? currentInstance, string? currentKey,
        bool destinationEmpty, bool normalMode, bool sourceVisible, bool destinationVisible, bool destinationDisabled) =>
        expected.CanRelocate && expected.InstanceId == currentInstance && expected.Key == currentKey &&
        destinationEmpty && normalMode && sourceVisible && destinationVisible && !destinationDisabled;
}
