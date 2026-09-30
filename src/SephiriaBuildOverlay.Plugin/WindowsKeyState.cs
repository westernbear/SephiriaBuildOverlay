using System.Runtime.InteropServices;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

// Only releases a confirmation latch; this API can never initiate a game action.
internal static class WindowsKeyState
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public static bool IsReleased(KeyCode key)
    {
        int code = (int)key;
        int virtualKey;
        if (key >= KeyCode.F1 && key <= KeyCode.F15) virtualKey = 0x70 + code - (int)KeyCode.F1;
        else if (key >= KeyCode.A && key <= KeyCode.Z) virtualKey = 0x41 + code - (int)KeyCode.A;
        else if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) virtualKey = code;
        else if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) virtualKey = 0x60 + code - (int)KeyCode.Keypad0;
        else virtualKey = key switch
        {
            KeyCode.Escape => 27, KeyCode.Space => 32, KeyCode.Tab => 9,
            KeyCode.Return or KeyCode.KeypadEnter => 13, KeyCode.Backspace => 8,
            KeyCode.Delete => 46, KeyCode.Insert => 45, KeyCode.Home => 36, KeyCode.End => 35,
            KeyCode.LeftArrow => 37, KeyCode.UpArrow => 38, KeyCode.RightArrow => 39, KeyCode.DownArrow => 40,
            KeyCode.PageUp => 33, KeyCode.PageDown => 34,
            KeyCode.LeftShift => 160, KeyCode.RightShift => 161,
            KeyCode.LeftControl => 162, KeyCode.RightControl => 163,
            KeyCode.LeftAlt => 164, KeyCode.RightAlt => 165,
            KeyCode.Semicolon => 186, KeyCode.Equals => 187, KeyCode.Comma => 188,
            KeyCode.Minus => 189, KeyCode.Period => 190, KeyCode.Slash => 191, KeyCode.BackQuote => 192,
            KeyCode.LeftBracket => 219, KeyCode.Backslash => 220, KeyCode.RightBracket => 221, KeyCode.Quote => 222,
            _ => 0
        };
        // Unknown mappings require an explicit GUI KeyUp (fail closed).
        return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & 0x8000) == 0;
    }
}
