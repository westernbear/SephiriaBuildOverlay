using System.Reflection;

namespace SephiriaBuildOverlay.Plugin;

internal static class ShopPriceBinding
{
    public static MethodInfo? NegotiationMethod(Type? unitAvatar, Type? stat) =>
        unitAvatar is null || stat is null || !stat.IsEnum ? null :
        unitAvatar.GetMethod("GetCustomStat", BindingFlags.Public | BindingFlags.Instance, null, new[] { stat }, null);
}
