using SephiriaBuildOverlay.Core.Import;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class StartingPresetWarnings
{
    private readonly List<string> _items = new();
    private readonly List<string> _favorites = new();
    private readonly List<(string Kind, string Key)> _lockedOptions = new();
    private readonly List<string> _favoriteKeys = new();
    private readonly HashSet<(string, string)> _keys = new();
    public IReadOnlyList<string> Items => _items;
    public IReadOnlyList<string> FavoriteItems => _favorites;
    public void Record(string kind, string key, string? name, bool exists, bool? unlocked)
    {
        if (!exists || unlocked is not false || !_keys.Add((kind, key))) return;
        var label = NotificationText.Plain(name);
        if (label.Length == 0) label = key;
        _items.Add(kind + ": " + label);
        _lockedOptions.Add((kind, key));
    }

    public void RecordUnavailableFavorite(string key, string? name, bool? discovered)
    {
        if (discovered is not false || !_keys.Add(("즐겨찾기", key))) return;
        var label = NotificationText.Plain(name);
        _favorites.Add(label.Length == 0 ? key : label);
        _favoriteKeys.Add(key);
    }

    public void RemoveAppliedOptions(NativePreset applied)
    {
        // Report the accepted native settings, not a pre-apply guess. A stale
        // unlock snapshot must never say an option was excluded when it stayed.
        for (var i = _lockedOptions.Count - 1; i >= 0; i--)
        {
            var option = _lockedOptions[i];
            var kept = option.Kind switch
            {
                "무기" => int.TryParse(option.Key, out var weapon) && applied.Weapon == weapon,
                "의상" => applied.Costume == option.Key,
                "스킨" => applied.Skin == option.Key,
                "특성" => ulong.TryParse(option.Key, out var passive) && applied.Passives.Any(x => x.Id == passive && x.Points > 0),
                "시작 아티팩트" => int.TryParse(option.Key, out var artifact) && applied.Pocket.Any(x => x.Entity == artifact),
                _ => false
            };
            if (kept) { _lockedOptions.RemoveAt(i); _items.RemoveAt(i); }
        }
        for (var i = _favoriteKeys.Count - 1; i >= 0; i--)
            if (int.TryParse(_favoriteKeys[i], out var favorite) && applied.Favorites.Contains(favorite))
            { _favoriteKeys.RemoveAt(i); _favorites.RemoveAt(i); }
    }
    public string? Bubble
    {
        get
        {
            if (_items.Count == 0 && _favorites.Count == 0) return null;
            var messages = new List<string>();
            if (_items.Count > 0)
                messages.Add("미해금 옵션이 있어 제외했습니다.\n" + Summarize(_items) + "\n해금된 옵션만 적용합니다.");
            if (_favorites.Count > 0)
                messages.Add("현재 도감에서 선택할 수 없는 즐겨찾기는 적용하지 않았습니다.\n" + Summarize(_favorites) +
                    "\n빌드 목표와 아이템 추천은 유지합니다.");
            return string.Join("\n", messages);
        }
    }

    private static string Summarize(IReadOnlyList<string> items) =>
        string.Join("\n", items.Take(3).Select(x => x.Length > 48 ? x.Substring(0, 47) + "…" : x)) +
        (items.Count > 3 ? $"\n외 {items.Count - 3}개" : "");
}
