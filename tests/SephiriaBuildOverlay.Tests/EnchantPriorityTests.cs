using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class EnchantPriorityTests
{
    private static ArtifactTarget Goal(string key, TargetRole role = TargetRole.Required, int priority = 0) => new(key, 3, role, priority);
    private static EnchantArtifact Item(string id, string key = "a", int maximum = 5, int? enchant = 0, int? level = 2, bool? active = true) =>
        new(id, key, maximum, enchant, level, active);

    [Fact]
    public void RoleThenBuildPriorityPrecedeCurrentEnhancementState()
    {
        var ranks = EnchantPriority.Rank(new[] { Item("c", "c"), Item("b", "b"), Item("a", "a", level: 5) },
            new[] { Goal("c", TargetRole.Recommended, -10), Goal("b", priority: 1), Goal("a", priority: 0) });
        Assert.Equal(new[] { "a", "b", "c" }, ranks.Select(x => x.Artifact.Id));
        Assert.Equal(new[] { 1, 2, 3 }, ranks.Select(x => x.Rank));
    }

    [Fact]
    public void DuplicatesRankSeparatelyWithImmediateBenefitThenLowerEnchantThenStableId()
    {
        var artifacts = new[] { Item("z", level: 5), Item("b", enchant: 2), Item("c"), Item("a"), Item("inactive", active: false) };
        var ranks = EnchantPriority.Rank(artifacts, new[] { Goal("a") });
        Assert.Equal(new[] { "a", "c", "b", "inactive", "z" }, ranks.Select(x => x.Artifact.Id));
        Assert.Equal(ranks.Select(x => x.Artifact.Id), EnchantPriority.Rank(artifacts.Reverse(), new[] { Goal("a") }).Select(x => x.Artifact.Id));
    }

    [Fact]
    public void NativeCapIsEnchantCountNotCurrentCellLevel()
    {
        var ranks = EnchantPriority.Rank(new[] { Item("already-max", enchant: 5), Item("zero", maximum: 0),
            Item("cell-max", level: 5), Item("unknown", enchant: null), Item("negative", enchant: -1) }, new[] { Goal("a") });
        Assert.Single(ranks);
        Assert.Equal("cell-max", ranks[0].Artifact.Id);
        Assert.False(ranks[0].Artifact.ImmediateBenefit);
    }

    [Fact]
    public void ExcludedUnclassifiedAndOutsideBuildHaveNoRank()
    {
        var ranks = EnchantPriority.Rank(new[] { Item("a"), Item("b", "b"), Item("c", "c"), Item("outside", "x") },
            new[] { Goal("a"), Goal("b", TargetRole.Excluded), Goal("c", TargetRole.Unclassified) });
        Assert.Single(ranks);
        Assert.Contains("필수", ranks[0].Reason);
        Assert.Contains("현재 레벨 개선", ranks[0].Reason);
    }

    [Fact]
    public void UnknownActivationIsGuidanceNotClaimedBenefit()
    {
        var rank = Assert.Single(EnchantPriority.Rank(new[] { Item("a", active: null, level: null) }, new[] { Goal("a") }));
        Assert.Contains("수동 확인", rank.Reason);
    }
}
