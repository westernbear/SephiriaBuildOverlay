using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    internal void VerifySpecialArtifactCatalog()
    {
        // Read resource PREFABS only. Never instantiate charms, initialize the
        // item database, run effect callbacks or touch a save/avatar/inventory.
        var charmType = GameType("Charm_Basic") ?? throw new InvalidOperationException("Native charm type missing.");
        var supported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in Resources.LoadAll("Item", GameType("ItemEntity")))
        {
            var prefab = ReadNamedObject(entity, "resourcePrefab") as GameObject;
            var charm = prefab == null ? null : prefab.GetComponent(charmType);
            if (charm == null) continue;
            var name = charm.GetType().Name;
            var field = name switch
            {
                "Charm_UpCharmDamage" => "damageBonusByLevel",
                "Charm_ReduceMPCost" => "reducePercentByLevel",
                "Charm_RightSpellCooldownHelper" => "cooldownRecoveryByLevel",
                "Charm_NearLevelDamage" => "allDamageBonusByLevel",
                _ => null
            };
            if (field is not null) NativeAmounts(charm, field);
            if (name == "Charm_UpCharmDamage" && ReadBool(charm, "hasDependencyCondition")) NativeAmounts(charm, "dependencyDamageBonusByLevel");
            if (name == "Charm_3Elemental_ByRow" && NativeStrings(ReadNamedObject(charm, "lineCategory")).Length == 0)
                throw new InvalidOperationException("Native row categories missing.");
            if (name == "Charm_WhitePaper" && ReadNamedNullableInt(charm, "match") is not (1 or 2))
                throw new InvalidOperationException("Native paper match threshold changed.");
            if (field is not null || name is "Charm_3Elemental_ByRow" or "Charm_WhitePaper" or "Charm_PlanetModule" or "Charm_CompanionChaos" or "Charm_FireIce" or "Charm_FireIceWeapon")
            {
                if (!ArtifactPlacementEffect.SupportsCategoryCallback(charm.GetType().GetMethod("OnPreSetEffectRefreshed")?.DeclaringType?.Name) ||
                    !ArtifactPlacementEffect.SupportsCategoryCallback(charm.GetType().GetMethod("GetItemCategory")?.DeclaringType?.Name))
                    throw new InvalidOperationException("Unexpected special category callback: " + name);
                supported.Add(name);
            }
        }
        foreach (var name in new[] { "Charm_UpCharmDamage", "Charm_ReduceMPCost", "Charm_RightSpellCooldownHelper", "Charm_NearLevelDamage",
            "Charm_3Elemental_ByRow", "Charm_WhitePaper", "Charm_PlanetModule", "Charm_CompanionChaos", "Charm_FireIce", "Charm_FireIceWeapon" })
            if (!supported.Contains(name)) throw new InvalidOperationException("Native special artifact prefab missing: " + name);
        _log.LogInfo("Special artifact catalog PASS: " + supported.Count + " verified native behaviors/tables/category callbacks; resource prefabs only, no game actions.");
    }
}
