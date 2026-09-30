using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Review;
using SephiriaBuildOverlay.Plugin;

namespace SephiriaBuildOverlay.Tests;

public sealed class ReviewCheckpointTests
{
    [Fact]
    public void RoundtripPreservesReviewedDuplicatesWithoutPersistingBindings()
    {
        var catalog = VersionedCatalog.LoadEmbedded("1.0.33");
        var build = new ImportedBuild(Guid.NewGuid(), "test", "1.0.33", null, null,
            new[] { new ImportedSection("a", "required", "", new[] { new ImportedItem("1", "blue_claws"), new ImportedItem("2", "blue_claws") }) }, new Dictionary<string, int>());
        var review = new BuildReviewSession(build, catalog);
        review.Sections[0].Role = TargetRole.Required;
        review.Sections[0].Items[1].DesiredAcquisitions = 3;
        review.Sections[0].Items[1].PriorityOverride = 7;
        var saved = JsonConvert.DeserializeObject<ReviewCheckpoint>(JsonConvert.SerializeObject(ReviewCheckpoint.Capture(review, true)))!;
        var restored = saved.Restore(catalog);
        Assert.True(saved.WasActivated);
        Assert.Empty(restored.Bindings);
        Assert.Equal(TargetRole.Required, restored.Sections[0].Role);
        Assert.Equal(3, restored.Sections[0].Items[1].DesiredAcquisitions);
        Assert.Equal(7, restored.Sections[0].Items[1].PriorityOverride);
    }

    [Fact]
    public void UnclassifiedCheckpointNeverSilentlyActivatesAndCorruptionIsRejected()
    {
        var catalog = VersionedCatalog.LoadEmbedded("1.0.33");
        var build = new ImportedBuild(Guid.NewGuid(), "test", "1.0.33", null, null,
            new[] { new ImportedSection("a", "unknown", "", Array.Empty<ImportedItem>()) }, new Dictionary<string, int>());
        var saved = ReviewCheckpoint.Capture(new BuildReviewSession(build, catalog), false);
        Assert.False(saved.WasActivated);
        Assert.False(saved.Restore(catalog).ValidateForActivation("1.0.33").CanActivate);
        saved.Sections[0].Role = (TargetRole)999;
        Assert.Throws<InvalidDataException>(() => saved.Restore(catalog));
    }
}
