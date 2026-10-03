using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class StartingFruitTests
{
    private const string Compact = "AAP1\nW:500\nC:Squirrel\nS:skin\nF:1154\nP:4,10\nD:-1,3002,1\nB:0\nR:STURDY,-1;ACADEMY,1;ACADEMY,1;ACADEMY,1;STURDY,-1;PRECISION,1;PRECISION,1;ELEMENTAL,1\n";

    [Fact]
    public void ExplicitWikiFruitOverridesStaleNativeCodeAndRetainsOtherSettings()
    {
        var preset = NativePreset.ParseCompact(Compact);
        StartingPresetFallback.ApplyFruitSkewer(preset, new[] { new ImportedFruit("academy", 3), new ImportedFruit("firmness", -2), new ImportedFruit("element", 1) });
        preset.LimitLoadout(_ => 1, 1, new HashSet<string> { "ACADEMY", "STURDY", "ELEMENTAL", "PRECISION" }, 6, 3, 2);
        Assert.Equal(new[] { ("ACADEMY", 1), ("ACADEMY", 1), ("ACADEMY", 1), ("STURDY", -1), ("STURDY", -1), ("ELEMENTAL", 1) }, preset.Fruits);
        Assert.Equal(0, preset.Adaptive); Assert.Equal(500, preset.Weapon); Assert.Equal("Squirrel", preset.Costume);
        Assert.Equal("skin", preset.Skin); Assert.Equal(new[] { 1154 }, preset.Favorites); Assert.Equal(new[] { (4UL, 10) }, preset.Passives); Assert.Single(preset.Pocket);
    }

    [Fact]
    public void MissingWikiFruitPreservesNativeCodeButExplicitEmptyClearsIt()
    {
        var preset = NativePreset.ParseCompact(Compact);
        StartingPresetFallback.ApplyFruitSkewer(preset, null);
        Assert.Equal(Compact, preset.Compact());
        StartingPresetFallback.ApplyFruitSkewer(preset, Array.Empty<ImportedFruit>());
        Assert.Empty(preset.Fruits); Assert.Equal(0, preset.Adaptive);
    }

    [Fact]
    public void AdaptiveFruitConsumesOneSlotWithoutRemovingDuplicateWeights()
    {
        var preset = NativePreset.ParseCompact(Compact);
        StartingPresetFallback.ApplyFruitSkewer(preset, new[] { new ImportedFruit("adaptive_drop_bonus", 1), new ImportedFruit("extrium", 2), new ImportedFruit("precision", 2), new ImportedFruit("magic_engineering", 1) });
        preset.LimitLoadout(_ => null, 0, new HashSet<string> { "DARKCLOUD", "PRECISION", "MAGITECH" }, 6, 3, 2);
        Assert.Equal(1, preset.Adaptive); Assert.Equal(5, preset.Fruits.Count);
        Assert.True(StartingFruitState.Matches(preset, 1, preset.Fruits.AsEnumerable().Reverse().ToArray()));
        Assert.False(StartingFruitState.Matches(preset, 0, preset.Fruits));
        Assert.False(StartingFruitState.Matches(preset, 1, preset.Fruits.Distinct().ToArray()));
        Assert.False(StartingFruitState.Matches(preset, 1, Array.Empty<(string, int)>()));
    }

    [Fact]
    public void EmptyAndUnknownNativeFruitResponsesAreDifferent()
    {
        var empty = NativePreset.ParseCompact("AAP1\nW:0\nC:PinkRabbit\nS:\nB:0\nR:\n");
        Assert.True(StartingFruitState.Matches(empty, 0, Array.Empty<(string, int)>()));
        Assert.False(StartingFruitState.Matches(empty, null, Array.Empty<(string, int)>()));
        Assert.False(StartingFruitState.Matches(empty, 0, null));
        Assert.False(StartingFruitState.Matches(NativePreset.ParseCompact(Compact), 0, empty.Fruits));
    }

    [Fact]
    public void WrongFruitSignOrUnexpectedFruitDoesNotAcknowledgePreset()
    {
        var preset = NativePreset.ParseCompact(Compact);
        var wrong = preset.Fruits.Select(x => (x.Category, -x.Value)).ToArray();
        Assert.False(StartingFruitState.Matches(preset, 0, wrong));
        Assert.False(StartingFruitState.Matches(preset, 0, preset.Fruits.Concat(new[] { ("ICE", 1) }).ToArray()));
    }
}
