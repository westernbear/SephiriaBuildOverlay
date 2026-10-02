using System.IO.Compression;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SephiriaBuildOverlay.Core.Updates;

public sealed class UpdateRelease
{
    public string Version { get; }
    public string ArchiveName => "SephiriaBuildOverlay-" + Version + ".zip";
    public string ChecksumName => "SephiriaBuildOverlay-" + Version + ".sha256";
    public string Digest { get; }
    public int Size { get; }
    public Uri Download(string name) => new(AutoUpdateClient.ReleaseRoot + "v" + Version + "/" + name);
    public UpdateRelease(string version, string digest, int size)
    {
        UpdateProtocol.ParseVersion(version);
        if (!UpdateProtocol.ValidHash(digest) || size <= 0 || size > AutoUpdateClient.MaxArchiveBytes)
            throw new InvalidDataException("Invalid release digest/size.");
        Version = version; Digest = digest; Size = size;
    }
}

public sealed class AutoUpdateClient : IDisposable
{
    public const string Repository = "westernbear/SephiriaBuildOverlay";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string ReleaseRoot = "https://github.com/" + Repository + "/releases/download/";
    public const int MaxArchiveBytes = 8 * 1024 * 1024;
    public const int MaxMetadataBytes = 256 * 1024;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _timeout;
    public AutoUpdateClient(HttpClient? client = null, TimeSpan? timeout = null)
    {
        _ownsClient = client == null;
        _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        _timeout = timeout ?? TimeSpan.FromSeconds(45);
    }

    public async Task<string?> StageLatestAsync(string installedVersion, UpdateStore store, CancellationToken token)
    {
        UpdateProtocol.ParseVersion(installedVersion);
        if (store.HasPending) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_timeout);
        try
        {
            var metadata = await DownloadAsync(new Uri(LatestUrl), MaxMetadataBytes, deadline.Token).ConfigureAwait(false);
            var release = ParseRelease(Encoding.UTF8.GetString(metadata), installedVersion);
            if (release == null) return null;
            var checksum = Encoding.ASCII.GetString(await DownloadAsync(release.Download(release.ChecksumName),
                1024, deadline.Token).ConfigureAwait(false)).Trim();
            if (checksum != release.Digest + "  " + release.ArchiveName)
                throw new InvalidDataException("Release checksum does not match GitHub's SHA-256 digest.");
            var archive = await DownloadAsync(release.Download(release.ArchiveName), MaxArchiveBytes, deadline.Token).ConfigureAwait(false);
            if (archive.Length != release.Size || UpdateProtocol.Hash(archive) != release.Digest)
                throw new InvalidDataException("Release archive size/hash mismatch.");
            var dlls = ExtractOwnedDlls(archive, release.Version, deadline.Token);
            store.Stage(installedVersion, release.Version, dlls, deadline.Token);
            return release.Version;
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Automatic update timed out; installed files were not changed.", ex); }
    }

    public static UpdateRelease? ParseRelease(string json, string installedVersion)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxMetadataBytes) throw new InvalidDataException("Release metadata too large.");
        using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16, DateParseHandling = DateParseHandling.None };
        var data = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("Unexpected trailing release data.");
        if (data["draft"]?.Type != JTokenType.Boolean || data["prerelease"]?.Type != JTokenType.Boolean)
            throw new InvalidDataException("Release schema mismatch.");
        if (data.Value<bool>("draft") || data.Value<bool>("prerelease")) return null;
        var tag = data.Value<string>("tag_name") ?? "";
        if (!tag.StartsWith("v", StringComparison.Ordinal)) throw new InvalidDataException("Invalid release tag.");
        var version = tag.Substring(1);
        if (UpdateProtocol.ParseVersion(version) <= UpdateProtocol.ParseVersion(installedVersion)) return null;
        if (data["assets"] is not JArray assets || assets.Count > 20) throw new InvalidDataException("Release assets schema mismatch.");
        var archiveName = "SephiriaBuildOverlay-" + version + ".zip";
        var checksumName = "SephiriaBuildOverlay-" + version + ".sha256";
        var archive = Asset(assets, archiveName, version);
        var checksum = Asset(assets, checksumName, version);
        if (checksum.Value<long?>("size") is not long checkSize || checkSize <= 0 || checkSize > 1024)
            throw new InvalidDataException("Invalid checksum size.");
        var digest = archive.Value<string>("digest") ?? "";
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal)) throw new InvalidDataException("Missing GitHub SHA-256 digest.");
        var size = archive.Value<long?>("size") ?? 0;
        if (size > MaxArchiveBytes || size <= 0) throw new InvalidDataException("Archive size limit exceeded.");
        return new UpdateRelease(version, digest.Substring(7), (int)size);
    }
    private static JObject Asset(JArray assets, string name, string version)
    {
        var matches = assets.OfType<JObject>().Where(a => a.Value<string>("name") == name).ToArray();
        if (matches.Length != 1 || matches[0].Value<string>("state") != "uploaded" ||
            matches[0].Value<string>("browser_download_url") != ReleaseRoot + "v" + version + "/" + name)
            throw new InvalidDataException("Missing, duplicate or foreign release asset: " + name);
        return matches[0];
    }

    public static bool AllowedDownloadUri(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        if (uri.AbsoluteUri == LatestUrl) return true;
        if (uri.Host == "github.com") return uri.Query.Length == 0 && uri.AbsoluteUri.StartsWith(ReleaseRoot, StringComparison.Ordinal);
        return (uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com") &&
            uri.AbsolutePath.StartsWith("/github-production-release-asset", StringComparison.Ordinal);
    }

    private async Task<byte[]> DownloadAsync(Uri uri, int limit, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            if (!AllowedDownloadUri(uri)) throw new InvalidDataException("Untrusted update URL/redirect.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("SephiriaBuildOverlay-Updater/1");
            request.Headers.Accept.ParseAdd(uri.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is Uri actual && !AllowedDownloadUri(actual))
                throw new InvalidDataException("Untrusted final update URL.");
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (response.Headers.Location == null || redirects == 3) throw new InvalidDataException("Invalid update redirect chain.");
                uri = new Uri(uri, response.Headers.Location); continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update response size limit exceeded.");
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (count == 0) return memory.ToArray();
                if (memory.Length + count > limit) throw new InvalidDataException("Update response size limit exceeded.");
                memory.Write(buffer, 0, count);
            }
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    public static IReadOnlyDictionary<string, byte[]> ExtractOwnedDlls(byte[] bytes, string version, CancellationToken token = default)
    {
        if (bytes.Length > MaxArchiveBytes) throw new InvalidDataException("Archive size limit exceeded.");
        using var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
        if (zip.Entries.Count > 128) throw new InvalidDataException("Too many archive entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (name.Length > 240 || name.Length == 0 || name.StartsWith("/", StringComparison.Ordinal) ||
                name.Contains('\\') || name.Contains(':') || name.Any(char.IsControl) ||
                name.TrimEnd('/').Split('/').Any(p => p == ".." || p == "." || p.Length == 0) ||
                !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Unsafe/duplicate archive entry.");
            total += entry.Length;
            if (entry.Length > 16 * 1024 * 1024 || total > 32 * 1024 * 1024)
                throw new InvalidDataException("Archive expansion limit exceeded.");
        }
        var protocol = zip.GetEntry("update-protocol.txt") ?? throw new InvalidDataException("Release requires manual installation: no update protocol.");
        if (Encoding.UTF8.GetString(ReadEntry(protocol, 128, token)) != "1\n" + version + "\n")
            throw new InvalidDataException("Unsupported release update protocol/version; install manually.");
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in UpdateProtocol.Files)
        {
            var entry = zip.GetEntry("BepInEx/plugins/SephiriaBuildOverlay/" + file) ??
                throw new InvalidDataException("Release missing owned DLL: " + file);
            result.Add(file, ReadEntry(entry, UpdateProtocol.MaxDllBytes, token));
        }
        return result;
    }
    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit, CancellationToken token)
    {
        if (entry.Length <= 0 || entry.Length > limit) throw new InvalidDataException("Archive entry size limit exceeded.");
        using var stream = entry.Open(); using var memory = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (memory.Length + read > limit) throw new InvalidDataException("Archive entry inflated beyond limit.");
            memory.Write(buffer, 0, read);
        }
        if (memory.Length != entry.Length) throw new InvalidDataException("Truncated archive entry.");
        return memory.ToArray();
    }
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
