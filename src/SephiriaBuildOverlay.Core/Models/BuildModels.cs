using System.Collections.ObjectModel;

namespace SephiriaBuildOverlay.Core.Models;

public enum TargetRole
{
    Unclassified,
    Required,
    Recommended,
    Excluded
}

[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum CatalogKind
{
    Artifact,
    Weapon,
    Miracle,
    Tablet,
    Combo,
    Costume
}

public sealed class ImportedBuild
{
    public ImportedBuild(
        Guid id,
        string title,
        string gameVersion,
        string? weaponSlug,
        string? miracleSlug,
        IReadOnlyList<ImportedSection> sections,
        IReadOnlyDictionary<string, int> talents,
        IReadOnlyList<string>? combos = null,
        string? costumeSlug = null,
        string? nativePresetCode = null,
        IReadOnlyList<ImportedFruit>? fruitSkewer = null,
        bool hasTalentAllocation = true)
    {
        Id = id;
        Title = title ?? string.Empty;
        GameVersion = gameVersion ?? string.Empty;
        WeaponSlug = weaponSlug;
        MiracleSlug = miracleSlug;
        Sections = sections ?? throw new ArgumentNullException(nameof(sections));
        Talents = talents ?? throw new ArgumentNullException(nameof(talents));
        Combos = combos ?? Array.Empty<string>();
        CostumeSlug = costumeSlug;
        NativePresetCode = nativePresetCode;
        FruitSkewer = fruitSkewer;
        HasTalentAllocation = hasTalentAllocation;
    }

    public Guid Id { get; }
    public string Title { get; }
    public string GameVersion { get; }
    public string? WeaponSlug { get; }
    public string? MiracleSlug { get; }
    public IReadOnlyList<ImportedSection> Sections { get; }
    public IReadOnlyDictionary<string, int> Talents { get; }
    public IReadOnlyList<string> Combos { get; }
    public string? CostumeSlug { get; }
    public string? NativePresetCode { get; }
    // Null means absent (retain current setup); an empty list means clear it.
    public IReadOnlyList<ImportedFruit>? FruitSkewer { get; }
    public bool HasTalentAllocation { get; }
}

public sealed class ImportedFruit
{
    public ImportedFruit(string key, int value) { Key = key; Value = value; }
    public string Key { get; }
    public int Value { get; }
}

public sealed class ImportedSection
{
    public ImportedSection(string id, string label, string description, IReadOnlyList<ImportedItem> items)
    {
        Id = id;
        Label = label ?? string.Empty;
        Description = description ?? string.Empty;
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
    public IReadOnlyList<ImportedItem> Items { get; }
}

public sealed class ImportedItem
{
    public ImportedItem(string instanceId, string slug)
    {
        InstanceId = instanceId ?? string.Empty;
        Slug = !string.IsNullOrWhiteSpace(slug) ? slug : throw new ArgumentException("slug is required", nameof(slug));
    }

    public string InstanceId { get; }
    public string Slug { get; }
}

public sealed class ArtifactTarget
{
    public ArtifactTarget(string catalogKey, int desiredAcquisitions, TargetRole role, int priority)
    {
        if (desiredAcquisitions < 1) throw new ArgumentOutOfRangeException(nameof(desiredAcquisitions));
        CatalogKey = catalogKey ?? throw new ArgumentNullException(nameof(catalogKey));
        DesiredAcquisitions = desiredAcquisitions;
        Role = role;
        Priority = priority;
    }

    public string CatalogKey { get; }
    public int DesiredAcquisitions { get; }
    public TargetRole Role { get; }
    public int Priority { get; }
}

public sealed class BuildPlan
{
    public BuildPlan(
        Guid sourceBuildId,
        string sourceGameVersion,
        IReadOnlyList<ArtifactTarget> artifacts,
        IReadOnlyList<string> weaponPath,
        string? miracleTarget,
        IReadOnlyDictionary<string, int> talentAllocation,
        IReadOnlyList<string>? combos = null,
        IReadOnlyList<string>? checklist = null)
    {
        SourceBuildId = sourceBuildId;
        SourceGameVersion = sourceGameVersion ?? string.Empty;
        Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        WeaponPath = weaponPath ?? throw new ArgumentNullException(nameof(weaponPath));
        MiracleTarget = miracleTarget;
        TalentAllocation = talentAllocation ?? throw new ArgumentNullException(nameof(talentAllocation));
        Combos = combos ?? Array.Empty<string>();
        Checklist = checklist ?? Array.Empty<string>();
    }

    public Guid SourceBuildId { get; }
    public string SourceGameVersion { get; }
    public IReadOnlyList<ArtifactTarget> Artifacts { get; }
    public IReadOnlyList<string> WeaponPath { get; }
    public string? MiracleTarget { get; }
    public IReadOnlyDictionary<string, int> TalentAllocation { get; }
    public IReadOnlyList<string> Combos { get; }
    public IReadOnlyList<string> Checklist { get; }
}

public static class TalentNames
{
    public static readonly IReadOnlyList<string> All = new ReadOnlyCollection<string>(new[]
    {
        "base", "will", "anger", "rapid", "wisdom", "patience", "survival"
    });
}
