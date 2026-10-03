using System.Collections;
using System.Reflection;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private readonly Dictionary<string, SpriteRenderer> _worldCandidateVisuals = new(StringComparer.Ordinal);
    private readonly List<Component> _riftShops = new();
    private bool _riftShopScanNeeded = true;
    private Component? _worldCandidateTextTemplate;

    internal void RegisterRiftShop(Component shop)
    {
        if (!_disposed && shop != null && !_riftShops.Contains(shop)) _riftShops.Add(shop);
    }

    private bool CaptureRiftShop(List<ScreenCandidate> candidates)
    {
        var shopType = GameType("PocketDimensionShop");
        if (shopType is null) return false;
        if (_riftShopScanNeeded)
        {
            // Seed once per scene for an already-started shop. New/dynamically
            // spawned shops register through Start; no Resources scan per tick.
            _riftShopScanNeeded = false;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var shop in root.GetComponentsInChildren(shopType, true)) RegisterRiftShop(shop);
            }
        }
        _riftShops.RemoveAll(x => x == null);
        foreach (var shop in _riftShops.Where(x => x.gameObject.activeInHierarchy))
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
                if (!TryWorldSpriteRect(visual, out _)) continue;
                var price = Convert.ToInt32(priceMethod.Invoke(arm, new[] { costType, entity }));
                if (price < 0) continue;
                var token = "rift:" + arm.GetInstanceID();
                candidates.Add(new ScreenCandidate(token, kind.Value, key, automaticActionAllowed: false,
                    additionalCostDescription: ShopOfferPolicy.SapphireCost(price),
                    admission: ReadInventoryAdmission(ReadNamedObject(avatar, "Inventory"), entity, allowWisdomMerge: false)));
                _worldCandidateVisuals[token] = visual;
                _worldCandidateTextTemplate ??= ReadNamedObject(arm, "priceText") as Component;
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
        return TryWorldSpriteRect(sprite, out rect);
    }

    private bool TryWorldSpriteRect(SpriteRenderer sprite, out Rect rect)
    {
        rect = default;
        var gameCamera = ReadStatic("GameCamera", "Instance");
        var camera = (gameCamera is null ? null : ReadNamedObject(gameCamera, "Camera") as Camera) ?? Camera.main;
        if (camera == null || (camera.cullingMask & (1 << sprite.gameObject.layer)) == 0) return false;
        var bounds = sprite.bounds; var a = camera.WorldToScreenPoint(bounds.min); var b = camera.WorldToScreenPoint(bounds.max);
        if (a.z <= 0 || b.z <= 0) return false;
        rect = new Rect(Math.Min(a.x, b.x) - 4, Screen.height - Math.Max(a.y, b.y) - 4, Math.Abs(b.x - a.x) + 8, Math.Abs(b.y - a.y) + 8);
        return rect.xMax >= 0 && rect.yMax >= 0 && rect.xMin <= Screen.width && rect.yMin <= Screen.height;
    }
}
