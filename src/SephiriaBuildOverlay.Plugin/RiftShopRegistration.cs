using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

public sealed partial class SephiriaBuildOverlayPlugin
{
    private void InstallRiftShopPatch()
    {
        try
        {
            var start = AccessTools.TypeByName("PocketDimensionShop")?.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (start is null) throw new MissingMethodException("PocketDimensionShop.Start");
            _harmony!.Patch(start, postfix: new HarmonyMethod(typeof(SephiriaBuildOverlayPlugin), nameof(OnRiftShopStarted)));
            Logger.LogInfo("Rift shop registration installed; passive/manual sapphire guidance.");
        }
        catch (Exception ex) { Logger.LogWarning("Rift shop registration unavailable; scene seed only: " + ex.Message); }
    }

    private static void OnRiftShopStarted(Component __instance)
    {
        var plugin = _instance;
        if (plugin is null || plugin._quitting || plugin._lifetime.Stopped) return;
        plugin._gateway.RegisterRiftShop(__instance);
    }
}
