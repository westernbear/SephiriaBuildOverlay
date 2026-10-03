namespace SephiriaBuildOverlay.Plugin;

internal enum ModalCursorSurface { None, System, NativeCanvas }
internal static class ModalCursorPresentation
{
    public static ModalCursorSurface Choose(bool panel, bool advanced, bool gamepad, bool focused, bool spriteAvailable) =>
        !panel || gamepad || !focused ? ModalCursorSurface.None :
        // IMGUI windows have their own front-to-back ordering. An OnGUI texture
        // is not a reliable topmost pointer even with a lower GUI.depth.
        // The OS pointer stays above both window contents and their dimmer.
        advanced || !spriteAvailable ? ModalCursorSurface.System : ModalCursorSurface.NativeCanvas;
}
