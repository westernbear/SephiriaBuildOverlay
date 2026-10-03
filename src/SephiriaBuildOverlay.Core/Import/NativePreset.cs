using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SephiriaBuildOverlay.Core.Import;

// Native clipboard protocol, not SephPlanner's preset system. All data is
// validated before calling the game's parser (which mutates current settings).
public sealed class NativePreset
{
    public const int MaxCodeLength = 65536;
    public const int MaxExpandedBytes = 65536;
    private const string Prefix = "AAF_PRESET_OBFZ|v1";
    public int Weapon { get; private set; }
    public string Costume { get; private set; } = "";
    public string Skin { get; private set; } = "";
    public List<int> Favorites { get; } = new();
    public List<(ulong Id, int Points)> Passives { get; } = new();
    public List<(int Instance, int Entity, int Quantity)> Pocket { get; } = new();
    public int Adaptive { get; private set; }
    public List<(string Category, int Value)> Fruits { get; } = new();

    public static NativePreset Decode(string code)
    {
        if (code.Length > MaxCodeLength || !code.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException("지원하지 않거나 너무 큰 시작 프리셋 코드입니다.");
        var bytes = Convert.FromBase64String(code.Substring(Prefix.Length));
        var key = Encoding.UTF8.GetBytes("ActionAnimalFarmPresetShareKey");
        for (var i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + count > MaxExpandedBytes) throw new FormatException("시작 프리셋 압축 해제 크기 제한을 초과했습니다.");
            output.Write(buffer, 0, count);
        }
        return ParseCompact(new UTF8Encoding(false, true).GetString(output.ToArray()));
    }

    public static NativePreset ParseCompact(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxExpandedBytes) throw new FormatException("프리셋이 너무 큽니다.");
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != "AAP1") throw new FormatException("프리셋 형식이 다릅니다.");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length < 2 || line[1] != ':' || !"WCSFPDBR".Contains(line[0]) || fields.ContainsKey(line.Substring(0, 1)))
                throw new FormatException("알 수 없거나 중복된 프리셋 필드입니다.");
            fields.Add(line.Substring(0, 1), line.Substring(2));
        }
        if (!fields.ContainsKey("W") || !fields.ContainsKey("C") || !fields.ContainsKey("S")) throw new FormatException("필수 프리셋 필드가 없습니다.");
        var preset = new NativePreset { Weapon = Number(fields["W"], 0, int.MaxValue), Costume = Name(fields["C"], false), Skin = Name(fields["S"], true) };
        var favorites = new HashSet<int>();
        foreach (var entry in Entries(fields, "F", ','))
        {
            var id = Number(entry, 0, int.MaxValue);
            if (!favorites.Add(id)) throw new FormatException("중복 즐겨찾기입니다.");
            preset.Favorites.Add(id);
        }
        var passives = new HashSet<ulong>();
        foreach (var entry in Entries(fields, "P", ';'))
        {
            var parts = Parts(entry, 2);
            if (!ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || !passives.Add(id)) throw new FormatException("특성 ID가 잘못되었습니다.");
            preset.Passives.Add((id, Number(parts[1], 0, 100)));
        }
        var instances = new HashSet<int>();
        foreach (var entry in Entries(fields, "D", ';'))
        {
            var parts = Parts(entry, 3);
            var instance = Number(parts[0], -1, int.MaxValue);
            if (instance >= 0 && !instances.Add(instance)) throw new FormatException("중복 주머니 인스턴스입니다.");
            preset.Pocket.Add((instance, Number(parts[1], 0, int.MaxValue), Number(parts[2], 1, 1)));
        }
        preset.Adaptive = fields.TryGetValue("B", out var adaptive) ? Number(adaptive, 0, 1) : 1;
        foreach (var entry in Entries(fields, "R", ';'))
        {
            var parts = Parts(entry, 2);
            var value = Number(parts[1], -1, 1);
            if (value == 0) throw new FormatException("과일 값은 -1 또는 1이어야 합니다.");
            preset.Fruits.Add((Name(parts[0], false), value));
        }
        return preset;
    }

    public void LimitPassives(IReadOnlyDictionary<ulong, int> unlocked, int availablePoints)
    {
        var remaining = Math.Max(0, availablePoints);
        for (var i = 0; i < Passives.Count; i++)
        {
            var pair = Passives[i];
            var points = unlocked.TryGetValue(pair.Id, out var maximum) ? Math.Min(Math.Min(pair.Points, Math.Max(0, maximum)), remaining) : 0;
            Passives[i] = (pair.Id, points); remaining -= points;
        }
        Passives.RemoveAll(x => x.Points == 0);
    }

    public void SetValidatedCostume(string costume, string skin)
    {
        Costume = Name(Uri.EscapeDataString(costume), false);
        Skin = Name(Uri.EscapeDataString(skin), true);
    }

    public void SetWeapon(int weapon) => Weapon = weapon >= 0 ? weapon : throw new ArgumentOutOfRangeException(nameof(weapon));
    public void SetAdaptive(bool adaptive) => Adaptive = adaptive ? 1 : 0;

    public void LimitLoadout(Func<int, int?> pocketCost, int pocketCapacity, ISet<string> categories, int fruitCapacity, int plusLimit, int minusLimit, Func<int, bool>? allowDuplicate = null)
    {
        var space = Math.Max(0, pocketCapacity);
        var chosen = new HashSet<int>();
        Pocket.RemoveAll(x =>
        {
            var cost = pocketCost(x.Entity);
            if (!cost.HasValue || cost.Value < 1 || cost > space || !chosen.Add(x.Entity) && allowDuplicate?.Invoke(x.Entity) != true) return true;
            space -= cost.Value; return false;
        });
        var slots = Math.Max(0, fruitCapacity);
        Adaptive = slots > 0 ? Adaptive : 0;
        slots -= Adaptive;
        // Wiki slugs are lowercase, while native compact presets/catalog IDs
        // use their exact (usually uppercase) spelling. Write the real ID, not
        // merely a case-insensitive membership check: native import is ordinal.
        var categoryIds = categories.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<(string, int), int>();
        var selected = new List<(string Category, int Value)>();
        foreach (var fruit in Fruits)
        {
            if (!categoryIds.TryGetValue(fruit.Category, out var matches)) continue;
            var category = matches.FirstOrDefault(x => string.Equals(x, fruit.Category, StringComparison.Ordinal))
                ?? (matches.Length == 1 ? matches[0] : null);
            if (category is null) continue; // Ambiguous binding is not permission.
            var x = (Category: category, fruit.Value);
            counts.TryGetValue(x, out var used);
            if (slots <= 0 || used >= Math.Max(0, x.Value > 0 ? plusLimit : minusLimit)) continue;
            counts[x] = used + 1; slots--; selected.Add(x);
        }
        Fruits.Clear(); Fruits.AddRange(selected);
    }

    public string Compact(bool includeLoadout = true)
    {
        var text = new StringBuilder("AAP1\n");
        text.Append("W:").Append(Weapon).Append("\nC:").Append(Uri.EscapeDataString(Costume)).Append("\nS:").Append(Uri.EscapeDataString(Skin)).Append('\n');
        text.Append("F:").Append(string.Join(",", Favorites)).Append('\n');
        text.Append("P:").Append(string.Join(";", Passives.Select(x => $"{x.Id},{x.Points}"))).Append('\n');
        text.Append("D:").Append(includeLoadout ? string.Join(";", Pocket.Select(x => $"{x.Instance},{x.Entity},{x.Quantity}")) : "").Append('\n');
        text.Append("B:").Append(includeLoadout ? Adaptive : 0).Append('\n');
        text.Append("R:").Append(includeLoadout ? string.Join(";", Fruits.Select(x => $"{Uri.EscapeDataString(x.Category)},{x.Value}")) : "").Append('\n');
        return text.ToString();
    }

    private static IEnumerable<string> Entries(Dictionary<string, string> fields, string field, char separator)
    {
        if (!fields.TryGetValue(field, out var value) || value.Length == 0) return Array.Empty<string>();
        var entries = value.Split(separator);
        if (entries.Length > 512) throw new FormatException("프리셋 항목 수 제한을 초과했습니다.");
        return entries;
    }
    private static string[] Parts(string value, int count)
    {
        var parts = value.Split(',');
        return parts.Length == count ? parts : throw new FormatException("프리셋 항목 형식이 잘못되었습니다.");
    }
    private static int Number(string text, int min, int max) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
        ? value : throw new FormatException("프리셋 숫자 범위가 잘못되었습니다.");
    private static string Name(string text, bool allowEmpty)
    {
        var value = Uri.UnescapeDataString(text);
        if (value.Length > 256 || (!allowEmpty && value.Length == 0) || value.Any(x => char.IsControl(x) || x == ':' || x == ';' || x == ',')) throw new FormatException("프리셋 이름이 잘못되었습니다.");
        return value;
    }
}
