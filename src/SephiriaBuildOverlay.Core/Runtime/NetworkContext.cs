namespace SephiriaBuildOverlay.Core.Runtime;

public enum NetworkRole { Offline, Host, Client, Disconnected }

// Captured values only: no Unity objects or mutable transport references leave
// the main thread. Server authority never grants local UI ownership.
public sealed class NetworkContext
{
    public NetworkContext(string sessionId, NetworkRole role, bool connected, string lobbyId = "local")
    {
        SessionId = sessionId;
        Role = role;
        Connected = connected;
        LobbyId = lobbyId;
    }
    public string SessionId { get; }
    public NetworkRole Role { get; }
    public bool Connected { get; }
    public string LobbyId { get; }
    public static NetworkContext Offline { get; } = new("offline", NetworkRole.Offline, true);
}

public readonly struct RunScope : IEquatable<RunScope>
{
    public RunScope(string runId, string playerId, string sessionId)
    { RunId = runId; PlayerId = playerId; SessionId = sessionId; }
    public string RunId { get; }
    public string PlayerId { get; }
    public string SessionId { get; }
    public bool Equals(RunScope other) => RunId == other.RunId && PlayerId == other.PlayerId && SessionId == other.SessionId;
    public override bool Equals(object? obj) => obj is RunScope other && Equals(other);
    public override int GetHashCode() => ((RunId?.GetHashCode() ?? 0) * 397) ^ (PlayerId?.GetHashCode() ?? 0) ^ (SessionId?.GetHashCode() ?? 0);
}

public static class LocalOwnershipPolicy
{
    public static bool Allows(bool localOwned, object? localAvatar, object? screenAvatar) =>
        localOwned && localAvatar is not null && ReferenceEquals(localAvatar, screenAvatar);
}
