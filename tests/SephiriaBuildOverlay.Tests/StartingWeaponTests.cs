using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class StartingWeaponTests
{
    [Theory]
    [InlineData("raging_helbanus", 400)]
    [InlineData("super_conductor", 0)]
    [InlineData("all_element_staff_ilonica", 500)]
    [InlineData("crossbow", 100)]
    [InlineData("minigun", 100)]
    public void StructuredTargetOverridesOldNativeSelectionWithVerifiedStartingWeapon(string slug, int root)
    {
        var catalog = VersionedCatalog.LoadEmbedded();
        var entities = catalog.Entries.Select(x => new GameEntityDescriptor(x.GameKey, x.Kind, x.KoreanName, x.Rarity, x.Category, x.Tier, x.ParentGameKey, x.IsDual)).ToArray();
        var preset = NativePreset.ParseCompact("AAP1\nW:20\nC:PinkRabbit\nS:\nB:1\nR:STURDY,1\n");
        var warnings = new List<string>();
        StartingPresetFallback.ApplyWeapon(preset, slug, catalog, entities, warnings);
        Assert.Equal(root, preset.Weapon); Assert.Empty(warnings); Assert.Single(preset.Fruits); Assert.Equal(1, preset.Adaptive);
        if (slug != "crossbow") Assert.NotEqual(int.Parse(catalog.FindBySlug(slug, CatalogKind.Weapon)!.GameKey), preset.Weapon);
    }

    [Theory]
    [InlineData("name", false)]
    [InlineData("tier", false)]
    [InlineData("parent", false)]
    [InlineData("missing", false)]
    [InlineData("name", true)]
    [InlineData("tier", true)]
    [InlineData("parent", true)]
    [InlineData("missing", true)]
    public void UnverifiedMinigunTargetStillSelectsVerifiedCrossbowWithOrWithoutNativePreset(string mismatch, bool nativePreset)
    {
        var catalog = VersionedCatalog.LoadEmbedded();
        var target = catalog.FindBySlug("minigun", CatalogKind.Weapon)!;
        var entities = Describe(catalog).Where(x => x.GameKey != target.GameKey).ToList();
        if (mismatch != "missing") entities.Add(new GameEntityDescriptor(target.GameKey, CatalogKind.Weapon,
            mismatch == "name" ? "변경된 이름" : target.KoreanName,
            tier: mismatch == "tier" ? 1 : target.Tier,
            parentGameKey: mismatch == "parent" ? "101" : target.ParentGameKey));
        var current = "AAP1\nW:500\nC:PinkRabbit\nS:\n";
        var warnings = new List<string>();
        NativePreset preset;
        if (nativePreset)
        {
            preset = NativePreset.ParseCompact(current);
            StartingPresetFallback.ApplyWeapon(preset, "minigun", catalog, entities, warnings);
        }
        else
        {
            var id = Guid.NewGuid();
            var build = WikiBuildParser.Parse($"{{\"postUuid\":\"{id}\",\"version\":\"1.0.23\",\"weapon\":\"minigun\",\"content\":[]}}", id);
            preset = StartingPresetFallback.Create(build, current, catalog, entities, _ => null, warnings);
        }
        Assert.Equal(100, preset.Weapon);
        Assert.NotEqual(int.Parse(target.GameKey), preset.Weapon);
        Assert.False(catalog.Verify(target.Slug, CatalogKind.Weapon, entities).AllowsAutomaticAction);
        Assert.Contains(warnings, x => x.Contains("minigun") && x.Contains("목표 강화 무기"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("name")]
    [InlineData("duplicate")]
    public void UnverifiedCrossbowRootKeepsCurrentWeaponEvenWhenUpgradeTargetIsVerified(string mismatch)
    {
        var catalog = VersionedCatalog.LoadEmbedded();
        var root = catalog.FindBySlug("crossbow", CatalogKind.Weapon)!;
        var entities = Describe(catalog).Where(x => x.GameKey != root.GameKey).ToList();
        if (mismatch != "missing") entities.Add(new GameEntityDescriptor(root.GameKey, CatalogKind.Weapon, "다른 무기", tier: 1));
        if (mismatch == "duplicate") entities.Add(new GameEntityDescriptor(root.GameKey, CatalogKind.Weapon, root.KoreanName, tier: 1));
        Assert.True(catalog.Verify("minigun", CatalogKind.Weapon, entities).AllowsAutomaticAction);
        var preset = NativePreset.ParseCompact("AAP1\nW:500\nC:PinkRabbit\nS:\n");
        var warnings = new List<string>();
        StartingPresetFallback.ApplyWeapon(preset, "minigun", catalog, entities, warnings);
        Assert.Equal(500, preset.Weapon);
        Assert.Contains(warnings, x => x.Contains("crossbow") && x.Contains("시작 무기"));
    }

    private static IEnumerable<GameEntityDescriptor> Describe(VersionedCatalog catalog) =>
        catalog.Entries.Select(x => new GameEntityDescriptor(x.GameKey, x.Kind, x.KoreanName, x.Rarity, x.Category, x.Tier, x.ParentGameKey, x.IsDual));

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("raging_helbanus")]
    public void MissingOrUnverifiedMappingNeverGrantsAWeapon(string? slug)
    {
        var preset = NativePreset.ParseCompact("AAP1\nW:20\nC:PinkRabbit\nS:\n");
        StartingPresetFallback.ApplyWeapon(preset, slug, VersionedCatalog.LoadEmbedded(), Array.Empty<GameEntityDescriptor>(), new List<string>());
        Assert.Equal(20, preset.Weapon);
    }

    [Theory]
    [InlineData(500, null, 500, 500, true)]
    [InlineData(0, null, 0, 0, true)]
    [InlineData(500, null, 500, 400, false)]
    [InlineData(500, null, 400, 500, false)]
    [InlineData(500, null, 500, null, false)]
    [InlineData(500, null, null, 500, false)]
    [InlineData(500, 600, 500, 600, true)]
    [InlineData(500, 600, 500, 500, false)]
    [InlineData(500, 0, 500, 0, true)]
    public void BothSelectedAndActuallyEquippedWeaponMustMatchNativeRules(int selected, int? forced, int? stored, int? equipped, bool matches) =>
        Assert.Equal(matches, StartingWeaponState.Matches(selected, forced, stored, equipped));
}
