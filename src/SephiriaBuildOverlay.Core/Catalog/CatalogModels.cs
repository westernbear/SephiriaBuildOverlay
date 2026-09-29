using Newtonsoft.Json;

namespace SephiriaBuildOverlay.Core.Catalog;

public sealed class CatalogDocument
{
    [JsonProperty("gameVersion")]
    public string GameVersion { get; set; } = string.Empty;

    [JsonProperty("entries")]
    public List<CatalogEntry> Entries { get; set; } = new();
}

public sealed class CatalogEntry
{
    [JsonProperty("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonProperty("gameKey")]
    public string GameKey { get; set; } = string.Empty;

    [JsonProperty("kind")]
    public Models.CatalogKind Kind { get; set; }

    [JsonProperty("koreanName")]
    public string KoreanName { get; set; } = string.Empty;

    [JsonProperty("rarity")]
    public string? Rarity { get; set; }

    [JsonProperty("category")]
    public string? Category { get; set; }

    [JsonProperty("tier")]
    public int? Tier { get; set; }

    [JsonProperty("parentGameKey")]
    public string? ParentGameKey { get; set; }
}

public sealed class GameEntityDescriptor
{
    public GameEntityDescriptor(
        string gameKey,
        Models.CatalogKind kind,
        string koreanName,
        string? rarity = null,
        string? category = null,
        int? tier = null,
        string? parentGameKey = null)
    {
        GameKey = gameKey;
        Kind = kind;
        KoreanName = koreanName;
        Rarity = rarity;
        Category = category;
        Tier = tier;
        ParentGameKey = parentGameKey;
    }

    public string GameKey { get; }
    public Models.CatalogKind Kind { get; }
    public string KoreanName { get; }
    public string? Rarity { get; }
    public string? Category { get; }
    public int? Tier { get; }
    public string? ParentGameKey { get; }
}

public enum BindingStatus
{
    Verified,
    MissingCatalogEntry,
    MissingGameEntity,
    Ambiguous,
    MetadataMismatch
}

public sealed class CatalogBinding
{
    public CatalogBinding(string slug, BindingStatus status, string? gameKey, string explanation)
    {
        Slug = slug;
        Status = status;
        GameKey = gameKey;
        Explanation = explanation;
    }

    public string Slug { get; }
    public BindingStatus Status { get; }
    public string? GameKey { get; }
    public string Explanation { get; }
    public bool AllowsAutomaticAction => Status == BindingStatus.Verified;
}
