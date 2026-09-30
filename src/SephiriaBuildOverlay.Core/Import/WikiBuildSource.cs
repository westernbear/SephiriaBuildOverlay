using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Import;

public sealed class WikiBuildSource : IBuildSource, IDisposable
{
    public const int MaximumResponseBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly Uri ApiRoot = new("https://www.sephiria.wiki/api/builds/");

    private readonly HttpClient _httpClient;
    private readonly BuildCache _cache;
    private readonly bool _ownsClient;
    private readonly Func<DateTimeOffset> _utcNow;

    public WikiBuildSource(HttpClient? httpClient = null, BuildCache? cache = null, Func<DateTimeOffset>? utcNow = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        _cache = cache ?? new BuildCache();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<BuildImportResult> ImportAsync(BuildLocator locator, CancellationToken cancellationToken = default)
    {
        if (locator is null) throw new ArgumentNullException(nameof(locator));

        Exception? networkFailure = null;
        try
        {
            var requestUri = new Uri(ApiRoot, locator.Id.ToString("D"));
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                throw new BuildImportException("Wiki 응답이 2 MiB 제한을 초과했습니다.");

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var json = await ReadLimitedAsync(stream, timeout.Token).ConfigureAwait(false);
            var build = WikiBuildParser.Parse(json, locator.Id);
            var now = _utcNow();
            string? cacheWarning = null;
            try { await _cache.SaveAsync(locator.Id, json, now, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cacheWarning = "최신 빌드 조회 성공. 캐시 저장 실패: " + ex.Message;
            }
            return new BuildImportResult(build, ImportOrigin.Network, now, cacheWarning);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            networkFailure = new TimeoutException("Wiki 요청이 10초 안에 완료되지 않았습니다.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is JsonException || ex is BuildImportException)
        {
            networkFailure = ex;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var cached = await _cache.TryLoadAsync(locator.Id, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                var age = _utcNow() - cached.RetrievedAt;
                var warning = $"Wiki 조회 실패: {networkFailure!.Message} 캐시 사용 중 (데이터 나이 {FormatAge(age)}).";
                return new BuildImportResult(cached.Build, ImportOrigin.Cache, cached.RetrievedAt, warning);
            }
        }
        catch (Exception cacheFailure) when (cacheFailure is IOException || cacheFailure is UnauthorizedAccessException || cacheFailure is JsonException)
        {
            throw new BuildImportException(
                $"Wiki 조회와 캐시 복구가 모두 실패했습니다. 네트워크: {networkFailure!.Message}; 캐시: {cacheFailure.Message}",
                new AggregateException(networkFailure!, cacheFailure));
        }

        throw new BuildImportException($"빌드를 가져올 수 없고 사용 가능한 캐시도 없습니다: {networkFailure!.Message}", networkFailure);
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }

    private static async Task<string> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaximumResponseBytes) throw new BuildImportException("Wiki 응답이 2 MiB 제한을 초과했습니다.");
            await memory.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalDays >= 1) return $"{(int)age.TotalDays}일";
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours}시간";
        return $"{Math.Max(0, (int)age.TotalMinutes)}분";
    }
}
