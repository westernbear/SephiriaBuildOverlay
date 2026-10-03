namespace SephiriaBuildOverlay.Plugin;

internal static class ShopOfferPolicy
{
    public static int ReplenishmentCost(int tries) => 1 << (Math.Max(0, Math.Min(4, tries)) + 1);
    public static bool CanReplenish(bool nativeButtonActive, int stockCount, int unpurchasedCount) =>
        nativeButtonActive && (stockCount == 0 || unpurchasedCount > 0);
    public static string SapphireCost(int price) => $"사파이어 {price} · 영구 재화 · 수동 확인";
}
