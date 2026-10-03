using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class StartingPresetFallbackTests
{
    private const string Current = "AAP1\nW:0\nC:PinkRabbit\nS:old\nF:123\nP:5,8\nD:1,100,1\nB:1\nR:lake,1\n";
    private readonly VersionedCatalog _catalog = VersionedCatalog.LoadEmbedded();
    private IReadOnlyList<GameEntityDescriptor> Entities => _catalog.Entries.Select(x => new GameEntityDescriptor(
        x.GameKey, x.Kind, x.KoreanName, x.Rarity, x.Category, x.Tier, x.ParentGameKey, x.IsDual)).ToArray();
    private static ulong? Passive(string slug) => StartingPassiveMapping.Expected(slug).Id;
    private ImportedBuild Parse(object fields, string version = "1.0.33")
    {
        var json = Newtonsoft.Json.Linq.JObject.FromObject(fields);
        var id = Guid.NewGuid(); json["postUuid"] = id.ToString(); json["version"] = version;
        json["content"] ??= new Newtonsoft.Json.Linq.JArray();
        return WikiBuildParser.Parse(json.ToString(), id);
    }

    [Fact]
    public void CodeMissingThunderBuildDerivesRootCostumeAllSevenTraitsAndRepeatedFruit()
    {
        // Structured fields from Wiki 7d554477-86e8-4932-a42c-a65ebfe729cf.
        var build = Parse(new { weapon = "super_conductor", costume = "frog", preset_code = (string?)null,
            ability = new { @base = 10, will = 20, anger = 20, rapid = 0, wisdom = 0, patience = 0, survival = 0 },
            fruit_skewer = new[] { new { key = "adaptive_drop_bonus", value = 1 }, new { key = "extrium", value = 2 },
                new { key = "precision", value = 2 }, new { key = "magic_engineering", value = 1 } } });
        var warnings = new List<string>();
        var result = StartingPresetFallback.Create(build, Current, _catalog, Entities, Passive, warnings);
        Assert.Equal(int.Parse(_catalog.BuildWeaponPath("super_conductor")[0]), result.Weapon);
        Assert.Equal("Frog", result.Costume); Assert.Empty(result.Skin);
        Assert.Contains((11UL, 10), result.Passives); Assert.Contains((9UL, 20), result.Passives); Assert.Contains((4UL, 20), result.Passives);
        Assert.Equal(3, result.Passives.Count); Assert.Equal(1, result.Adaptive);
        Assert.Equal(new[] { ("darkcloud", 1), ("darkcloud", 1), ("precision", 1), ("precision", 1), ("magitech", 1) }, result.Fruits);
        Assert.Empty(warnings); Assert.Equal(result.Compact(), NativePreset.ParseCompact(result.Compact()).Compact());
    }

    [Fact]
    public void AbsentFieldsKeepCurrentSettingsIncludingOldFruitCheckpoint()
    {
        var build = Parse(new { });
        var result = StartingPresetFallback.Create(build, Current, _catalog, Entities, Passive, new List<string>());
        Assert.Equal(Current, result.Compact()); Assert.False(build.HasTalentAllocation); Assert.Null(build.FruitSkewer);
        var restored = JsonConvert.DeserializeObject<ImportedBuild>(JsonConvert.SerializeObject(build))!;
        Assert.False(restored.HasTalentAllocation); Assert.Null(restored.FruitSkewer);
    }

    [Theory]
    [InlineData("1.0.24")]
    [InlineData("1.0.31")]
    [InlineData("1.0.34")]
    public void OlderOrNewerWikiVersionUsesVerifiedCurrentCatalogForCodeMissingPreset(string version)
    {
        var build = Parse(new { weapon = "super_conductor", costume = "frog", ability = new { @base = 10 } }, version);
        var warnings = new List<string>();
        var preset = StartingPresetFallback.Create(build, Current, _catalog, Entities, Passive, warnings);
        Assert.Equal(int.Parse(_catalog.BuildWeaponPath("super_conductor")[0]), preset.Weapon);
        Assert.Equal("Frog", preset.Costume);
        Assert.Contains((11UL, 10), preset.Passives);
        Assert.Empty(warnings);
        var mismatched = StartingPresetFallback.Create(build, Current, _catalog, Array.Empty<GameEntityDescriptor>(), _ => null, warnings);
        Assert.Equal(0, mismatched.Weapon);
        Assert.Equal("PinkRabbit", mismatched.Costume);
        Assert.Equal(new[] { (5UL, 8) }, mismatched.Passives);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void ExplicitEmptyFruitClearsFruitAndAdaptive()
    {
        var result = StartingPresetFallback.Create(Parse(new { fruit_skewer = Array.Empty<object>() }), Current, _catalog, Entities, Passive, new List<string>());
        Assert.Empty(result.Fruits); Assert.Equal(0, result.Adaptive);
    }

    [Fact]
    public void ExplicitStartingSectionWinsAndDuplicatesAreSeparateBeforeNativeLimits()
    {
        var build = Parse(new { content = new[] {
            new { label = "기타", items = new[] { new { value = "blue_claws" } } },
            new { label = "시작 아티팩트", items = new[] { new { value = "six_leaf_clover" }, new { value = "six_leaf_clover" } } } } });
        var result = StartingPresetFallback.Create(build, Current, _catalog, Entities, Passive, new List<string>());
        Assert.Equal(2, result.Favorites.Count); Assert.Equal(2, result.Pocket.Count);
        Assert.All(result.Pocket, x => { Assert.Equal(-1, x.Instance); Assert.Equal(int.Parse(_catalog.FindBySlug("six_leaf_clover")!.GameKey), x.Entity); });
        result.LimitLoadout(_ => 1, 1, new HashSet<string>(), 0, 0, 0, _ => true);
        Assert.Single(result.Pocket);
    }

    [Fact]
    public void WithoutStartingSectionUsesOriginalItemOrderAndUnverifiedItemsAreNotInserted()
    {
        var build = Parse(new { content = new[] { new { label = "목표", items = new[] {
            new { value = "six_leaf_clover" }, new { value = "missing" }, new { value = "blue_claws" } } } } });
        var warnings = new List<string>();
        var result = StartingPresetFallback.Create(build, Current, _catalog, Entities, Passive, warnings);
        Assert.Equal(new[] { "six_leaf_clover", "blue_claws" }.Select(x => int.Parse(_catalog.FindBySlug(x)!.GameKey)), result.Pocket.Select(x => x.Entity));
        Assert.Single(warnings);
    }

    [Fact]
    public void MetadataMismatchKeepsExistingWeaponCostumeAndWholePassiveAllocation()
    {
        var warnings = new List<string>();
        var result = StartingPresetFallback.Create(Parse(new { weapon = "super_conductor", costume = "frog", ability = new { @base = 10 } }),
            Current, _catalog, Array.Empty<GameEntityDescriptor>(), _ => null, warnings);
        Assert.Equal(0, result.Weapon); Assert.Equal("PinkRabbit", result.Costume); Assert.Equal(new[] { (5UL, 8) }, result.Passives);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void UnknownFruitIsFilteredByNativeCategoryCapacityAndUnlockPolicy()
    {
        var result = StartingPresetFallback.Create(Parse(new { ability = new { @base = 10, will = 20 }, fruit_skewer = new[] {
            new { key = "bad", value = 1 }, new { key = "extrium", value = -2 } } }), Current, _catalog, Entities, Passive, new List<string>());
        result.LimitPassives(new Dictionary<ulong, int> { [11] = 20 }, 5);
        Assert.Equal(new[] { (11UL, 5) }, result.Passives);
        result.LimitLoadout(_ => null, 0, new HashSet<string> { "darkcloud" }, 5, 2, 1);
        Assert.Equal(new[] { ("darkcloud", -1) }, result.Fruits); Assert.Empty(result.Pocket);
    }

    [Theory]
    [InlineData("base", 11UL, "Passive_Ingenuity_Name", "기지")]
    [InlineData("anger", 4UL, "Passive_Crit_Name", "분노")]
    [InlineData("rapid", 5UL, "Passive_Swiftness_Name", "신속")]
    [InlineData("survival", 6UL, "Passive_Survive_Name", "생존")]
    [InlineData("patience", 7UL, "Passive_Patience_Name", "인내")]
    [InlineData("wisdom", 8UL, "Passive_MPRegen_Name", "지혜")]
    [InlineData("will", 9UL, "Passive_Item_Name", "의지")]
    public void RuntimeTraitBindingRequiresIdLocalizationKeyAndKoreanName(string slug, ulong id, string key, string name)
    {
        Assert.True(StartingPassiveMapping.Matches(slug, id, key, name));
        Assert.False(StartingPassiveMapping.Matches(slug, id + 1, key, name));
        Assert.False(StartingPassiveMapping.Matches(slug, id, key + "_new", name));
        Assert.False(StartingPassiveMapping.Matches(slug, id, key, "다름"));
    }

    [Fact]
    public void CostumeCatalogIncludesEveryActualWikiCostumeAndRejectsMismatch()
    {
        Assert.Equal(27, _catalog.Entries.Count(x => x.Kind == CatalogKind.Costume));
        Assert.Equal("DarkBrownRabbit", _catalog.FindBySlug("braid")!.GameKey);
        Assert.Equal("Seren", _catalog.FindBySlug("scholar_lizard")!.GameKey);
        Assert.False(_catalog.Verify("frog", CatalogKind.Costume, new[] { new GameEntityDescriptor("Frog", CatalogKind.Costume, "잘못됨") }).AllowsAutomaticAction);
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("[{\"key\":\"ice\",\"value\":17}]")]
    [InlineData("[{\"key\":\"ice\",\"value\":-17}]")]
    [InlineData("[{\"key\":\"ice\",\"value\":1.5}]")]
    [InlineData("{}")]
    public void MalformedFruitIsRejectedBeforeNativeImport(string fruit)
    {
        var id = Guid.NewGuid();
        Assert.ThrowsAny<JsonException>(() => WikiBuildParser.Parse($"{{\"postUuid\":\"{id}\",\"version\":\"1.0.33\",\"content\":[],\"fruit_skewer\":{fruit}}}", id));
    }
}
