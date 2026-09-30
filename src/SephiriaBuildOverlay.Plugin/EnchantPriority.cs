using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class EnchantArtifact
{
    public EnchantArtifact(string id, string key, int maximum, int? enchant, int? displayedLevel, bool? active)
    { Id = id; Key = key; Maximum = maximum; Enchant = enchant; DisplayedLevel = displayedLevel; Active = active; }
    public string Id { get; }
    public string Key { get; }
    public int Maximum { get; }
    public int? Enchant { get; }
    public int? DisplayedLevel { get; }
    public bool? Active { get; }
    public bool Eligible => Maximum > 0 && Enchant.HasValue && Enchant.Value >= 0 && Enchant.Value < Maximum;
    public bool ImmediateBenefit => Active == true && DisplayedLevel.HasValue && DisplayedLevel.Value >= 0 && DisplayedLevel.Value < Maximum;
}
internal sealed class EnchantRank
{
    public EnchantRank(EnchantArtifact artifact, ArtifactTarget goal, int rank)
    { Artifact = artifact; Goal = goal; Rank = rank; }
    public EnchantArtifact Artifact { get; }
    public ArtifactTarget Goal { get; }
    public int Rank { get; }
    public string Reason => $"{(Goal.Role == TargetRole.Required ? "필수" : "추천")} · 빌드 우선 {Goal.Priority} · 강화 {Artifact.Enchant}/{Artifact.Maximum}" +
        (Artifact.ImmediateBenefit ? " · 현재 레벨 개선" : " · 현재 효과 개선은 수동 확인");
}
internal static class EnchantPriority
{
    // Guidance only, never a RecommendedAction. MaxLevel caps enchant COUNT,
    // not eligibility based on displayed cell level. Duplicates keep own IDs.
    public static IReadOnlyList<EnchantRank> Rank(IEnumerable<EnchantArtifact> artifacts, IEnumerable<ArtifactTarget> goals)
    {
        var targets = goals.Where(x => x.Role is TargetRole.Required or TargetRole.Recommended)
            .GroupBy(x => x.CatalogKey, StringComparer.Ordinal).ToDictionary(x => x.Key,
                x => x.OrderBy(g => g.Role == TargetRole.Required ? 0 : 1).ThenBy(g => g.Priority).First(), StringComparer.Ordinal);
        return artifacts.Where(a => a.Eligible && targets.ContainsKey(a.Key))
            .Select(a => new { Artifact = a, Goal = targets[a.Key] })
            .OrderBy(x => x.Goal.Role == TargetRole.Required ? 0 : 1).ThenBy(x => x.Goal.Priority)
            .ThenByDescending(x => x.Artifact.ImmediateBenefit).ThenBy(x => x.Artifact.Enchant)
            .ThenBy(x => x.Artifact.Id, StringComparer.Ordinal)
            .Select((x, i) => new EnchantRank(x.Artifact, x.Goal, i + 1)).ToArray();
    }
}
