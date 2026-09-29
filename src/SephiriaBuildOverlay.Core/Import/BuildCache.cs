using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Import;

public sealed class BuildCache
{
    private readonly string _directory;

    public BuildCache(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SephiriaBuildOverlay");
    }

    public string DirectoryPath => _directory;

    public async Task SaveAsync(Guid id, string json, DateTimeOffset retrievedAt, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var dataPath = DataPath(id);
        var metadataPath = MetadataPath(id);
        await AtomicWriteAsync(dataPath, json, cancellationToken).ConfigureAwait(false);
        await AtomicWriteAsync(metadataPath, JsonConvert.SerializeObject(new CacheMetadata
        {
            BuildId = id,
            RetrievedAtUtc = retrievedAt.UtcDateTime
        }), cancellationToken).ConfigureAwait(false);
    }

    public async Task<CachedBuild?> TryLoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var dataPath = DataPath(id);
        if (!File.Exists(dataPath)) return null;

        var json = await ReadTextAsync(dataPath, cancellationToken).ConfigureAwait(false);
        DateTimeOffset retrievedAt;
        try
        {
            var metadataJson = await ReadTextAsync(MetadataPath(id), cancellationToken).ConfigureAwait(false);
            var metadata = JsonConvert.DeserializeObject<CacheMetadata>(metadataJson)
                ?? throw new JsonException("캐시 메타데이터가 비어 있습니다.");
            if (metadata.BuildId != id) throw new JsonException("캐시 ID가 일치하지 않습니다.");
            retrievedAt = new DateTimeOffset(DateTime.SpecifyKind(metadata.RetrievedAtUtc, DateTimeKind.Utc));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            retrievedAt = File.GetLastWriteTimeUtc(dataPath);
        }

        // Validate before returning so corrupt cache can never look successful.
        var build = WikiBuildParser.Parse(json, id);
        return new CachedBuild(build, retrievedAt);
    }

    private string DataPath(Guid id) => Path.Combine(_directory, $"build-{id:D}.json");
    private string MetadataPath(Guid id) => Path.Combine(_directory, $"build-{id:D}.meta.json");

    private static async Task AtomicWriteAsync(string destination, string contents, CancellationToken cancellationToken)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                await writer.WriteAsync(contents).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(destination))
                File.Replace(temporary, destination, null);
            else
                File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        var buffer = new char[4096];
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0) break;
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private sealed class CacheMetadata
    {
        public Guid BuildId { get; set; }
        public DateTime RetrievedAtUtc { get; set; }
    }
}

public sealed class CachedBuild
{
    public CachedBuild(ImportedBuild build, DateTimeOffset retrievedAt)
    {
        Build = build;
        RetrievedAt = retrievedAt;
    }

    public ImportedBuild Build { get; }
    public DateTimeOffset RetrievedAt { get; }
}
