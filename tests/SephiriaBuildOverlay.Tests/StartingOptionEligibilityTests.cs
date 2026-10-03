using SephiriaBuildOverlay.Plugin;
using SephiriaBuildOverlay.Core.Import;

namespace SephiriaBuildOverlay.Tests;

public sealed class StartingOptionEligibilityTests
{
    [Theory]
    [InlineData("Default", false, true)]
    [InlineData("Default", true, true)]
    [InlineData("Purchase", true, true)]
    [InlineData("Purchase", false, false)]
    [InlineData("Locked", true, true)]
    [InlineData("Locked", false, false)]
    [InlineData("Locked", null, null)]
    [InlineData("Purchase", null, null)]
    [InlineData("Unknown", true, null)]
    [InlineData(null, true, null)]
    public void SkinUsesNativeDefaultOrOwnedRuleNotMetadataAlone(string? type, bool? owned, bool? expected) =>
        Assert.Equal(expected, StartingOptionEligibility.SkinSelectable(true, true, type, owned));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MissingAndDifferentCostumeSkinsAreNotMisreportedAsLocked(bool exists, bool matches) =>
        Assert.Null(StartingOptionEligibility.SkinSelectable(exists, matches, "Purchase", false));

    [Fact]
    public void OwnedLockedSkinAndUnknownReadDoNotEmitFalseUnlockWarning()
    {
        var warnings = new StartingPresetWarnings();
        warnings.Record("스킨", "owned", "농부 다람쥐", true, StartingOptionEligibility.SkinSelectable(true, true, "Locked", true));
        warnings.Record("스킨", "unknown", "상태 불명", true, null);
        Assert.Empty(warnings.Items); Assert.Null(warnings.Bubble);
    }

    [Fact]
    public void AuthorsUnavailableJournalFavoritesAreNotLockedStartingArtifacts()
    {
        var warnings = new StartingPresetWarnings();
        warnings.RecordUnavailableFavorite("1154", "맹독 포자 주머니", false);
        warnings.RecordUnavailableFavorite("1282", "도시락", false);
        warnings.RecordUnavailableFavorite("1154", "맹독 포자 주머니", false);
        warnings.RecordUnavailableFavorite("other", "이미 발견", true);
        warnings.RecordUnavailableFavorite("unknown", "상태 불명", null);
        Assert.Empty(warnings.Items); Assert.Equal(2, warnings.FavoriteItems.Count);
        Assert.DoesNotContain("미해금", warnings.Bubble!);
        Assert.Contains("즐겨찾기", warnings.Bubble!); Assert.Contains("추천은 유지", warnings.Bubble!);
    }

    [Fact]
    public void RealLockedStartingOptionAndSeparateJournalRestrictionHaveDistinctReasons()
    {
        var warnings = new StartingPresetWarnings();
        warnings.Record("시작 아티팩트", "pocket", "진짜 미해금", true, false);
        warnings.RecordUnavailableFavorite("1154", "맹독 포자 주머니", false);
        Assert.Single(warnings.Items); Assert.Single(warnings.FavoriteItems);
        Assert.Contains("시작 아티팩트: 진짜 미해금", warnings.Bubble!);
        Assert.DoesNotContain("아티팩트: 맹독", warnings.Bubble!);
    }

    [Fact]
    public void SuccessfulNativeReadbackSuppressesStaleExcludedWarningsForEveryOptionType()
    {
        var warnings = new StartingPresetWarnings();
        warnings.Record("무기", "500", "무기", true, false);
        warnings.Record("의상", "Squirrel", "의상", true, false);
        warnings.Record("스킨", "owned-skin", "스킨", true, false);
        warnings.Record("특성", "4", "특성", true, false);
        warnings.Record("시작 아티팩트", "3002", "주머니", true, false);
        warnings.RecordUnavailableFavorite("1154", "맹독 포자 주머니", false);
        var applied = NativePreset.ParseCompact("AAP1\nW:500\nC:Squirrel\nS:owned-skin\nP:4,10\nD:-1,3002,1\nF:1154\n");
        warnings.RemoveAppliedOptions(applied);
        Assert.Empty(warnings.Items); Assert.Empty(warnings.FavoriteItems); Assert.Null(warnings.Bubble);
        warnings.RemoveAppliedOptions(applied); Assert.Null(warnings.Bubble);
    }

    [Fact]
    public void ReadbackRetainsOnlyActuallyMissingStartingOptionsAndFavorites()
    {
        var warnings = new StartingPresetWarnings();
        warnings.Record("스킨", "missing", "스킨", true, false);
        warnings.RecordUnavailableFavorite("1154", "맹독 포자 주머니", false);
        warnings.RecordUnavailableFavorite("1282", "도시락", false);
        warnings.RemoveAppliedOptions(NativePreset.ParseCompact("AAP1\nW:0\nC:PinkRabbit\nS:\nF:1282\n"));
        Assert.Single(warnings.Items); Assert.Equal(new[] { "맹독 포자 주머니" }, warnings.FavoriteItems);
        Assert.DoesNotContain("도시락", warnings.Bubble!);
    }
}
