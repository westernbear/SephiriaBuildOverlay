using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class UiFontFamilyTests
{
    [Fact]
    public void MissingGalmuriDoesNotPreventKoreanFallback() =>
        Assert.Equal("Malgun Gothic", UiFontFamily.Select(new[] { "Arial", "Malgun Gothic" }));

    [Fact]
    public void InstalledGameFamilyTakesPriority() =>
        Assert.Equal("Galmuri11", UiFontFamily.Select(new[] { "Malgun Gothic", "Galmuri9", "Galmuri11" }));

    [Fact]
    public void ActualInstalledNameIsReturned() =>
        Assert.Equal("galmuri9", UiFontFamily.Select(new[] { "Malgun Gothic", "galmuri9" }));

    [Fact]
    public void LocalizedKoreanNameIsSupported() =>
        Assert.Equal("맑은 고딕", UiFontFamily.Select(new[] { "Arial", "맑은 고딕" }));

    [Fact]
    public void UnknownNamesDoNotInventAnUnavailableFont() =>
        Assert.Null(UiFontFamily.Select(new[] { " ", "MissingFont" }));
}
