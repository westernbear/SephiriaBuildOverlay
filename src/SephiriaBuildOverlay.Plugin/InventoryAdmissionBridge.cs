using System.Collections;
using System.Reflection;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    // Capture only pure native queries on the Unity thread. Never call an add,
    // remove, enchant, purchase or network method to test whether an item fits.
    private InventoryAdmission ReadInventoryAdmission(object? inventory, object? entity, bool allowWisdomMerge)
    {
        if (inventory is null || entity is null || !IsLocalInventory(inventory)) return InventoryAdmission.Unverified;
        try
        {
            var entityType = GameType("ItemEntity"); var itemType = GameType("EItemType");
            if (entityType is null || itemType is null) return InventoryAdmission.Unverified;
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var canAdd = inventory.GetType().GetMethod("CanAddItem", flags, null, new[] { entityType, typeof(int) }, null);
            var empty = inventory.GetType().GetMethod("GetEmptySlotCount", flags, null, new[] { itemType }, null);
            var type = ReadNamedObject(entity, "type");
            if (canAdd is null || empty is null || type is null) return InventoryAdmission.Unverified;
            var nativeResult = Convert.ToInt32(canAdd.Invoke(inventory, new[] { entity, (object)1 }));
            var hasSpace = Convert.ToInt32(empty.Invoke(inventory, new[] { type })) > 0;
            if (!allowWisdomMerge || Convert.ToInt32(type) != 5)
                return InventoryAdmissionPolicy.Evaluate(nativeResult, hasSpace);

            var byRef = typeof(sbyte).MakeByRefType();
            var hasItem = inventory.GetType().GetMethod("HasItem", flags, null, new[] { entityType, byRef, byRef, byRef }, null);
            if (hasItem is null) return InventoryAdmission.Unverified;
            // The reward UI checks connected unique items BEFORE its merge
            // branch. Do not override that native rejection with a space claim.
            var charmType = GameType("Charm_Basic");
            var prefab = ReadNamedObject(entity, "resourcePrefab") as GameObject;
            var prototype = prefab == null || charmType is null ? null : prefab.GetComponent(charmType);
            if (prototype != null && ReadBool(prototype, "isUniqueEffect") && ReadNamedObject(prototype, "connectedUniqueItems") is IEnumerable connected)
                foreach (var linked in connected)
                    if (linked is not null && hasItem.Invoke(inventory, new object?[] { linked, (sbyte)0, (sbyte)0, (sbyte)0 }) is true)
                        return InventoryAdmission.LimitReached;

            var pairCount = ReadNamedNullableInt(inventory, "uniquePairCount");
            if (!pairCount.HasValue) return InventoryAdmission.Unverified;
            if (pairCount <= 0) return InventoryAdmissionPolicy.Evaluate(nativeResult, hasSpace);
            var arguments = new object?[] { entity, (sbyte)0, (sbyte)0, (sbyte)0 };
            var same = hasItem.Invoke(inventory, arguments) is true;
            if (!same) return InventoryAdmissionPolicy.Evaluate(nativeResult, hasSpace);

            var find = inventory.GetType().GetMethod("FindItem", flags, null, new[] { typeof(sbyte), typeof(sbyte) }, null);
            var instance = find?.Invoke(inventory, new[] { arguments[1], arguments[2] });
            var charm = instance is null ? null : ReadNamedObject(instance, "Charm");
            var id = instance is null ? null : ReadNamedNullableInt(instance, "InstanceID");
            var maximum = charm is null ? null : ReadNamedNullableInt(charm, "maxLevel");
            var manager = ReadStatic("DungeonManager", "Instance");
            var enchant = manager?.GetType().GetMethod("GetGlobalItemStatValue", flags, null, new[] { typeof(int), typeof(string) }, null);
            if (manager is null || enchant is null || !id.HasValue || !maximum.HasValue) return InventoryAdmission.Unverified;
            // Native reward UI treats an absent/non-numeric Enchant as zero.
            int.TryParse(enchant.Invoke(manager, new object[] { id.Value, "Enchant" })?.ToString(), out var level);
            return InventoryAdmissionPolicy.Evaluate(nativeResult, hasSpace, true, true, maximum, level);
        }
        catch { return InventoryAdmission.Unverified; }
    }
}
