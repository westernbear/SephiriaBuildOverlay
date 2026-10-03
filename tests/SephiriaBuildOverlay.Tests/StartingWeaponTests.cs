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
    public void StructuredTargetOverridesOldNativeSelectionWithVerifiedStartingWeapon(string slug, int root)
    {
        var catalog = VersionedCatalog.LoadEmbedded();
        var entities = catalog.Entries.Select(x => new GameEntityDescriptor(x.GameKey, x.Kind, x.KoreanName, x.Rarity, x.Category, x.Tier, x.ParentGameKey, x.IsDual)).ToArray();
        var preset = NativePreset.ParseCompact("AAP1\nW:20\nC:PinkRabbit\nS:\nB:1\nR:STURDY,1\n");
        var warnings = new List<string>();
        StartingPresetFallback.ApplyWeapon(preset, slug, catalog, entities, warnings);
        Assert.Equal(root, preset.Weapon); Assert.Empty(warnings); Assert.Single(preset.Fruits); Assert.Equal(1, preset.Adaptive);
        Assert.NotEqual(int.Parse(catalog.FindBySlug(slug, CatalogKind.Weapon)!.GameKey), preset.Weapon);
    }

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
