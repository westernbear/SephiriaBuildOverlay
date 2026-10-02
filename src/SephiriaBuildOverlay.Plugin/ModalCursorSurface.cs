namespace SephiriaBuildOverlay.Plugin;

internal enum ModalCursorSurface { None, System, NativeCanvas, Immediate }
internal static class ModalCursorPresentation
{
    public static ModalCursorSurface Choose(bool panel, bool advanced, bool gamepad, bool focused, bool spriteAvailable) =>
        !panel || gamepad || !focused ? ModalCursorSurface.None :
        !spriteAvailable ? ModalCursorSurface.System : advanced ? ModalCursorSurface.Immediate : ModalCursorSurface.NativeCanvas;
}
