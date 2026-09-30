using System.Net;
using System.Text;
using SephiriaBuildOverlay.Core.Import;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Tests;

public sealed class BuildLocatorTests
{
    private const string Id = "c4fa31fc-5b95-4e04-a08b-dfb752f3bf21";

    [Theory]
    [InlineData(Id)]
    [InlineData("https://sephiria.wiki/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21")]
    [InlineData("https://www.sephiria.wiki/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21")]
    public void AcceptsOnlySupportedForms(string text)
    {
        Assert.True(BuildLocator.TryParse(text, out var locator, out _));
        Assert.Equal(Guid.Parse(Id), locator!.Id);
    }

    [Theory]
    [InlineData("http://sephiria.wiki/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21")]
    [InlineData("https://evil.example/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21")]
    [InlineData("https://user@sephiria.wiki/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21")]
    [InlineData("https://sephiria.wiki:8443/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21")]
    [InlineData("https://sephiria.wiki/api/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21")]
    [InlineData("https://sephiria.wiki/builds/c4fa31fc-5b95-4e04-a08b-dfb752f3bf21?x=1")]
    [InlineData("{c4fa31fc-5b95-4e04-a08b-dfb752f3bf21}")]
    [InlineData("")]
    public void RejectsUnsupportedForms(string text) => Assert.False(BuildLocator.TryParse(text, out _, out _));
}

public sealed class WikiBuildSourceTests : IDisposable
{
    [Fact]
    public async Task RecordedPublicBuildPreservesActualCountsAndAllSections()
    {
        // Public API response, 2026-09-30. Descriptive prose was removed from the fixture.
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wiki-c4fa31fc.json"));
        using var source = Source(new StubHandler(_ => JsonResponse(json)));
        var result = await source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D")));
        Assert.Equal(5, result.Build.Sections.Count);
        var items = result.Build.Sections.SelectMany(section => section.Items).ToArray();
        Assert.Equal(49, items.Length);
        Assert.Equal(4, items.Count(item => item.Slug == "unalloyed_gold_needle"));
        Assert.Equal(2, items.Count(item => item.Slug == "blue_claws"));
        Assert.Equal(49, items.Select(item => item.InstanceId).Distinct().Count());
        var review = new SephiriaBuildOverlay.Core.Review.BuildReviewSession(result.Build,
            SephiriaBuildOverlay.Core.Catalog.VersionedCatalog.LoadEmbedded());
        Assert.False(review.ValidateForActivation("1.0.33").CanActivate);
        Assert.All(items, item => Assert.NotNull(SephiriaBuildOverlay.Core.Catalog.VersionedCatalog.LoadEmbedded().FindBySlug(item.Slug)));
    }

    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "sbo-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly Guid BuildId = Guid.Parse("c4fa31fc-5b95-4e04-a08b-dfb752f3bf21");

    [Fact]
    public async Task RequestsOnlyFixedPublicApiWithoutUserCredentials()
    {
        using var source = Source(new StubHandler(request =>
        {
            Assert.Equal($"https://www.sephiria.wiki/api/builds/{BuildId:D}", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return JsonResponse(ExampleJson());
        }));
        await source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D")));
    }

    [Fact]
    public async Task CacheWriteFailureDoesNotDiscardFreshSuccessfulResponse()
    {
        Directory.CreateDirectory(_cacheDirectory);
        var path = Path.Combine(_cacheDirectory, "not-a-directory");
        await File.WriteAllTextAsync(path, "test");
        using var source = new WikiBuildSource(new HttpClient(new StubHandler(_ => JsonResponse(ExampleJson()))), new BuildCache(path));
        var result = await source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D")));
        Assert.Equal(ImportOrigin.Network, result.Origin);
        Assert.Contains("캐시 저장 실패", result.Warning);
    }

    [Fact]
    public async Task RejectsStreamingResponseWithoutContentLengthAtByteLimit()
    {
        using var source = Source(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(new byte[WikiBuildSource.MaximumResponseBytes + 1]))
        }));
        var error = await Assert.ThrowsAsync<BuildImportException>(() => source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D"))));
        Assert.Contains("2 MiB", error.Message);
        Assert.False(Directory.Exists(_cacheDirectory));
    }

    [Fact]
    public async Task ParsesSyntheticResponseShapeAndPreservesEveryDuplicateOccurrence()
    {
        var handler = new StubHandler(_ => JsonResponse(ExampleJson()));
        using var source = Source(handler);
        var result = await source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D")));

        Assert.Equal(ImportOrigin.Network, result.Origin);
        Assert.Equal("1.0.33", result.Build.GameVersion);
        Assert.Equal(5, result.Build.Sections.SelectMany(x => x.Items).Count(x => x.Slug == "unalloyed_gold_needle"));
        Assert.Equal(2, result.Build.Sections.SelectMany(x => x.Items).Count(x => x.Slug == "blue_claws"));
        Assert.Equal(20, result.Build.Talents["anger"]);
    }

    [Fact]
    public async Task FallsBackToLastSuccessfulCacheAndReportsAge()
    {
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        using (var online = new WikiBuildSource(new HttpClient(new StubHandler(_ => JsonResponse(ExampleJson()))), new BuildCache(_cacheDirectory), () => now))
            await online.ImportAsync(BuildLocator.Parse(BuildId.ToString("D")));

        using var offline = new WikiBuildSource(new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline"))),
            new BuildCache(_cacheDirectory), () => now.AddHours(3));
        var result = await offline.ImportAsync(BuildLocator.Parse(BuildId.ToString("D")));

        Assert.Equal(ImportOrigin.Cache, result.Origin);
        Assert.Contains("3시간", result.Warning);
        Assert.Contains("offline", result.Warning);
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*.tmp"));
    }

    [Fact]
    public async Task RejectsOversizeResponseWithoutCachingIt()
    {
        var oversized = new string('x', WikiBuildSource.MaximumResponseBytes + 1);
        using var source = Source(new StubHandler(_ => JsonResponse(oversized)));
        var error = await Assert.ThrowsAsync<BuildImportException>(() => source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D"))));
        Assert.Contains("2 MiB", error.Message);
    }

    [Fact]
    public async Task ReportsSchemaFailureAndCorruptCache()
    {
        Directory.CreateDirectory(_cacheDirectory);
        File.WriteAllText(Path.Combine(_cacheDirectory, $"build-{BuildId:D}.json"), "not-json");
        using var source = Source(new StubHandler(_ => JsonResponse("{\"data\":{}}")));
        var error = await Assert.ThrowsAsync<BuildImportException>(() => source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D"))));
        Assert.Contains("모두 실패", error.Message);
    }

    [Fact]
    public async Task TreatsInternalCancellationAsTimeoutAndUsesCache()
    {
        var cache = new BuildCache(_cacheDirectory);
        await cache.SaveAsync(BuildId, ExampleJson(), DateTimeOffset.UtcNow, CancellationToken.None);
        using var source = Source(new StubHandler(_ => throw new OperationCanceledException("slow")));
        var result = await source.ImportAsync(BuildLocator.Parse(BuildId.ToString("D")));
        Assert.Equal(ImportOrigin.Cache, result.Origin);
        Assert.Contains("10초", result.Warning);
    }

    private WikiBuildSource Source(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new BuildCache(_cacheDirectory), () => DateTimeOffset.UtcNow);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string ExampleJson() => """
    {"data":{"postUuid":"$BUILD_ID$","title":"synthetic 1.0.33 example","version":"1.0.33",
    "weapon":"eternal_snow_silence","miracle":"explorer","costume":"wings_lost_bat","combo":["glacier","precision"],
    "content":[
      {"label":"start","description":"","items":[{"id":"1","value":"blue_claws"},{"id":"2","value":"blue_claws"}]},
      {"label":"glacier","description":"x5","items":[
        {"id":"3","value":"unalloyed_gold_needle"},{"id":"4","value":"unalloyed_gold_needle"},
        {"id":"5","value":"unalloyed_gold_needle"},{"id":"6","value":"unalloyed_gold_needle"},{"id":"7","value":"unalloyed_gold_needle"}]}
    ],"artifact_values":["blue_claws","unalloyed_gold_needle"],
    "ability":{"base":10,"will":20,"anger":20,"rapid":0,"wisdom":0,"patience":0,"survival":0}}}
    """.Replace("$BUILD_ID$", BuildId.ToString("D"), StringComparison.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] data) : base(data) { }
        public override bool CanSeek => false;
    }
}
