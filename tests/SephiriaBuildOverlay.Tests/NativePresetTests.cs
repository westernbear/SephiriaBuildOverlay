using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class NativePresetTests
{
    private const string Compact = "AAP1\nW:0\nC:PinkRabbit\nS:\nF:123,124\nP:1,10;2,8;3,5\nD:1,100,1;2,200,1;3,100,1\nB:1\nR:ice,1;ice,1;ice,-1;other,1\n";

    [Fact]
    public void RealWikiNativeCodeUsesSupportedFullPresetProtocol()
    {
        // Wiki API c4fa31fc-5b95-4e04-a08b-dfb752f3bf21; only its
        // native clipboard payload is retained, not free-form article content.
        const string code = "AAF_PRESET_OBFZ|v1Xuh8aW9uQW5pZ0z8DeuxXUA2knG4sFEdKqSqYCKwRaCwXxlOJYfgSkb0vVIvS8s1Gjkw5qkWR997jj7SFE7ThCDF9L4nwv47zOcFtkrN4BvTQ6zyXIMOhPy6mmbZ12U3iuNJbDgsWGlyC0x3eyYVdIkQYZDhEifiBSFESXd+cr5j8Q1FQIHazRsk9dpEMn2LeeFgmx+oMCdvLI0m9+DM2G8FX698LEnUu3JthjszWcXha71qgqZ3Sx64Q7YToZFAgGh8xWLePysgcOoLYPHhsOCat6s5IMTXxegE+mB+BZiIHmGGnP6rpLQ+O+/vp/MLvI2fGTYKFsmyse6vclf015dC+6QMyJmUly6KCPAbH00BMU7O78GUlBMXzDXZEr68fAaMdVNo";
        var preset = NativePreset.Decode(code);
        Assert.NotEmpty(preset.Costume);
        Assert.NotEmpty(preset.Passives);
        Assert.Equal(preset.Compact(), NativePreset.ParseCompact(preset.Compact()).Compact());
    }

    [Fact]
    public void NativeClipboardRoundtripIncludesEveryStartingSetting()
    {
        var decoded = NativePreset.Decode(Encode(Compact));
        Assert.Equal(Compact, decoded.Compact());
        Assert.Equal(2, decoded.Favorites.Count);
        Assert.Equal(3, decoded.Passives.Count);
        Assert.Equal(3, decoded.Pocket.Count);
        Assert.Equal(4, decoded.Fruits.Count);
    }

    [Theory]
    [InlineData("AAF_PRESET_OBFZ|v2abc")]
    [InlineData("AAF_PRESET_OBFZ|v1!!!")]
    [InlineData("AAP1\nW:0\nC:PinkRabbit\nS:")]
    public void UnsupportedOrMalformedClipboardCannotReachNativeParser(string code) => Assert.ThrowsAny<Exception>(() => NativePreset.Decode(code));

    [Fact]
    public void CompressedBombAndOversizedCodeAreBounded()
    {
        Assert.Throws<FormatException>(() => NativePreset.Decode(Encode(new string('x', NativePreset.MaxExpandedBytes + 1))));
        Assert.Throws<FormatException>(() => NativePreset.Decode(new string('x', NativePreset.MaxCodeLength + 1)));
    }

    [Theory]
    [InlineData("W:1")]
    [InlineData("X:1")]
    [InlineData("P:1,-1")]
    [InlineData("P:1,5;1,8")]
    [InlineData("D:1,100,127")]
    [InlineData("D:1,100,1;1,200,1")]
    [InlineData("B:2147483647")]
    [InlineData("R:ice,100")]
    [InlineData("F:1,1")]
    [InlineData("C:Bad%0AName")]
    public void UntrustedFieldsAreRejectedBeforeAnyMutation(string field) => Assert.Throws<FormatException>(() => NativePreset.ParseCompact("AAP1\nW:0\nC:PinkRabbit\nS:\n" + field));

    [Fact]
    public void UnlockedPassivesRespectMaximumAndExistingAvailablePoints()
    {
        var preset = NativePreset.ParseCompact(Compact);
        preset.LimitPassives(new Dictionary<ulong, int> { [1] = 7, [3] = 20 }, 10);
        Assert.Equal(new[] { (1UL, 7), (3UL, 3) }, preset.Passives);
        var empty = NativePreset.ParseCompact(Compact);
        empty.LimitPassives(new Dictionary<ulong, int> { [1] = 20 }, -1);
        Assert.Empty(empty.Passives);
    }

    [Fact]
    public void PocketAndFruitFilteringPreservesCapacityUnlocksAndCategoryLimits()
    {
        var preset = NativePreset.ParseCompact(Compact);
        preset.LimitLoadout(id => id == 100 ? 3 : null, 6, new HashSet<string> { "ice" }, 4, 1, 1);
        Assert.Single(preset.Pocket); // No duplicate pocket entity; locked 200 excluded.
        Assert.Equal(new[] { ("ice", 1), ("ice", -1) }, preset.Fruits);
        Assert.Equal(1, preset.Adaptive);
        var none = NativePreset.ParseCompact(Compact);
        none.LimitLoadout(_ => 3, 0, new HashSet<string> { "ice" }, 0, 10, 10);
        Assert.Empty(none.Pocket); Assert.Empty(none.Fruits); Assert.Equal(0, none.Adaptive);
    }

    [Fact]
    public void WikiFruitIdsBindToExactNativeCaseBeforeCapacityFiltering()
    {
        var preset = NativePreset.ParseCompact("AAP1\nW:0\nC:PinkRabbit\nS:\nB:0\nR:academy,1;academy,1;sturdy,-1;elemental,1\n");
        preset.LimitLoadout(_ => null, 0, new HashSet<string> { "ACADEMY", "STURDY", "ELEMENTAL" }, 6, 3, 2);
        Assert.Equal(new[] { ("ACADEMY", 1), ("ACADEMY", 1), ("STURDY", -1), ("ELEMENTAL", 1) }, preset.Fruits);
        Assert.Contains("R:ACADEMY,1;ACADEMY,1;STURDY,-1;ELEMENTAL,1", preset.Compact());
    }

    [Fact]
    public void MixedCaseFruitDuplicatesShareTheNativeCategoryLimit()
    {
        var preset = NativePreset.ParseCompact("AAP1\nW:0\nC:PinkRabbit\nS:\nB:1\nR:academy,1;ACADEMY,1;Academy,1;academy,-1\n");
        preset.LimitLoadout(_ => null, 0, new HashSet<string> { "ACADEMY" }, 4, 2, 1);
        Assert.Equal(new[] { ("ACADEMY", 1), ("ACADEMY", 1), ("ACADEMY", -1) }, preset.Fruits);
        Assert.Equal(1, preset.Adaptive);
    }

    [Fact]
    public void AmbiguousFruitCaseBindingDoesNotGuessButExactIdsRemainValid()
    {
        var preset = NativePreset.ParseCompact("AAP1\nW:0\nC:PinkRabbit\nS:\nB:0\nR:Academy,1;academy,-1;ACADEMY,1;missing,1\n");
        preset.LimitLoadout(_ => null, 0, new HashSet<string> { "academy", "ACADEMY" }, 6, 3, 2);
        Assert.Equal(new[] { ("academy", -1), ("ACADEMY", 1) }, preset.Fruits);
    }

    [Fact]
    public void FirstNativeStageCannotLoadPocketOrFruitBeforeStatValidation()
    {
        var preset = NativePreset.ParseCompact(Compact);
        var first = NativePreset.ParseCompact(preset.Compact(false));
        Assert.Empty(first.Pocket); Assert.Empty(first.Fruits); Assert.Equal(0, first.Adaptive);
        Assert.Equal(preset.Passives, first.Passives);
        Assert.Equal(preset.Favorites, first.Favorites);
    }

    [Fact]
    public void CommonPocketDuplicatesRemainSeparateWithinCapacity()
    {
        var preset = NativePreset.ParseCompact(Compact);
        preset.LimitLoadout(id => id == 100 ? 3 : null, 6, new HashSet<string>(), 0, 0, 0, _ => true);
        Assert.Equal(2, preset.Pocket.Count);
        Assert.Equal(new[] { 1, 3 }, preset.Pocket.Select(x => x.Instance));
    }

    [Fact]
    public void ValidatedCostumeCanDropMismatchedOrLockedSkinWithoutChangingOtherFields()
    {
        var preset = NativePreset.ParseCompact(Compact);
        preset.SetValidatedCostume("PinkRabbit", "");
        Assert.Equal("PinkRabbit", preset.Costume); Assert.Empty(preset.Skin);
        Assert.Equal(2, preset.Favorites.Count);
        Assert.Throws<FormatException>(() => preset.SetValidatedCostume("bad\nname", ""));
    }

    [Theory]
    [InlineData(null, "a", "1.0.33", "1.0.33", true, true, false, false, false)]
    [InlineData("a", "b", "1.0.33", "1.0.33", true, true, false, false, false)]
    [InlineData("a", "a", "1.0.33", "1.0.33", false, true, false, false, false)]
    [InlineData("a", "a", "1.0.33", "1.0.33", true, false, false, false, false)]
    [InlineData("a", "a", "1.0.33", "1.0.33", true, true, true, false, false)]
    [InlineData("a", "a", "1.0.33", "1.0.33", true, true, false, true, false)]
    [InlineData("a", "a", "1.0.33", "1.0.33", true, true, false, false, true)]
    public void UnsafeContextsNeverApply(string? imported, string? current, string source, string game, bool owner, bool server, bool remote, bool pending, bool editing) =>
        Assert.False(StartingPresetPolicy.CanApply(imported, current, source, game, owner, server, remote, pending, editing));

    [Fact]
    public void FreshSoloLobbyImportMayApplyButRestorationHasNoImportContext()
    {
        Assert.True(StartingPresetPolicy.CanApply("a", "a", "1.0.33", "1.0.33", true, true, false, false, false));
        Assert.False(StartingPresetPolicy.CanApply(null, "a", "1.0.33", "1.0.33", true, true, false, false, false));
    }

    [Theory]
    [InlineData("1.0.24", "1.0.33")]
    [InlineData("1.0.31", "1.0.33")]
    [InlineData("1.0.33", "1.0.34")]
    public void VersionMismatchDoesNotRejectOtherwiseSafePresetLobby(string source, string game) =>
        Assert.True(StartingPresetPolicy.CanApply("a", "a", source, game, true, true, false, false, false));

    [Fact]
    public void PresetRefusalExplainsTheActualFailedGuard()
    {
        Assert.Null(StartingPresetPolicy.RejectionReason("a", "a", true, true, false, false, false));
        Assert.Contains("런", StartingPresetPolicy.RejectionReason("a", "b", true, true, false, false, false)!);
        Assert.Contains("편집", StartingPresetPolicy.RejectionReason("a", "a", true, true, false, false, true)!);
        Assert.Contains("응답", StartingPresetPolicy.RejectionReason("a", "a", true, true, false, true, false)!);
        Assert.Contains("싱글플레이", StartingPresetPolicy.RejectionReason("a", "a", true, true, true, false, false)!);
    }

    [Fact]
    public void ParserAndOldCheckpointsRemainCompatible()
    {
        var id = Guid.NewGuid();
        var code = Encode(Compact);
        var json = JsonConvert.SerializeObject(new { postUuid = id, version = "1.0.33", content = Array.Empty<object>(), preset_code = code });
        var build = WikiBuildParser.Parse(json, id);
        Assert.Equal(code, build.NativePresetCode);
        Assert.Equal(code, JsonConvert.DeserializeObject<ImportedBuild>(JsonConvert.SerializeObject(build))!.NativePresetCode);
        var old = WikiBuildParser.Parse(JsonConvert.SerializeObject(new { postUuid = id, version = "1.0.33", content = Array.Empty<object>() }), id);
        Assert.Null(old.NativePresetCode);
    }

    private static string Encode(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        {
            var plain = Encoding.UTF8.GetBytes(text); gzip.Write(plain);
        }
        var bytes = output.ToArray(); var key = Encoding.UTF8.GetBytes("ActionAnimalFarmPresetShareKey");
        for (var i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
        return "AAF_PRESET_OBFZ|v1" + Convert.ToBase64String(bytes);
    }
}
