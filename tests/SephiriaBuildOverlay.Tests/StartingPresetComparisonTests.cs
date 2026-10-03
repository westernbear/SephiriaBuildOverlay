using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class StartingPresetComparisonTests
{
    private const string Requested = "AAP1\nW:500\nC:Squirrel\nS:skin\nF:1154,1282\nP:4,10;9,20\nD:-1,3002,1;-1,3002,1\nB:0\nR:ice,1;ice,1;fire,-1\n";

    [Fact]
    public void NativeOrderingAndAssignedInstanceIdsAreNotPartialFailure()
    {
        var reordered = "AAP1\nW:500\nC:Squirrel\nS:skin\nF:1282,1154\nP:9,20;4,10\nD:123,3002,1;124,3002,1\nB:0\nR:fire,-1;ice,1;ice,1\n";
        Assert.Empty(StartingPresetComparison.ChangedSections(NativePreset.ParseCompact(Requested), NativePreset.ParseCompact(reordered)));
    }

    [Theory]
    [InlineData("W:500", "W:0", "시작 무기")]
    [InlineData("C:Squirrel", "C:PinkRabbit", "의상")]
    [InlineData("S:skin", "S:", "스킨")]
    [InlineData("F:1154,1282", "F:1282", "즐겨찾기")]
    [InlineData("P:4,10;9,20", "P:4,10;9,5", "특성 포인트")]
    [InlineData("D:-1,3002,1;-1,3002,1", "D:-1,3002,1", "시작 주머니")]
    [InlineData("R:ice,1;ice,1;fire,-1", "R:ice,1;fire,-1", "과일꼬치")]
    public void ActualAcceptedSettingDifferencesHavePreciseNonUnlockReasons(string original, string replacement, string expected)
    {
        Assert.Equal(new[] { expected }, StartingPresetComparison.ChangedSections(NativePreset.ParseCompact(Requested),
            NativePreset.ParseCompact(Requested.Replace(original, replacement))));
    }
}
