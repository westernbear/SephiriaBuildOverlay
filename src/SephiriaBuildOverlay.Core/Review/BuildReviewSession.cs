using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Review;

public sealed class ReviewItem
{
    internal ReviewItem(ImportedItem source, int sourceOrder)
    {
        Source = source;
        SourceOrder = sourceOrder;
        DesiredAcquisitions = 1;
    }

    public ImportedItem Source { get; }
    public int SourceOrder { get; }
    public TargetRole? RoleOverride { get; set; }
    public int? PriorityOverride { get; set; }
    public int DesiredAcquisitions { get; set; }
    public string? ManualCatalogKey { get; set; }
}

public sealed class ReviewSection
{
    internal ReviewSection(ImportedSection source, IReadOnlyList<ReviewItem> items)
    {
        Source = source;
        Items = items;
    }

    public ImportedSection Source { get; }
    public TargetRole Role { get; set; } = TargetRole.Unclassified;
    public int Priority { get; set; }
    public IReadOnlyList<ReviewItem> Items { get; }
}

public sealed class ActivationCheck
{
    public ActivationCheck(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null)
    { Errors = errors; Warnings = warnings ?? Array.Empty<string>(); }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool CanActivate => Errors.Count == 0;
}

public sealed class BuildReviewSession
{
    private readonly VersionedCatalog _catalog;
    private readonly Dictionary<string, CatalogBinding> _bindings = new(StringComparer.Ordinal);

    public BuildReviewSession(ImportedBuild build, VersionedCatalog catalog)
    {
        Build = build;
        _catalog = catalog;
        var order = 0;
        Sections = build.Sections.Select(section => new ReviewSection(
            section,
            section.Items.Select(item => new ReviewItem(item, order++)).ToArray())).ToArray();
    }

    public ImportedBuild Build { get; }
    public IReadOnlyList<ReviewSection> Sections { get; }
    public IReadOnlyDictionary<string, CatalogBinding> Bindings => _bindings;

    // Quick imports opt in explicitly; restoring advanced review checkpoints
    // must not silently overwrite the user's classifications or quantities.
    public void RecommendAll()
    {
        foreach (var section in Sections)
        {
            section.Role = TargetRole.Recommended;
            foreach (var item in section.Items) item.RoleOverride = null;
        }
    }

    public void VerifyBindings(IEnumerable<GameEntityDescriptor> gameEntities)
    {
        var snapshot = gameEntities.ToArray();
        _bindings.Clear();
        foreach (var slug in Sections.SelectMany(x => x.Items).Select(x => x.ManualCatalogKey ?? x.Source.Slug).Distinct(StringComparer.Ordinal))
            _bindings[slug] = _catalog.Verify(slug, CatalogKind.Artifact, snapshot);

        if (!string.IsNullOrWhiteSpace(Build.WeaponSlug))
        {
            _bindings[Build.WeaponSlug!] = _catalog.Verify(Build.WeaponSlug!, CatalogKind.Weapon, snapshot);
            if (_catalog.FindBySlug(Build.WeaponSlug!, CatalogKind.Weapon) is not null)
            {
                foreach (var gameKey in _catalog.BuildWeaponPath(Build.WeaponSlug!))
                {
                    var entry = _catalog.Entries.First(x => x.Kind == CatalogKind.Weapon && x.GameKey == gameKey);
                    _bindings[entry.Slug] = _catalog.Verify(entry.Slug, CatalogKind.Weapon, snapshot);
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(Build.MiracleSlug))
            _bindings[Build.MiracleSlug!] = _catalog.Verify(Build.MiracleSlug!, CatalogKind.Miracle, snapshot);
    }

    public ActivationCheck ValidateForActivation(string installedGameVersion)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        foreach (var section in Sections.Where(x => x.Role == TargetRole.Unclassified))
            errors.Add($"구역 '{section.Source.Label}'을 필수/추천/제외 중 하나로 분류하세요.");

        foreach (var section in Sections)
        foreach (var item in section.Items)
        {
            var role = item.RoleOverride ?? section.Role;
            if (role != TargetRole.Required) continue;
            if (item.DesiredAcquisitions < 1)
            {
                errors.Add($"필수 항목 '{item.Source.Slug}'의 필요 횟수는 1 이상이어야 합니다.");
                continue;
            }
            var slug = item.ManualCatalogKey ?? item.Source.Slug;
            if (!_bindings.TryGetValue(slug, out var binding) || binding.Status == BindingStatus.MissingCatalogEntry || binding.Status == BindingStatus.MissingGameEntity)
                errors.Add($"필수 항목 '{item.Source.Slug}'을 게임 엔티티로 해석할 수 없습니다.");
        }

        if (!string.Equals(Build.GameVersion, installedGameVersion, StringComparison.Ordinal) ||
            !string.Equals(_catalog.GameVersion, installedGameVersion, StringComparison.Ordinal))
            warnings.Add($"버전이 달라도 빌드를 적용합니다. 빌드 {Build.GameVersion} / 게임 {installedGameVersion} / 카탈로그 {_catalog.GameVersion}");
        return new ActivationCheck(errors, warnings);
    }

    public BuildPlan CreatePlan(string installedGameVersion, bool acceptVersionMismatch = false)
    {
        // Keep the legacy argument for source compatibility. Even false no
        // longer blocks on version metadata; entity/action validation remains.
        var check = ValidateForActivation(installedGameVersion);
        if (!check.CanActivate) throw new InvalidOperationException(string.Join(Environment.NewLine, check.Errors));

        var targets = Sections.SelectMany(section => section.Items.Select(item => new
            {
                Key = item.ManualCatalogKey ?? item.Source.Slug,
                Role = item.RoleOverride ?? section.Role,
                Priority = item.PriorityOverride ?? section.Priority,
                Count = item.DesiredAcquisitions,
                item.SourceOrder
            }))
            .Where(x => x.Role is TargetRole.Required or TargetRole.Recommended)
            .Where(x => x.Role == TargetRole.Required || _catalog.FindBySlug(x.Key, CatalogKind.Artifact) is not null)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new ArtifactTarget(
                ResolveGameKey(group.Key),
                group.Sum(x => x.Count),
                group.Any(x => x.Role == TargetRole.Required) ? TargetRole.Required : TargetRole.Recommended,
                group.Min(x => x.Priority * 10000 + x.SourceOrder)))
            .OrderBy(x => x.Role == TargetRole.Required ? 0 : 1)
            .ThenBy(x => x.Priority)
            .ToArray();

        var weaponPath = string.IsNullOrWhiteSpace(Build.WeaponSlug) || _catalog.FindBySlug(Build.WeaponSlug!, CatalogKind.Weapon) is null
            ? Array.Empty<string>()
            : _catalog.BuildWeaponPath(Build.WeaponSlug!);
        var miracle = string.IsNullOrWhiteSpace(Build.MiracleSlug) || _catalog.FindBySlug(Build.MiracleSlug!, CatalogKind.Miracle) is null
            ? null : ResolveGameKey(Build.MiracleSlug!);
        var checklist = new List<string>();
        if (!string.IsNullOrWhiteSpace(Build.WeaponSlug) && weaponPath.Count == 0)
            checklist.Add($"미해결 무기 목표 (수동 확인): {Build.WeaponSlug}");
        if (!string.IsNullOrWhiteSpace(Build.MiracleSlug) && miracle is null)
            checklist.Add($"미해결 나무 뿌리 목표 (수동 확인): {Build.MiracleSlug}");
        foreach (var section in Sections)
        foreach (var item in section.Items.Where(x => (x.RoleOverride ?? section.Role) == TargetRole.Recommended &&
            _catalog.FindBySlug(x.ManualCatalogKey ?? x.Source.Slug, CatalogKind.Artifact) is null))
            checklist.Add($"미해결 추천 목표 (수동 확인): {item.Source.Slug}");
        if (!string.IsNullOrWhiteSpace(Build.CostumeSlug)) checklist.Add($"의상: {Build.CostumeSlug}");
        checklist.AddRange(Build.Combos.Select(x => $"콤보: {x}"));
        checklist.Add("과일꼬치와 영구 특성 포인트는 수동으로 확인하세요.");

        return new BuildPlan(Build.Id, Build.GameVersion, targets, weaponPath, miracle, Build.Talents, Build.Combos, checklist);
    }

    private string ResolveGameKey(string slug)
    {
        if (_bindings.TryGetValue(slug, out var binding) && binding.GameKey is not null) return binding.GameKey;
        return _catalog.FindBySlug(slug)?.GameKey
            ?? throw new InvalidOperationException($"'{slug}' 매핑을 찾을 수 없습니다.");
    }
}
