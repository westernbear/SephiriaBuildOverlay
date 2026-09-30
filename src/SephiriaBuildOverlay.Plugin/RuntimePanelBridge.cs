using System.Reflection;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    // Version-specific, bounded references: never search siblings/parents' entire
    // object graphs for a target ID, and never interpret an unknown cost as free.
    private void CapturePanelCandidates(GameObject panelObject, ScreenKind screen, GameObject? player, List<ScreenCandidate> candidates)
    {
        var panel = panelObject.GetComponents<Component>().FirstOrDefault(x => x != null && x.GetType().Name == screen switch
        {
            ScreenKind.ArtifactReward => "UI_SephiriteRewardPanel",
            ScreenKind.Shop => "UI_ShopPanel",
            ScreenKind.WeaponUpgrade => "UI_WeaponEnhancementPanel",
            ScreenKind.MiracleChoice => "UI_MiraclePanel",
            _ => string.Empty
        });
        if (panel is null || player is null) return;
        foreach (var element in panel.GetComponentsInChildren<Component>(false).Where(x => x != null))
        {
            if (screen == ScreenKind.ArtifactReward && element.GetType().Name == "UI_SephiriteRewardElement")
            {
                var reward = ReadNamedObject(element, "reward");
                // Unknown IDs must still have a visual label. They cannot become
                // automatic goals without a verified catalog binding.
                var key = reward is null ? null : ReadNamedString(reward, "entityID");
                var method = panel.GetType().GetMethod("HandleSephiriteElementRightClicked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var group = ReadNamedObject(panel, "rewardsGroup") as CanvasGroup;
                if (key is not null && method is not null && group is not null)
                    AddCandidate(element, CandidateKind.Artifact, key, candidates, () => method.Invoke(panel, new object[] { element }), selectable: group.interactable);
            }
            else if (screen == ScreenKind.MiracleChoice && element.GetType().Name == "UI_MiracleElement")
            {
                var entity = ReadNamedObject(element, "entity");
                var key = entity is null ? null : EntityKey(entity, CatalogKind.Miracle);
                var button = ReadNamedObject(element, "button") as Component;
                if (key is not null && button is not null)
                    AddCandidate(element, CandidateKind.Miracle, key, candidates, () => InvokeButton(button), selectable: IsSelectable(button));
            }
            else if (screen == ScreenKind.WeaponUpgrade && IsType(element.GetType(), "UI_WeaponEnhancementButton"))
            {
                var weapon = ReadNamedObject(element, "weapon");
                var key = weapon is null ? null : EntityKey(weapon, CatalogKind.Weapon);
                var method = element.GetType().GetMethod("Click", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var button = element.GetComponents<Component>().FirstOrDefault(x => x != null && IsType(x.GetType(), "UnityEngine.UI.Button"));
                if (key is not null && method is not null && button is not null)
                    AddCandidate(element, CandidateKind.Weapon, key, candidates, () => method.Invoke(element, null), selectable: IsSelectable(button));
            }
            else if (screen == ScreenKind.Shop && element.GetType().Name == "UI_NewInventoryIcon")
                CaptureShopItem(panel, element, candidates);
        }

        if (screen is ScreenKind.MiracleChoice or ScreenKind.WeaponUpgrade or ScreenKind.ArtifactReward)
        {
            var visual = ReadNamedObject(panel, screen == ScreenKind.MiracleChoice ? "rerollButton" : "rerollButtonGroup");
            var rerollObject = visual as GameObject ?? (visual as Component)?.gameObject;
            var method = panel.GetType().GetMethod(screen == ScreenKind.MiracleChoice ? "RequestReroll" : "Reroll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (rerollObject is null || method is null || !rerollObject.activeInHierarchy) return;
            var free = false;
            if (screen == ScreenKind.ArtifactReward)
            {
                var diceImage = ReadNamedObject(panel, "rerollButtonDiceImage") as GameObject;
                if (diceImage is null || ReadNamedObject(panel, "sephirite") is null) return;
                free = !diceImage.activeSelf; // the game's SetRerollCount updates this from remaining free rerolls
            }
            var rerollButton = rerollObject.GetComponentsInChildren<Component>(false).FirstOrDefault(x => x != null && IsType(x.GetType(), "UnityEngine.UI.Button"));
            var group = visual as CanvasGroup;
            var selectable = (group is null || group.interactable) && (rerollButton is null || IsSelectable(rerollButton));
            AddCandidate(rerollObject.transform, CandidateKind.Reroll, null, candidates, () => method.Invoke(panel, null), dice: free ? 0 : 1, free: free, selectable: selectable);
        }
    }

    private void CaptureShopItem(Component panel, Component icon, List<ScreenCandidate> candidates)
    {
        var shopInventory = ReadNamedObject(panel, "Shop");
        var buyerInventory = ReadNamedObject(panel, "Buyer");
        if (shopInventory is null || buyerInventory is null || !IsLocalInventory(buyerInventory) ||
            !ReferenceEquals(ReadNamedObject(icon, "Inventory"), shopInventory)) return;
        var item = ReadNamedObject(icon, "Item");
        var entity = item is null ? null : ReadNamedObject(item, "Entity");
        var key = entity is null ? null : EntityKey(entity, CatalogKind.Artifact);
        var buyer = ReadNamedObject(panel, "BuyerCharacter");
        var shop = ReadNamedObject(panel, "ShopCharacter");
        var x = ReadNamedNullableInt(icon, "X");
        var y = ReadNamedNullableInt(icon, "Y");
        if (key is null || buyer is null || shop is null || !x.HasValue || !y.HasValue) return;
        // The normal purchase route automatically consumes a trade voucher when
        // present. Until that cost can be displayed, leave such purchases manual.
        var voucherCheck = buyerInventory.GetType().GetMethod("TryGetTradeVoucher", BindingFlags.Instance | BindingFlags.Public);
        if (voucherCheck is null || Convert.ToBoolean(voucherCheck.Invoke(buyerInventory, new object?[] { null }))) return;
        // Same pure price function used by UI_ShopPanel. Normal BuyFromShop
        // validates resources on the server.
        var statMethod = buyer.GetType().GetMethod("GetCustomStat", BindingFlags.Instance | BindingFlags.Public);
        if (statMethod is null) return;
        var stat = Enum.Parse(statMethod.GetParameters()[0].ParameterType, "Negotiation");
        var buyerNegotiation = statMethod.Invoke(buyer, new[] { stat });
        var shopNegotiation = statMethod.Invoke(shop, new[] { stat });
        var priceMethod = HarmonyLib.AccessTools.TypeByName("ItemDatabase")?.GetMethod("GetItemBuyPrice", BindingFlags.Static | BindingFlags.Public);
        var request = buyer.GetType().GetMethod("BuyFromShop", BindingFlags.Instance | BindingFlags.Public);
        if (priceMethod is null || request is null) return;
        var price = Convert.ToInt32(priceMethod.Invoke(null, new[] { entity, shopNegotiation, buyerNegotiation }));
        if (price < 0 || !ReadBool(panel, "canSell")) return;
        AddCandidate(icon, CandidateKind.Artifact, key, candidates,
            () => request.Invoke(buyer, new object[] { shop, shopInventory, checked((sbyte)x.Value), checked((sbyte)y.Value), (sbyte)-1, (sbyte)-1 }), money: price);
    }

    private void AddCandidate(Component visual, CandidateKind kind, string? key, List<ScreenCandidate> candidates, Action request,
        int money = 0, int dice = 0, bool free = false, bool selectable = true)
    {
        var token = visual.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (candidates.Any(x => x.Token == token)) return;
        candidates.Add(new ScreenCandidate(token, kind, key, money, dice, free, selectable && visual.gameObject.activeInHierarchy));
        _actions[token] = request;
        if (visual.transform is RectTransform rect) _rectangles[token] = rect;
    }

    private string? EntityKey(object entity, CatalogKind? kind, params string[] names)
    {
        var key = ReadNamedString(entity, names.Length == 0 ? new[] { "id" } : names);
        return key is not null && (kind.HasValue ? _entityKeys[kind.Value].Contains(key) : _entityKeys.Values.Any(x => x.Contains(key))) ? key : null;
    }

    private static bool IsType(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.Name == name || current.FullName == name) return true;
        return false;
    }

    private static bool IsSelectable(Component button) => ReadBool(button, "interactable") &&
        button.GetComponentsInParent<CanvasGroup>(true).All(group => group.interactable);
}
