using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;
using SephiriaBuildOverlay.Core.Review;
using SephiriaBuildOverlay.Core.Runtime;

namespace SephiriaBuildOverlay.Tests;

public sealed class ReviewAndProgressTests
{
    [Fact]
    public void UnknownRecommendedItemRemainsVisibleChecklistButRequiredBlocks()
    {
        var build = new ImportedBuild(Guid.NewGuid(), "unknown", "1.0.33", null, null,
            new[] { new ImportedSection("s", "section", "", new[] { new ImportedItem("one", "missing") }) },
            new Dictionary<string, int>());
        var review = new BuildReviewSession(build, new VersionedCatalog("1.0.33", Array.Empty<CatalogEntry>()));
        review.VerifyBindings(Array.Empty<GameEntityDescriptor>());
        review.Sections[0].Role = TargetRole.Recommended;
        var plan = review.CreatePlan("1.0.33");
        Assert.Empty(plan.Artifacts);
        Assert.Contains(plan.Checklist, line => line.Contains("missing") && line.Contains("수동 확인"));
        review.Sections[0].Role = TargetRole.Required;
        Assert.Throws<InvalidOperationException>(() => review.CreatePlan("1.0.33"));
    }

    [Fact]
    public void ReactivationDoesNotCountConfirmedInventoryAgain()
    {
        var plan = Plan(desired: 3);
        var empty = Snapshot();
        var state = ActiveBuildState.Activate(plan, empty);
        state.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired);
        var held = Snapshot(inventory: new[] { new InventoryArtifact("held", "a") });
        Assert.Same(state, ActiveBuildState.Activate(plan, held, state));
        Assert.Equal(1, state.EffectiveAcquisitions("a"));
        Assert.False(state.Artifacts["a"].IsUncertain);
        state.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired); // wisdom merge leaves one instance
        ActiveBuildState.Activate(plan, held, state);
        Assert.Equal(2, state.EffectiveAcquisitions("a"));
    }

    [Fact]
    public void EmbeddedCatalogContainsCompleteVersionedDataAndWeaponParents()
    {
        var catalog = VersionedCatalog.LoadEmbedded("1.0.33");
        Assert.Equal("1.0.33", catalog.GameVersion);
        Assert.True(catalog.Entries.Count(x => x.Kind == CatalogKind.Artifact) >= 250);
        Assert.True(catalog.Entries.Count(x => x.Kind == CatalogKind.Weapon) >= 150);
        Assert.True(catalog.Entries.Count(x => x.Kind == CatalogKind.Miracle) >= 20);
        Assert.Equal("1145", catalog.FindBySlug("blue_claws", CatalogKind.Artifact)!.GameKey);
        var needle = catalog.FindBySlug("unalloyed_gold_needle", CatalogKind.Artifact);
        Assert.NotNull(needle);
        Assert.Equal("1289", needle!.GameKey);
        Assert.Equal(string.Empty, needle.Category); // artifacts without combos must not be omitted
        Assert.False(needle.IsDual);
        var dual = catalog.FindBySlug("ice_cloud_butterfly", CatalogKind.Artifact)!;
        Assert.Equal("Rare", dual.Rarity);
        Assert.True(dual.IsDual);
        var path = catalog.BuildWeaponPath("eternal_snow_silence");
        Assert.Equal("420", path[^1]);
        Assert.True(path.Count >= 3);
        foreach (var weapon in catalog.Entries.Where(x => x.Kind == CatalogKind.Weapon))
            Assert.Equal(weapon.GameKey, catalog.BuildWeaponPath(weapon.Slug)[^1]);
    }

    [Fact]
    public void EverySectionMustBeClassifiedBeforeActivationAndDuplicatesBecomeCount()
    {
        var (review, entities) = CreateReview();
        review.VerifyBindings(entities);
        Assert.False(review.ValidateForActivation("1.0.33").CanActivate);

        foreach (var section in review.Sections) section.Role = TargetRole.Required;
        var plan = review.CreatePlan("1.0.33");

        Assert.Equal(2, Assert.Single(plan.Artifacts).DesiredAcquisitions);
        Assert.Equal(new[] { "weapon-root", "weapon-final" }, plan.WeaponPath);
        Assert.Equal("miracle-target", plan.MiracleTarget);
    }

    [Fact]
    public void MissingRequiredMappingBlocksButMetadataMismatchOnlyDisablesAutomaticAction()
    {
        var (review, entities) = CreateReview();
        review.Sections[0].Role = TargetRole.Required;
        review.VerifyBindings(Array.Empty<GameEntityDescriptor>());
        Assert.False(review.ValidateForActivation("1.0.33").CanActivate);

        var mismatched = entities.Select(x => x.Kind == CatalogKind.Artifact
            ? new GameEntityDescriptor(x.GameKey, x.Kind, x.KoreanName, "wrong", x.Category, x.Tier, x.ParentGameKey)
            : x).ToArray();
        review.VerifyBindings(mismatched);
        Assert.True(review.ValidateForActivation("1.0.33").CanActivate);
        Assert.Equal(BindingStatus.MetadataMismatch, review.Bindings["a"].Status);
        Assert.False(review.Bindings["a"].AllowsAutomaticAction);
    }

    [Fact]
    public void IntermediateWeaponPathBindingsAreVerified()
    {
        var (review, entities) = CreateReview();
        review.VerifyBindings(entities);
        Assert.Equal(BindingStatus.Verified, review.Bindings["weapon-root"].Status);
        review.VerifyBindings(entities.Where(x => x.GameKey != "weapon-root"));
        Assert.False(review.Bindings["weapon-root"].AllowsAutomaticAction);
    }

    [Fact]
    public void VersionMismatchRequiresExplicitAcceptance()
    {
        var (review, entities) = CreateReview();
        review.Sections[0].Role = TargetRole.Required;
        review.VerifyBindings(entities);
        Assert.Throws<InvalidOperationException>(() => review.CreatePlan("1.0.34"));
        Assert.NotNull(review.CreatePlan("1.0.34", acceptVersionMismatch: true));
    }

    [Fact]
    public void MidRunInstancesAreUncertainMinimumRewardAddsButAltarDoesNot()
    {
        var plan = Plan(desired: 3);
        var snapshot = Snapshot(inventory: new[] { new InventoryArtifact("held", "a") });
        var state = ActiveBuildState.Activate(plan, snapshot);

        Assert.Equal(1, state.EffectiveAcquisitions("a"));
        Assert.True(state.Artifacts["a"].IsUncertain);
        state.RecordArtifact("a", ArtifactProgressEvent.AltarEnhanced);
        Assert.Equal(1, state.EffectiveAcquisitions("a"));
        state.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired);
        Assert.Equal(2, state.EffectiveAcquisitions("a"));
        state.SetUserAdjustment("a", 1);
        Assert.Equal(3, state.EffectiveAcquisitions("a"));
    }

    [Fact]
    public void NewRunDoesNotReusePreviousProgress()
    {
        var plan = Plan();
        var previous = ActiveBuildState.Activate(plan, Snapshot(run: "run-1"));
        previous.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired);
        var next = ActiveBuildState.Activate(plan, Snapshot(run: "run-2"), previous);
        Assert.Equal("run-2", next.RunId);
        Assert.Equal(0, next.EffectiveAcquisitions("a"));
    }

    [Fact]
    public void ActiveStateRoundTripsPerRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sbo-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = ActiveBuildState.Activate(Plan(), Snapshot(run: "run:one"));
            state.RecordArtifact("a", ArtifactProgressEvent.RewardAcquired);
            state.SetUserAdjustment("a", 2);
            var store = new ActiveStateStore(directory);
            store.Save(state);
            var loaded = store.Load(state.SourceBuildId, "run:one");
            Assert.NotNull(loaded);
            Assert.Equal(3, loaded!.EffectiveAcquisitions("a"));
            Assert.Null(store.Load(state.SourceBuildId, "other"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    internal static BuildPlan Plan(int desired = 1, string? miracle = "miracle-target", IReadOnlyList<string>? weapon = null) =>
        new(Guid.NewGuid(), "1.0.33", new[] { new ArtifactTarget("a", desired, TargetRole.Required, 0) },
            weapon ?? Array.Empty<string>(), miracle, new Dictionary<string, int>());

    internal static RunSnapshot Snapshot(string run = "run-1", long revision = 1, ScreenKind screen = ScreenKind.None,
        IReadOnlyList<ScreenCandidate>? candidates = null, IReadOnlyList<InventoryArtifact>? inventory = null,
        string? weapon = null, string? miracle = null, int money = 10, int dice = 2, bool owned = true, bool pending = false) =>
        new(run, "player", revision, screen, candidates ?? Array.Empty<ScreenCandidate>(), inventory ?? Array.Empty<InventoryArtifact>(),
            weapon, miracle, money, dice, owned, pending);

    private static (BuildReviewSession Review, GameEntityDescriptor[] Entities) CreateReview()
    {
        var id = Guid.NewGuid();
        var build = new ImportedBuild(id, "test", "1.0.33", "weapon-final", "miracle-target",
            new[] { new ImportedSection("s", "section", "", new[] { new ImportedItem("1", "a"), new ImportedItem("2", "a") }) },
            TalentNames.All.ToDictionary(x => x, _ => 0));
        var entries = new[]
        {
            Entry("a", "a", CatalogKind.Artifact, "아티팩트", "rare", "precision"),
            Entry("weapon-root", "weapon-root", CatalogKind.Weapon, "시작", null, "sword", 1),
            Entry("weapon-final", "weapon-final", CatalogKind.Weapon, "최종", null, "sword", 2, "weapon-root"),
            Entry("miracle-target", "miracle-target", CatalogKind.Miracle, "기적")
        };
        var entities = entries.Select(x => new GameEntityDescriptor(x.GameKey, x.Kind, x.KoreanName, x.Rarity, x.Category, x.Tier, x.ParentGameKey)).ToArray();
        return (new BuildReviewSession(build, new VersionedCatalog("1.0.33", entries)), entities);
    }

    private static CatalogEntry Entry(string slug, string key, CatalogKind kind, string name, string? rarity = null,
        string? category = null, int? tier = null, string? parent = null) => new()
    {
        Slug = slug, GameKey = key, Kind = kind, KoreanName = name, Rarity = rarity, Category = category, Tier = tier, ParentGameKey = parent
    };
}
