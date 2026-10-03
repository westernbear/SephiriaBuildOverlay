using System.Reflection;
using System.Collections;
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
        var elements = panel.GetComponentsInChildren<Component>(false).Where(x => x != null).AsEnumerable();
        if (screen == ScreenKind.Shop)
        {
            // The panel owns these pools; never depend on merchant-specific
            // hierarchy/scroll content placement or inspect the buyer's pool.
            foreach (var name in new[] { "shopInventoryIconList", "replenishmentIcons" })
                if (ReadNamedObject(panel, name) is IEnumerable pool)
                    elements = elements.Concat(pool.OfType<Component>().Where(x => x != null && x.gameObject.activeInHierarchy).Take(128));
        }
        foreach (var element in elements.Distinct())
        {
            if (screen == ScreenKind.ArtifactReward && element.GetType().Name == "UI_SephiriteRewardElement")
            {
                var reward = ReadNamedObject(element, "reward");
                // Unknown IDs must still have a visual label. They cannot become
                // automatic goals without a verified catalog binding.
                var key = reward is null ? null : ReadNamedString(reward, "entityID");
                var entity = key is not null && int.TryParse(key, out var entityId)
                    ? GameType("ItemDatabase")?.GetMethod("FindItemById", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { entityId }) : null;
                var tablet = entity is not null && ReadNamedNullableInt(entity, "type") == 6;
                var method = panel.GetType().GetMethod("HandleSephiriteElementRightClicked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var group = ReadNamedObject(panel, "rewardsGroup") as CanvasGroup;
                if (key is not null && method is not null && group is not null)
                {
                    var avatar = ReadNamedObject(panel, "openedAvatar");
                    var inventory = avatar is null ? null : ReadNamedObject(avatar, "Inventory");
                    AddCandidate(element, tablet ? CandidateKind.Tablet : CandidateKind.Artifact, key, candidates, () => method.Invoke(panel, new object[] { element }),
                        selectable: group.interactable, admission: ReadInventoryAdmission(inventory, entity, allowWisdomMerge: true));
                    if (tablet && entity is not null && reward is not null) _rewardTabletSpecs.Add((element.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture), entity, ReadNamedNullableInt(reward, "instanceID") ?? 0));
                }
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
                // Native SetWeaponMethod populates enhancementMetadata, NOT
                // the serialized weapon field (which remains null/stale).
                var metadata = ReadNamedObject(element, "enhancementMetadata");
                var weapon = metadata is null ? null : ReadNamedObject(metadata, "enhanced");
                var key = weapon is null ? null : EntityKey(weapon, CatalogKind.Weapon);
                var method = element.GetType().GetMethod("Click", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var button = ReadNamedObject(element, "button") as Component;
                if (key is not null && method is not null && button is not null)
                    AddCandidate(element, CandidateKind.Weapon, key, candidates, () => method.Invoke(element, null), selectable: IsSelectable(button));
            }
            else if (screen == ScreenKind.Shop && element.GetType().Name == "UI_NewInventoryIcon")
                CaptureShopItem(panel, element, candidates);
            else if (screen == ScreenKind.Shop && element.GetType().Name == "UI_ReplenishmentIcon")
                CaptureReplenishmentItem(panel, element, candidates);
        }

        if (screen == ScreenKind.Shop) CaptureShopReplenishment(panel, candidates);
        if (screen == ScreenKind.ArtifactReward) CaptureRewardConversion(panel, candidates);

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

    private void CaptureRewardConversion(Component panel, List<ScreenCandidate> candidates)
    {
        var group = ReadNamedObject(panel, "convertRerollDiceButtonGroup") as CanvasGroup;
        var method = panel.GetType().GetMethod("ConvertRerollDice", BindingFlags.Instance | BindingFlags.Public);
        var avatar = ReadNamedObject(panel, "openedAvatar") as Component;
        if (group == null || !group.gameObject.activeInHierarchy || !group.interactable || method is null ||
            avatar == null || !_playerComponents.Contains(avatar) || ReadNamedObject(panel, "sephirite") is null) return;
        var button = group.GetComponentsInChildren<Component>(false).FirstOrDefault(x => x != null && IsType(x.GetType(), "UnityEngine.UI.Button"));
        AddCandidate(group.transform, CandidateKind.AbandonOrConvert, null, candidates, () => method.Invoke(panel, null),
            selectable: button is null || IsSelectable(button), additionalCost: "주사위 +1 · 게임 확인 창에서 변환");
        _actionOutcomes[group.transform.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture)] = _ =>
        {
            var manager = ReadStatic("UIManager", "Instance");
            var registry = manager is null ? null : ReadNamedObject(manager, "uiElementsByTypename") as IDictionary;
            return registry?["UI_MessageBoxHolder"] is object holder && ReadBool(holder, "HasOpenedBox");
        };
    }

    private void CaptureShopItem(Component panel, Component icon, List<ScreenCandidate> candidates)
    {
        var shopInventory = ReadNamedObject(panel, "Shop");
        var buyerInventory = ReadNamedObject(panel, "Buyer");
        if (shopInventory is null || buyerInventory is null || !IsLocalInventory(buyerInventory) ||
            !ReferenceEquals(ReadNamedObject(icon, "Inventory"), shopInventory)) return;
        var item = ReadNamedObject(icon, "Item");
        var entity = item is null ? null : ReadNamedObject(item, "Entity");
        var kind = ShopOfferPolicy.ItemKind(entity is null ? null : ReadNamedNullableInt(entity, "type"));
        var key = ShopItemKey(entity, kind);
        var buyer = ReadNamedObject(panel, "BuyerCharacter");
        var shop = ReadNamedObject(panel, "ShopCharacter");
        var x = ReadNamedNullableInt(icon, "X");
        var y = ReadNamedNullableInt(icon, "Y");
        if (kind is null || key is null || buyer is null || shop is null || !x.HasValue || !y.HasValue) return;
        // Keep guidance when a voucher is present; do not silently consume it.
        var voucherCheck = buyerInventory.GetType().GetMethod("TryGetTradeVoucher", BindingFlags.Instance | BindingFlags.Public);
        var voucher = voucherCheck is null ? (bool?)null : Convert.ToBoolean(voucherCheck.Invoke(buyerInventory, new object?[] { null }));
        var price = ShopPrice(entity!, buyer, shop);
        var request = buyer.GetType().GetMethod("BuyFromShop", BindingFlags.Instance | BindingFlags.Public);
        if (!price.HasValue || price < 0 || request is null || !ReadBool(panel, "canSell")) return;
        var button = ReadNamedObject(icon, "button") as Component;
        AddCandidate(icon, kind.Value, key, candidates,
            () => request.Invoke(buyer, new object[] { shop, shopInventory, checked((sbyte)x.Value), checked((sbyte)y.Value), (sbyte)-1, (sbyte)-1 }),
            money: voucher == true ? 0 : price.Value, selectable: button is null || IsSelectable(button),
            automatic: voucher == false, additionalCost: voucher == true ? "거래권 1장 · 수동 확인" : voucher is null ? $"돈 {price} · 거래권 상태 확인 필요 · 수동 확인" : null,
            admission: ReadInventoryAdmission(buyerInventory, entity, allowWisdomMerge: true));
        if (kind == CandidateKind.Tablet) _rewardTabletSpecs.Add((icon.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture), entity!,
            ReadNamedNullableInt(item!, "InstanceID") ?? ReadNamedNullableInt(item!, "instanceID") ?? 0));
    }

    private int? ShopPrice(object entity, object buyer, object shop)
    {
        // Native pure pricing function, not a rendered/localized price string.
        // UnitAvatar has BOTH GetCustomStat(string) and GetCustomStat(ECustomStat).
        // Name-only reflection throws AmbiguousMatchException and wipes every
        // merchant candidate in this snapshot. Bind the shared base signature.
        var statType = GameType("ECustomStat");
        var statMethod = ShopPriceBinding.NegotiationMethod(GameType("UnitAvatar"), statType);
        if (statMethod is null) return null;
        var stat = Enum.Parse(statMethod.GetParameters()[0].ParameterType, "Negotiation");
        var buyerNegotiation = statMethod.Invoke(buyer, new[] { stat });
        var shopNegotiation = statMethod.Invoke(shop, new[] { stat });
        var entityType = GameType("ItemEntity");
        var priceMethod = entityType is null ? null : GameType("ItemDatabase")?.GetMethod("GetItemBuyPrice", BindingFlags.Static | BindingFlags.Public,
            null, new[] { entityType, typeof(int), typeof(int) }, null);
        return priceMethod is null ? null : Convert.ToInt32(priceMethod.Invoke(null, new[] { entity, shopNegotiation, buyerNegotiation }));
    }

    private string? ShopItemKey(object? entity, CandidateKind? kind) => entity is null || kind is null ? null :
        kind == CandidateKind.Tablet ? ReadNamedString(entity, "id") : EntityKey(entity, CatalogKind.Artifact);

    private Component? LocalShopNpc(Component panel)
    {
        var buyerInventory = ReadNamedObject(panel, "Buyer");
        if (buyerInventory is null || !IsLocalInventory(buyerInventory) || ReadNamedObject(panel, "ShopCharacter") is not Component shop) return null;
        var type = GameType("UnitAI_NewBasic");
        return type is null ? null : shop.GetComponent(type);
    }

    private void CaptureReplenishmentItem(Component panel, Component icon, List<ScreenCandidate> candidates)
    {
        var npc = LocalShopNpc(panel);
        if (npc == null || !ReferenceEquals(ReadNamedObject(icon, "Shop"), npc) || !ReadBool(panel, "canSell")) return;
        var index = ReadNamedNullableInt(icon, "ReplenishmentIdx");
        var stock = ReadNamedObject(npc, "replenishments") as IList;
        var entity = ReadNamedObject(icon, "Entity");
        var kind = ShopOfferPolicy.ItemKind(entity is null ? null : ReadNamedNullableInt(entity, "type"));
        var key = ShopItemKey(entity, kind);
        if (!index.HasValue || stock is null || index < 0 || index >= stock.Count || stock[index.Value] is not object entry ||
            ReadBool(entry, "purchased") || kind is null || key is null || ReadNamedString(entry, "entityID") != key) return;
        var buyer = ReadNamedObject(panel, "BuyerCharacter"); var shop = ReadNamedObject(panel, "ShopCharacter");
        var button = ReadNamedObject(icon, "button") as Component;
        var price = buyer is null || shop is null ? null : ShopPrice(entity!, buyer, shop);
        if (!price.HasValue || price < 0 || button is null) return;
        // Native new-stock purchases open a second confirmation dialog. Keep
        // this flow manual rather than authorizing that dialog's YES implicitly.
        AddCandidate(icon, kind.Value, key, candidates, null, money: price.Value,
            selectable: IsSelectable(button), automatic: false, additionalCost: $"돈 {price} · 게임 구매 창에서 확인",
            admission: ReadInventoryAdmission(ReadNamedObject(panel, "Buyer"), entity, allowWisdomMerge: true));
        if (kind == CandidateKind.Tablet) _rewardTabletSpecs.Add((icon.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture), entity!,
            ReadNamedNullableInt(entry, "instanceID") ?? 0));
    }

    private void CaptureShopReplenishment(Component panel, List<ScreenCandidate> candidates)
    {
        var npc = LocalShopNpc(panel);
        var visual = ReadNamedObject(panel, "replenishmentButton") as GameObject;
        var stock = npc is null ? null : ReadNamedObject(npc, "replenishments") as IList;
        var tries = npc is null ? null : ReadNamedNullableInt(npc, "replenishmentTryCount");
        if (npc == null || visual == null || stock is null || !tries.HasValue) return;
        // The game's handler refuses replenishment when every stock item was
        // purchased. Availability follows native rules, not shared dice.
        var available = ShopOfferPolicy.CanReplenish(visual.activeInHierarchy, stock.Count,
            stock.Cast<object>().Count(x => !ReadBool(x, "purchased")));
        if (!available || NativePickerBusy()) return;
        var button = visual.GetComponentsInChildren<Component>(false).FirstOrDefault(x => x != null && IsType(x.GetType(), "UnityEngine.UI.Button"));
        AddCandidate(visual.transform, CandidateKind.Reroll, null, candidates, null, selectable: button is null || IsSelectable(button),
            automatic: false, additionalCost: ShopOfferPolicy.SapphireCost(ShopOfferPolicy.ReplenishmentCost(tries.Value)));
    }

    private void AddCandidate(Component visual, CandidateKind kind, string? key, List<ScreenCandidate> candidates, Action? request,
        int money = 0, int dice = 0, bool free = false, bool selectable = true, bool automatic = true, string? additionalCost = null,
        InventoryAdmission admission = InventoryAdmission.Available)
    {
        var token = visual.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (candidates.Any(x => x.Token == token)) return;
        candidates.Add(new ScreenCandidate(token, kind, key, money, dice, free, selectable && visual.gameObject.activeInHierarchy, automatic, additionalCost, admission));
        if (automatic && request is not null && InventoryAdmissionPolicy.BlockReason(admission) is null) _actions[token] = request;
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
