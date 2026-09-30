using System.Collections;
using System.Reflection;

namespace SephiriaBuildOverlay.Plugin;

// No game or InputSystem assembly reference is shipped. Cached metadata reads
// the game's existing PlayerInput and ONLY its paired Gamepad, never current.
internal sealed class ControllerInputBridge
{
    private readonly PropertyInfo? _current;
    private readonly RuntimeMemberCache _members = new();
    public bool GamepadMode { get; private set; }
    public string? DeviceId { get; private set; }
    public string Scheme { get; private set; } = string.Empty;
    public string ModifierLabel { get; private set; } = "View/Back";
    public string SubmitLabel { get; private set; } = "A";
    public string CancelLabel { get; private set; } = "B";
    public PadButtons Buttons { get; private set; }
    public int PairedGamepads { get; private set; }
    public ControllerInputBridge(Func<string, Type?> resolve) =>
        _current = resolve("ControlsChangeHandler")?.GetProperty("Current", BindingFlags.Static | BindingFlags.Public);

    public void Capture(string modifier)
    {
        Buttons = PadButtons.None; DeviceId = null; GamepadMode = false; Scheme = string.Empty; PairedGamepads = 0;
        try
        {
            var handler = _current?.GetValue(null);
            var playerInput = Read(handler, "PlayerInput");
            Scheme = Read(playerInput, "currentControlScheme") as string ?? string.Empty;
            GamepadMode = Scheme == "Gamepad";
            if (Read(playerInput, "devices") is not IEnumerable devices) return;
            object? pad = null;
            foreach (var device in devices)
            {
                if (device is null || !IsGamepad(device.GetType()) || Read(device, "added") is not true) continue;
                PairedGamepads++;
                pad = device;
            }
            if (!GamepadMode || PairedGamepads != 1 || pad is null) return;
            var modifierControl = Read(pad, modifier);
            var dpad = Read(pad, "dpad");
            if (modifierControl is null || dpad is null) return;
            DeviceId = Convert.ToString(Read(pad, "deviceId"), System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(DeviceId)) { DeviceId = null; return; }
            ModifierLabel = Label(modifierControl, modifier == "selectButton" ? "View/Back" : modifier);
            SubmitLabel = Label(Read(pad, "buttonSouth"), "A");
            CancelLabel = Label(Read(pad, "buttonEast"), "B");
            if (Pressed(modifierControl)) Buttons |= PadButtons.Modifier;
            if (Pressed(Read(dpad, "up"))) Buttons |= PadButtons.Up;
            if (Pressed(Read(dpad, "down"))) Buttons |= PadButtons.Down;
            if (Pressed(Read(dpad, "left"))) Buttons |= PadButtons.Left;
            if (Pressed(Read(dpad, "right"))) Buttons |= PadButtons.Right;
            if (Pressed(Read(pad, "buttonSouth"))) Buttons |= PadButtons.Submit;
            if (Pressed(Read(pad, "buttonEast"))) Buttons |= PadButtons.Cancel;
        }
        catch
        {
            // A removed device or unsupported API is not permission to guess.
            DeviceId = null; Buttons = PadButtons.None;
        }
    }

    public string Prompt(string direction) => ModifierLabel + "+" + direction;
    private bool Pressed(object? control) => Read(control, "isPressed") is true;
    private string Label(object? control, string fallback) =>
        Read(control, "shortDisplayName") as string ?? Read(control, "displayName") as string ?? fallback;
    private object? Read(object? value, string name)
    {
        if (value is null) return null;
        return _members.Find(value.GetType(), name) switch
        {
            PropertyInfo property => property.GetValue(value), FieldInfo field => field.GetValue(value), _ => null
        };
    }
    private static bool IsGamepad(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            if (current.FullName == "UnityEngine.InputSystem.Gamepad") return true;
        return false;
    }
}
