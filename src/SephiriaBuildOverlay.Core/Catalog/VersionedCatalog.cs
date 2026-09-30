using System.Reflection;
using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Catalog;

public sealed class VersionedCatalog
{
    private readonly IReadOnlyList<CatalogEntry> _entries;
    private readonly Dictionary<string, CatalogEntry> _bySlug;
    private readonly Dictionary<string, CatalogEntry> _weaponsByGameKey;

    public VersionedCatalog(string gameVersion, IEnumerable<CatalogEntry> entries)
    {
        GameVersion = gameVersion;
        _entries = entries.ToArray();
        _bySlug = _entries.GroupBy(x => x.Slug, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        _weaponsByGameKey = _entries.Where(x => x.Kind == CatalogKind.Weapon).GroupBy(x => x.GameKey, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    }

    public string GameVersion { get; }
    public IReadOnlyList<CatalogEntry> Entries => _entries;

    public static VersionedCatalog LoadEmbedded(string version = "1.0.33")
    {
        var assembly = typeof(VersionedCatalog).Assembly;
        var suffix = $"catalog-{version}.json";
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(x => x.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"내장 카탈로그 {version}을 찾을 수 없습니다.");
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"내장 카탈로그 {resource}를 열 수 없습니다.");
        using var reader = new StreamReader(stream);
        var document = JsonConvert.DeserializeObject<CatalogDocument>(reader.ReadToEnd())
            ?? throw new JsonException("카탈로그 JSON이 비어 있습니다.");
        return new VersionedCatalog(document.GameVersion, document.Entries);
    }

    public CatalogEntry? FindBySlug(string slug, CatalogKind? kind = null)
    {
        if (!_bySlug.TryGetValue(slug, out var entry)) return null;
        return kind is null || entry.Kind == kind ? entry : null;
    }

    public CatalogBinding Verify(string slug, CatalogKind kind, IEnumerable<GameEntityDescriptor> gameEntities)
    {
        var entry = FindBySlug(slug, kind);
        if (entry is null)
            return new CatalogBinding(slug, BindingStatus.MissingCatalogEntry, null, "이 버전의 카탈로그에 slug가 없습니다.");

        var entities = gameEntities.Where(x => x.Kind == kind).ToArray();
        var candidates = entities.Where(x => string.Equals(x.GameKey, entry.GameKey, StringComparison.Ordinal)).ToArray();
        if (candidates.Length == 0)
            candidates = entities.Where(x => string.Equals(x.KoreanName, entry.KoreanName, StringComparison.Ordinal)).ToArray();
        if (candidates.Length == 0)
            return new CatalogBinding(slug, BindingStatus.MissingGameEntity, entry.GameKey, "일치하는 게임 엔티티가 없습니다.");
        if (candidates.Length > 1)
            return new CatalogBinding(slug, BindingStatus.Ambiguous, null, "게임 엔티티가 둘 이상 일치합니다.");

        var actual = candidates[0];
        var mismatches = new List<string>();
        if (!string.Equals(actual.GameKey, entry.GameKey, StringComparison.Ordinal)) mismatches.Add("키");
        if (!string.Equals(actual.KoreanName, entry.KoreanName, StringComparison.Ordinal)) mismatches.Add("한국어 이름");
        if (!EqualOptional(actual.Rarity, entry.Rarity)) mismatches.Add("희귀도");
        if (entry.IsDual.HasValue && actual.IsDual != entry.IsDual) mismatches.Add("영원 아티팩트 구분");
        if (!EqualOptional(actual.Category, entry.Category)) mismatches.Add("카테고리");
        if (actual.Tier != entry.Tier) mismatches.Add("무기 티어");
        if (!EqualOptional(actual.ParentGameKey, entry.ParentGameKey)) mismatches.Add("부모 무기");

        return mismatches.Count == 0
            ? new CatalogBinding(slug, BindingStatus.Verified, actual.GameKey, "게임 엔티티 메타데이터가 일치합니다.")
            : new CatalogBinding(slug, BindingStatus.MetadataMismatch, actual.GameKey, $"메타데이터 불일치: {string.Join(", ", mismatches)}");
    }

    public IReadOnlyList<string> BuildWeaponPath(string finalWeaponSlug)
    {
        var final = FindBySlug(finalWeaponSlug, CatalogKind.Weapon)
            ?? throw new KeyNotFoundException($"무기 slug '{finalWeaponSlug}'가 카탈로그에 없습니다.");
        var reverse = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = final;
        while (true)
        {
            if (!visited.Add(current.GameKey)) throw new InvalidOperationException("무기 부모 경로에 순환이 있습니다.");
            reverse.Add(current.GameKey);
            if (string.IsNullOrEmpty(current.ParentGameKey)) break;
            if (!_weaponsByGameKey.TryGetValue(current.ParentGameKey, out current!))
                throw new InvalidOperationException($"부모 무기 '{current.ParentGameKey}'를 찾을 수 없습니다.");
        }
        reverse.Reverse();
        return reverse;
    }

    private static bool EqualOptional(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
}
