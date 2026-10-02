using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SephiriaBuildOverlay.Core.Updates;

// Linked into the independent preloader: no Core, Unity or JSON dependency there.
public static class UpdateProtocol
{
    public const int Protocol = 1;
    public const int MaxDllBytes = 4 * 1024 * 1024;
    public static IReadOnlyList<string> Files { get; } = Array.AsReadOnly(new[]
        { "SephiriaBuildOverlay.Core.dll", "SephiriaBuildOverlay.Plugin.dll" });

    public static Version ParseVersion(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Length > 9 ||
            (p.Length > 1 && p[0] == '0') || p.Any(c => c < '0' || c > '9')) ||
            !Version.TryParse(value, out var result)) throw new InvalidDataException("Invalid stable version.");
        return result;
    }

    public static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream));
    }
    public static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(bytes));
    }
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    public static bool ValidHash(string value) => value.Length == 64 && value.All(c =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    public static void ValidateAssembly(string path, string filename, Version version)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > MaxDllBytes) throw new InvalidDataException("DLL size limit exceeded.");
        var name = AssemblyName.GetAssemblyName(path); // Reads metadata; never loads executable code.
        if (name.Name != Path.GetFileNameWithoutExtension(filename) || name.Version !=
            new Version(version.Major, version.Minor, version.Build, 0))
            throw new InvalidDataException("DLL identity/version mismatch: " + filename);
    }

    public static void SafePath(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Updater does not follow junctions/symbolic links: " + current);
        }
    }

    public static string ReadSmall(string path)
    {
        SafePath(path);
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Update record size limit exceeded.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public static void WriteAtomic(string path, string text)
    {
        SafePath(path);
        var temp = path + ".writing";
        SafePath(temp);
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class UpdateRecord
{
    public string FromVersion { get; }
    public string ToVersion { get; }
    public IReadOnlyList<string> OldHashes { get; }
    public IReadOnlyList<string> NewHashes { get; }
    public UpdateRecord(string from, string to, IEnumerable<string> oldHashes, IEnumerable<string> newHashes)
    {
        if (UpdateProtocol.ParseVersion(to) <= UpdateProtocol.ParseVersion(from))
            throw new InvalidDataException("Updates must be newer than the installed version.");
        var oldArray = oldHashes.ToArray(); var newArray = newHashes.ToArray();
        if (oldArray.Length != 2 || newArray.Length != 2 ||
            oldArray.Concat(newArray).Any(h => !UpdateProtocol.ValidHash(h)))
            throw new InvalidDataException("Invalid update hashes.");
        FromVersion = from; ToVersion = to;
        OldHashes = Array.AsReadOnly(oldArray); NewHashes = Array.AsReadOnly(newArray);
    }
    public string Serialize() => string.Join("\n", new[] { "SBO-UPDATE-1", FromVersion, ToVersion,
        OldHashes[0], NewHashes[0], OldHashes[1], NewHashes[1] });
    public static UpdateRecord Parse(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length != 7 || lines[0] != "SBO-UPDATE-1") throw new InvalidDataException("Unsupported update protocol.");
        return new UpdateRecord(lines[1], lines[2], new[] { lines[3], lines[5] }, new[] { lines[4], lines[6] });
    }
}
