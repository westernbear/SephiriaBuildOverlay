# Bundled third-party components

The BepInEx 5.4.23.5 Windows x64 archive is redistributed unchanged inside this package.
`dependency-manifest.json` records upstream URLs and SHA-256 digests. No game DLLs or assets are included.

| Component | License | Included notice |
| --- | --- | --- |
| BepInEx 5.4.23.5 | MIT | BepInEx-LICENSE.txt |
| HarmonyX 2.9.0 | MIT | HarmonyX-LICENSE.txt |
| BepInEx.Harmony | MIT | BepInEx.Harmony-LICENSE.txt |
| Mono.Cecil 0.10.4 | MIT | Mono.Cecil-LICENSE.txt |
| MonoMod RuntimeDetour / Utils 22.1.29.1 | MIT | MonoMod-LICENSE.txt |
| UnityDoorstop 4.5.0 (`winhttp.dll`) | LGPL-2.1 | UnityDoorstop-LICENSE.txt |

UnityDoorstop is an unmodified, replaceable dynamic library. Its complete corresponding
source, including build instructions, is supplied in `UnityDoorstop-source-v4.5.0.zip`
alongside its full license. Upstream: https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0.
The library can be replaced with a compatible user-built version without rebuilding this mod.
This distribution imposes no additional restriction on modification or debugging of LGPL components.
