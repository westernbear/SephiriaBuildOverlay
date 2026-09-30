namespace SephiriaBuildOverlay.Core.Models;

public sealed class BuildLocator
{
    private BuildLocator(Guid id) => Id = id;

    public Guid Id { get; }

    public static bool TryParse(string? value, out BuildLocator? locator, out string? error)
    {
        locator = null;
        error = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "빌드 UUID 또는 URL을 입력하세요.";
            return false;
        }

        if (Guid.TryParseExact(text, "D", out var rawId))
        {
            locator = new BuildLocator(rawId);
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "HTTPS Sephiria Wiki URL 또는 원시 UUID만 허용됩니다.";
            return false;
        }

        if (!string.Equals(uri.Host, "sephiria.wiki", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "www.sephiria.wiki", StringComparison.OrdinalIgnoreCase))
        {
            error = "sephiria.wiki 도메인만 허용됩니다.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            error = "빌드 URL에 사용자 정보나 별도 포트를 포함할 수 없습니다.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "빌드 URL에는 쿼리나 프래그먼트를 포함할 수 없습니다.";
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length != 2 || !string.Equals(segments[0], "builds", StringComparison.Ordinal) ||
            !Guid.TryParseExact(segments[1], "D", out var urlId))
        {
            error = "URL 형식은 https://sephiria.wiki/builds/{UUID} 이어야 합니다.";
            return false;
        }

        locator = new BuildLocator(urlId);
        return true;
    }

    public static BuildLocator Parse(string value)
    {
        if (!TryParse(value, out var locator, out var error)) throw new FormatException(error);
        return locator!;
    }

    public override string ToString() => Id.ToString("D");
}

public enum ImportOrigin
{
    Network,
    Cache
}

public sealed class BuildImportResult
{
    public BuildImportResult(ImportedBuild build, ImportOrigin origin, DateTimeOffset retrievedAt, string? warning = null)
    {
        Build = build;
        Origin = origin;
        RetrievedAt = retrievedAt;
        Warning = warning;
    }

    public ImportedBuild Build { get; }
    public ImportOrigin Origin { get; }
    public DateTimeOffset RetrievedAt { get; }
    public TimeSpan Age => DateTimeOffset.UtcNow - RetrievedAt;
    public string? Warning { get; }
}

public interface IBuildSource
{
    Task<BuildImportResult> ImportAsync(BuildLocator locator, CancellationToken cancellationToken = default);
}

public sealed class BuildImportException : Exception
{
    public BuildImportException(string message, Exception? inner = null) : base(message, inner) { }
}
