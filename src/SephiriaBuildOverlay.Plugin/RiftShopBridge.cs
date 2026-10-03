using System.Collections;
using System.Reflection;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private readonly Dictionary<string, SpriteRenderer> _worldCandidateVisuals = new(StringComparer.Ordinal);

    private bool CaptureRiftShop(IEnumerable registered, GameObject localPlayer, List<ScreenCandidate> candidates)
    {
        var armType = GameType("PocketDimensionShopArm");
        if (armType is null) return false;
        var shops = new List<object>();
        foreach (var interaction in registered.OfType<Component>().Take(64))
        {
            if (interaction == null || !interaction.gameObject.activeInHierarchy) continue;
            var arm = interaction.GetComponent(armType);
            var shop = arm == null ? null : ReadNamedObject(arm, "shop");
            if (shop is not null && !shops.Any(x => ReferenceEquals(x, shop))) shops.Add(shop);
        }
        foreach (var shop in shops.Take(2))
        {
            var owner = ReadNamedObject(shop, "player");
            var avatar = owner is null ? null : ReadNamedObject(owner, "PlayerAvatar") as Component;
            if (avatar == null || !_playerComponents.Contains(avatar) || !IsLocalInventory(ReadNamedObject(avatar, "Inventory")!)) continue;
            if (ReadNamedObject(shop, "arms") is not IEnumerable arms) continue;
            foreach (var arm in arms.OfType<Component>().Take(16))
            {
                if (arm == null || !arm.gameObject.activeInHierarchy || !ReferenceEquals(ReadNamedObject(arm, "shop"), shop)) continue;
                var entity = ReadNamedObject(arm, "item"); var data = ReadNamedObject(arm, "data");
                var costType = ReadNamedObject(arm, "costType");
                var visual = ReadNamedObject(arm, "itemRenderer") as SpriteRenderer;
                var interaction = ReadNamedObject(arm, "interactable") as Behaviour;
                var kind = ShopOfferPolicy.ItemKind(entity is null ? null : ReadNamedNullableInt(entity, "type"));
                var key = ShopItemKey(entity, kind);
                var priceMethod = arm.GetType().GetMethod("GetPrice", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (entity is null || data is null || ReadBool(data, "isPaid") || key is null || costType is null ||
                    kind is null || Convert.ToInt32(costType) is not (0 or 1) || visual == null || visual.sprite == null || !visual.enabled || interaction == null || !interaction.enabled || priceMethod is null) continue;
                var price = Convert.ToInt32(priceMethod.Invoke(arm, new[] { costType, entity }));
                if (price < 0) continue;
                var token = "rift:" + arm.GetInstanceID();
                candidates.Add(new ScreenCandidate(token, kind.Value, key, automaticActionAllowed: false,
                    additionalCostDescription: ShopOfferPolicy.SapphireCost(price),
                    admission: ReadInventoryAdmission(ReadNamedObject(avatar, "Inventory"), entity, allowWisdomMerge: false)));
                _worldCandidateVisuals[token] = visual;
                if (kind == CandidateKind.Tablet) _rewardTabletSpecs.Add((token, entity, 0));
            }
        }
        return candidates.Count > 0;
    }

    private bool TryCandidateRect(string token, out Rect rect, out RectTransform? uiVisual)
    {
        uiVisual = null; rect = default;
        if (_rectangles.TryGetValue(token, out var visual) && visual != null && visual.gameObject.activeInHierarchy)
        { uiVisual = visual; rect = ScreenRect(visual); return true; }
        if (!_worldCandidateVisuals.TryGetValue(token, out var sprite) || sprite == null || !sprite.gameObject.activeInHierarchy || !sprite.enabled || sprite.sprite == null) return false;
        var gameCamera = ReadStatic("GameCamera", "Instance");
        var camera = gameCamera is null ? Camera.main : ReadNamedObject(gameCamera, "Camera") as Camera;
        if (camera == null) return false;
        var bounds = sprite.bounds; var a = camera.WorldToScreenPoint(bounds.min); var b = camera.WorldToScreenPoint(bounds.max);
        if (a.z <= 0 || b.z <= 0) return false;
        rect = new Rect(Math.Min(a.x, b.x) - 4, Screen.height - Math.Max(a.y, b.y) - 4, Math.Abs(b.x - a.x) + 8, Math.Abs(b.y - a.y) + 8);
        return true;
    }
}
