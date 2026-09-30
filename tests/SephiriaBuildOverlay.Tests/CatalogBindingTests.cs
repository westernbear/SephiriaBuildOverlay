using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Tests;

public sealed class CatalogBindingTests
{
    [Fact]
    public void DualArtifactRequiresBothRuntimeRarityAndDualFlag()
    {
        var catalog = new VersionedCatalog("1.0.33", new[]
        {
            new CatalogEntry { Slug = "dual", GameKey = "1244", Kind = CatalogKind.Artifact,
                KoreanName = "얼음구름 나비", Rarity = "Rare", Category = "glacier", IsDual = true }
        });
        Assert.True(catalog.Verify("dual", CatalogKind.Artifact, new[]
        { new GameEntityDescriptor("1244", CatalogKind.Artifact, "얼음구름 나비", "Rare", "glacier", isDual: true) }).AllowsAutomaticAction);
        foreach (var flag in new bool?[] { false, null })
            Assert.False(catalog.Verify("dual", CatalogKind.Artifact, new[]
            { new GameEntityDescriptor("1244", CatalogKind.Artifact, "얼음구름 나비", "Rare", "glacier", isDual: flag) }).AllowsAutomaticAction);
        Assert.False(catalog.Verify("dual", CatalogKind.Artifact, new[]
        { new GameEntityDescriptor("1244", CatalogKind.Artifact, "얼음구름 나비", "Legend", "glacier", isDual: true) }).AllowsAutomaticAction);
    }

    private static VersionedCatalog Catalog() => new("1.0.33", new[]
    {
        new CatalogEntry { Slug = "a", GameKey = "1145", Kind = CatalogKind.Artifact, KoreanName = "푸른 발톱", Rarity = "Legend", Category = "glacier" }
    });

    [Fact]
    public void ExactVersionedIdDisambiguatesDifferentEntityWithSameName()
    {
        var result = Catalog().Verify("a", CatalogKind.Artifact, new[]
        {
            new GameEntityDescriptor("1145", CatalogKind.Artifact, "푸른 발톱", "Legend", "glacier"),
            new GameEntityDescriptor("999", CatalogKind.Artifact, "푸른 발톱", "Rare", "lake")
        });
        Assert.Equal(BindingStatus.Verified, result.Status);
    }

    [Fact]
    public void WrongIdAndConflictingDescriptorsNeverAuthorizeAction()
    {
        var wrongId = Catalog().Verify("a", CatalogKind.Artifact, new[]
        { new GameEntityDescriptor("181", CatalogKind.Artifact, "푸른 발톱", "Legend", "glacier") });
        Assert.Equal(BindingStatus.MetadataMismatch, wrongId.Status);
        Assert.False(wrongId.AllowsAutomaticAction);
        var conflict = Catalog().Verify("a", CatalogKind.Artifact, new[]
        {
            new GameEntityDescriptor("1145", CatalogKind.Artifact, "푸른 발톱", "Legend", "glacier"),
            new GameEntityDescriptor("1145", CatalogKind.Artifact, "푸른 발톱", "Rare", "lake")
        });
        Assert.Equal(BindingStatus.Ambiguous, conflict.Status);
    }
}
