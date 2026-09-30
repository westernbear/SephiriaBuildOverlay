using System.Globalization;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private bool _enchantMode;
    private readonly List<EnchantArtifact> _enchantArtifacts = new();
    private readonly Dictionary<string, EnchantRank> _enchantRanks = new(StringComparer.Ordinal);
    private void CaptureEnchantArtifact(object charm, string id, string key)
    {
        var manager = ReadStatic("DungeonManager", "Instance");
        var method = manager?.GetType().GetMethod("GetGlobalItemStatValue", new[] { typeof(int), typeof(string) });
        int? enchant = null;
        if (manager is not null && method is not null && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var instance))
        {
            var value = method.Invoke(manager, new object[] { instance, "Enchant" })?.ToString();
            // Native eligibility treats missing/empty stats as zero, but an
            // incompatible method is unknown, never a guessed enchant count.
            enchant = int.TryParse(value, out var parsed) ? parsed : 0;
        }
        _enchantArtifacts.Add(new EnchantArtifact(id, key, ReadNamedNullableInt(charm, "maxLevel") ?? 0, enchant,
            ReadNamedNullableInt(charm, "DisplayedLevel"), ReadNamedObject(charm, "IsEffectEnabled") as bool?));
    }
}
