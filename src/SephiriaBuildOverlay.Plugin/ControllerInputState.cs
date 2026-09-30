namespace SephiriaBuildOverlay.Plugin;

[Flags]
internal enum PadButtons
{
    None = 0, Modifier = 1, Up = 2, Down = 4, Left = 8, Right = 16, Submit = 32, Cancel = 64
}

internal enum PadCommand { None, Import, Overlay, Confirm, Previous, Next, Submit, Cancel }

// Polling never treats a held control, device switch or focus return as a new
// confirmation. The game's control scheme is authoritative, not device presence.
internal sealed class ControllerInputState
{
    private string? _device;
    private bool _enabled;
    private bool _modal;
    private PadButtons _previous;

    public PadCommand Update(bool gamepadMode, bool focused, string? pairedDevice, PadButtons buttons, bool modal)
    {
        var enabled = gamepadMode && focused && pairedDevice is not null;
        var changed = enabled != _enabled || pairedDevice != _device || modal != _modal;
        var pressed = buttons & ~_previous;
        _previous = buttons; _enabled = enabled; _device = pairedDevice; _modal = modal;
        if (!enabled || changed) return PadCommand.None;
        if ((buttons & PadButtons.Modifier) != 0)
        {
            // Exactly one command even if a diagonal is pressed.
            if ((pressed & PadButtons.Up) != 0) return PadCommand.Import;
            if ((pressed & PadButtons.Left) != 0) return PadCommand.Overlay;
            if ((pressed & PadButtons.Right) != 0) return PadCommand.Confirm;
            return PadCommand.None;
        }
        if (!modal) return PadCommand.None;
        if ((pressed & PadButtons.Cancel) != 0) return PadCommand.Cancel;
        if ((pressed & PadButtons.Submit) != 0) return PadCommand.Submit;
        if ((pressed & (PadButtons.Up | PadButtons.Left)) != 0) return PadCommand.Previous;
        if ((pressed & (PadButtons.Down | PadButtons.Right)) != 0) return PadCommand.Next;
        return PadCommand.None;
    }
}

internal sealed class ControllerMenu
{
    private readonly List<(string Id, Action Activate)> _controls = new();
    private string? _selected;
    public void BeginFrame() => _controls.Clear();
    public void Add(string id, Action activate)
    {
        _controls.Add((id, activate));
        _selected ??= id;
    }
    public void EndFrame()
    {
        if (!_controls.Any(x => x.Id == _selected)) _selected = _controls.FirstOrDefault().Id;
    }
    public bool IsSelected(string id) => _selected == id;
    public void Navigate(int delta)
    {
        if (_controls.Count == 0) return;
        var index = _controls.FindIndex(x => x.Id == _selected);
        _selected = _controls[(Math.Max(0, index) + delta % _controls.Count + _controls.Count) % _controls.Count].Id;
    }
    public void Activate() => _controls.FirstOrDefault(x => x.Id == _selected).Activate?.Invoke();
    public void Reset() { _controls.Clear(); _selected = null; }
    public void Invalidate() => _controls.Clear(); // Page changes retain focus, not stale callbacks.
}
