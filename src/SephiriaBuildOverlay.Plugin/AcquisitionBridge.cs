using System.Collections;
using SephiriaBuildOverlay.Core.Runtime;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    internal RunSnapshot? ProgressSnapshot()
    {
        var id = FindLocalPlayerId(out var owned, out _);
        RefreshNetworkContext(id, owned);
        return owned && _network.Connected && _lastSnapshot?.RunId == FindRunId(id) ? _lastSnapshot : null;
    }

    internal static int MergeEvidenceCount(object inventory) =>
        (ReadNamedObject(inventory, "uniquePairArtifactConvertDataServerside") as IList)?.Count ?? -1;

    internal static bool HasMergeSource(object inventory, int countBefore, int instanceId, int entityId)
    {
        if (countBefore < 0 || ReadNamedObject(inventory, "uniquePairArtifactConvertDataServerside") is not IList entries) return false;
        return entries.Cast<object>().Skip(countBefore).Any(x => ReadNamedNullableInt(x, "instanceID") == instanceId &&
            ReadNamedNullableInt(x, "entityID") == entityId);
    }

    internal static (string? Instance, string? Entity) ClientAddition(object item) =>
        (ReadNamedString(item, "InstanceID"), ReadNamedString(item, "EntityID"));

    internal static object? ClientMergedItem(object inventory, object position)
    {
        var x = ReadNamedObject(position, "x"); var y = ReadNamedObject(position, "y");
        var method = inventory.GetType().GetMethod("FindItem", new[] { typeof(sbyte), typeof(sbyte) });
        return x is null || y is null ? null : method?.Invoke(inventory, new[] { x, y });
    }
}
