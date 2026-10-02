namespace SephiriaBuildOverlay.Plugin;

internal sealed class StartingPresetWarnings
{
    private readonly List<string> _items = new();
    private readonly HashSet<(string, string)> _keys = new();
    public IReadOnlyList<string> Items => _items;
    public void Record(string kind, string key, string? name, bool exists, bool unlocked)
    {
        if (!exists || unlocked || !_keys.Add((kind, key))) return;
        var label = NotificationText.Plain(name);
        if (label.Length == 0) label = key;
        _items.Add(kind + ": " + label);
    }
    public string? Bubble
    {
        get
        {
            if (_items.Count == 0) return null;
            var shown = _items.Take(3).Select(x => x.Length > 48 ? x.Substring(0, 47) + "…" : x);
            return "미해금 옵션이 있어 제외했습니다.\n" + string.Join("\n", shown) +
                (_items.Count > 3 ? $"\n외 {_items.Count - 3}개" : "") + "\n해금된 옵션만 적용합니다.";
        }
    }
}
