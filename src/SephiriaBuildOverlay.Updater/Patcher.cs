using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;
using SephiriaBuildOverlay.Core.Updates;

namespace SephiriaBuildOverlay.Updater;

public static class Patcher
{
    public static IEnumerable<string> TargetDLLs => Array.Empty<string>();
    public static void Patch(AssemblyDefinition assembly) { }
    public static void Initialize()
    {
        var log = Logger.CreateLogSource("BuildOverlayUpdater");
        UpdateStore? store = null;
        try
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                UpdateProtocol.Files.Contains(a.GetName().Name + ".dll")))
                throw new IOException("Owned DLL already loaded; update deferred.");
            // Don't update files shared by another running game process.
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName(current.ProcessName);
            try { if (processes.Length > 1) throw new IOException("Another game process is running; update deferred."); }
            finally { foreach (var process in processes) process.Dispose(); }
            var configPath = Path.Combine(Paths.ConfigPath, "io.github.sephiria.build-overlay.cfg");
            UpdateProtocol.SafePath(configPath);
            var config = new ConfigFile(configPath, false) { SaveOnConfigSet = false };
            var enabled = config.Bind("Updates", "Enabled", true, "Download stable releases; apply on next game start.").Value;
            store = new UpdateStore(Paths.BepInExRootPath);
            log.LogInfo(store.ApplyPending(enabled));
        }
        catch (Exception ex)
        {
            log.LogWarning("Update deferred/rolled back: " + ex.Message);
            if (store != null && !store.HasUnfinishedTransaction && ex is InvalidDataException)
            {
                try { store.DiscardInvalidPending(); }
                catch (Exception cleanup) { log.LogWarning("Invalid pending download retained: " + cleanup.Message); }
            }
        }
    }
}
