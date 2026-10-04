namespace SephiriaBuildOverlay.Core.Runtime;

public sealed class NetworkSessionTracker
{
    // Core stays loaded across ScriptEngine plugin reloads. Preserve a live
    // connection's scope there, but never carry it across process restarts.
    public static NetworkSessionTracker Process { get; } = new();
    private string? _context;
    private NetworkContext? _current;

    public NetworkContext Observe(string connectionKey, string playerId, bool owned, NetworkRole role, string lobbyId = "local")
    {
        var context = $"{role}:{connectionKey.Length}:{connectionKey}:{playerId.Length}:{playerId}:{owned}:{lobbyId.Length}:{lobbyId}";
        if (_current is null || context != _context)
        {
            _context = context;
            _current = new NetworkContext(Guid.NewGuid().ToString("N"), role, role != NetworkRole.Disconnected, lobbyId);
        }
        return _current;
    }

    public void Disconnect() { _context = null; _current = null; }
}
