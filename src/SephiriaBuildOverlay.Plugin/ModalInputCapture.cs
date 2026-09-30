namespace SephiriaBuildOverlay.Plugin;

// Keep the closing click/key, including its release, out of native UI. This
// state is independent of input mode: switching devices cannot release focus.
internal sealed class ModalInputCapture
{
    public bool Visible { get; private set; }
    public bool Capturing { get; private set; }
    private int _neutralFrame = -1;
    public void SetVisible(bool visible)
    {
        if (Visible == visible) return;
        Visible = visible;
        Capturing = true;
        _neutralFrame = -1;
    }
    public void Tick(int frame, bool focused, bool anyHeld)
    {
        if (!Capturing || Visible) return;
        if (!focused || anyHeld) { _neutralFrame = -1; return; }
        if (_neutralFrame < 0) _neutralFrame = frame;
        if (frame > _neutralFrame + 1) Capturing = false;
    }
}

internal static class ImportShortcutMigration
{
    public static int Migrate(int configured, int oldDefault, int newDefault, bool alreadyMigrated) =>
        !alreadyMigrated && configured == oldDefault ? newDefault : configured;
}
