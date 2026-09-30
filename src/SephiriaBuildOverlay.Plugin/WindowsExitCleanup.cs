using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SephiriaBuildOverlay.Plugin;

// Workaround for the observed Unity 6000.3.21 exit stack:
// CleanupModule_Accessibility -> UiaDisconnectAllProviders -> message pump ->
// PlayerMainWndProc -> NewInput::Activate (input singleton already null).
// Call the documented UIA cleanup BEFORE native input shutdown. This does not
// terminate the process, disable the crash reporter, or modify engine binaries.
internal static class WindowsExitCleanup
{
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data);
    private static readonly SubclassProc Guard = OnMessage;
    private static readonly UIntPtr GuardId = new(0x53424F);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] private static extern uint InSendMessageEx(IntPtr reserved);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("UIAutomationCore.dll")] private static extern int UiaDisconnectAllProviders();

    private static IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, UIntPtr id, UIntPtr data) =>
        message == 0x003D // WM_GETOBJECT: do not re-publish a provider during its disconnect.
            ? DefWindowProc(hwnd, message, wparam, lparam)
            : DefSubclassProc(hwnd, message, wparam, lparam);

    public static void DisconnectProviders(Action<object> info, Action<object> warn)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        IntPtr window = IntPtr.Zero;
        var installed = false;
        try
        {
            if (GetModuleHandle("UIAutomationCore.dll") == IntPtr.Zero) return;
            // Outbound COM is forbidden inside a synchronous SendMessage call.
            if (InSendMessageEx(IntPtr.Zero) != 0) { warn("Early UIA cleanup skipped: synchronous window message."); return; }
            using var process = Process.GetCurrentProcess();
            window = process.MainWindowHandle;
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var owner) == 0 || owner != process.Id)
            { warn("Early UIA cleanup skipped: no owned game window."); return; }
            installed = SetWindowSubclass(window, Guard, GuardId, UIntPtr.Zero);
            if (!installed) { warn("Early UIA cleanup skipped: provider re-entry guard unavailable."); return; }
            var result = UiaDisconnectAllProviders();
            if (result < 0) warn($"Early UIA cleanup failed: HRESULT=0x{result:X8}");
            else info("Early UIA provider cleanup completed before Input System shutdown.");
        }
        catch (Exception ex) { warn("Early UIA cleanup unavailable: " + ex.Message); }
        finally
        {
            // No managed window callback may survive into Mono/engine teardown.
            if (installed) RemoveWindowSubclass(window, Guard, GuardId);
        }
    }
}
