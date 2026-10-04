using System.Runtime.CompilerServices;
using System.Reflection;
using SephiriaBuildOverlay.Core.Runtime;
using UnityEngine;

namespace SephiriaBuildOverlay.Plugin;

internal sealed partial class UnityGameGateway
{
    private NetworkContext _network = NetworkContext.Offline;
    internal event Action? LocalContextChanged;

    private NetworkContext RefreshNetworkContext(string playerId, bool owned)
    {
        var client = Convert.ToBoolean(ReadStatic("Mirror.NetworkClient", "active") ?? false);
        var server = Convert.ToBoolean(ReadStatic("Mirror.NetworkServer", "active") ?? false);
        var connected = Convert.ToBoolean(ReadStatic("Mirror.NetworkClient", "isConnected") ?? false);
        var connection = ReadStatic("Mirror.NetworkClient", "connection");
        var role = client && connected ? (server ? NetworkRole.Host : NetworkRole.Client) :
            client || server ? NetworkRole.Disconnected : NetworkRole.Offline;
        var lobbyId = ReadNativeLobbyId();
        if (lobbyId.EndsWith(":unknown", StringComparison.Ordinal)) role = NetworkRole.Disconnected;
        // Connection object identity changes on reconnect even to the same room
        // and seed. Hosts retain that connection when leaving a Steam lobby, so
        // the native lobby ID must also distinguish rooms with the same avatar.
        var captured = NetworkSessionTracker.Process.Observe((connection is null ? 0 : RuntimeHelpers.GetHashCode(connection)).ToString(), playerId, owned, role, lobbyId);
        if (_network.SessionId != captured.SessionId)
        {
            _network = captured;
            InvalidateLocalContext();
        }
        return _network;
    }

    private string ReadNativeLobbyId()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var eos = GameType("EOSLobbyManager")?.GetField("instance", flags)?.GetValue(null);
        var eosId = eos is null ? null : ReadNamedString(eos, "CurrentLobbyId");
        if (!string.IsNullOrEmpty(eosId)) return "eos:" + eosId;
        var invitation = GameType("SteamInvitation")?.GetField("instance", flags)?.GetValue(null);
        var manager = invitation is null ? null : ReadNamedObject(invitation, "lobbyManager");
        if (manager is null) return "local";
        var lobby = ReadNamedObject(manager, "Lobby");
        // Read cached data only; HasLobby queries Steam membership on every call.
        if (lobby is null || ReadNamedObject(lobby, "id") is not ulong id) return "steam:unknown";
        return id == 0 ? "local" : "steam:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal void NetworkDisconnected()
    {
        NetworkSessionTracker.Process.Disconnect();
        _network = new NetworkContext(Guid.NewGuid().ToString("N"), NetworkRole.Disconnected, false);
        InvalidateLocalContext();
    }

    private void InvalidateLocalContext()
    {
        if (_requestPending && _confirmation is not null) TraceRequest("ContextInvalidated");
        _confirmation?.TrySetResult(false);
        _requestPending = false;
        _actionRequestPending = false;
        _outcomeObserver = null; _pendingActionOutcome = null;
        _lastSnapshot = null; _highlightToken = null;
        _actions.Clear(); _rectangles.Clear(); _actionOutcomes.Clear();
        CancelGhostCalculation(); ClearTabletRewards(); _tabletOptionCache.Clear();
        _boardVisible = false; PlacementBatchEnabled = false;
        LocalContextChanged?.Invoke();
    }

    private bool IsLocalPanel(Component panel)
    {
        if (panel.GetType().Name == "UI_InventoryViewer")
            return ReadNamedObject(panel, "Inventory") is object inventory && IsLocalInventory(inventory);
        FindLocalPlayerId(out var owned, out var player);
        var avatarType = GameType("PlayerAvatar");
        var avatar = player == null || avatarType is null ? null : player.GetComponent(avatarType);
        var member = panel.GetType().Name switch
        {
            "UI_SephiriteRewardPanel" => "openedAvatar",
            "UI_WeaponEnhancementPanel" => "player",
            "UI_MiraclePanel" or "UI_TabletMixPanel" => "playerAvatar",
            "UI_CharacterStatusPanel" => "PlayerAvatar",
            "UI_ShopPanel" => "BuyerCharacter",
            "UI_InventoryViewer" => "PlayerAvatar",
            _ => ""
        };
        return LocalOwnershipPolicy.Allows(owned, avatar, member.Length == 0 ? null : ReadNamedObject(panel, member));
    }
}
