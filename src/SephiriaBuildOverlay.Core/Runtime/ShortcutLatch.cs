namespace SephiriaBuildOverlay.Core.Runtime;

// UI key-down repeats are not fresh confirmations. Only a release arms the next press.
public sealed class ShortcutLatch
{
    private readonly HashSet<int> _held = new();
    public bool Press(int key) => _held.Add(key);
    public void Release(int key) => _held.Remove(key);
}
