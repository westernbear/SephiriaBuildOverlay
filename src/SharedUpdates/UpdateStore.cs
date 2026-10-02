namespace SephiriaBuildOverlay.Core.Updates;

// Journal is durable BEFORE replacing either DLL. Recovery always restores the pair.
public sealed class UpdateStore
{
    public string PluginDirectory { get; }
    public string StateDirectory { get; }
    public string PendingDirectory => Path.Combine(StateDirectory, "pending");
    private string TransactionDirectory => Path.Combine(StateDirectory, "transaction");
    private string Journal => Path.Combine(TransactionDirectory, "journal.txt");
    private readonly Action<string, string> _replace;

    public UpdateStore(string bepinexRoot, Action<string, string>? replace = null)
    {
        var root = Path.GetFullPath(bepinexRoot);
        PluginDirectory = Path.Combine(root, "plugins", "SephiriaBuildOverlay");
        StateDirectory = Path.Combine(root, "cache", "SephiriaBuildOverlayUpdater");
        _replace = replace ?? ((source, target) => File.Replace(source, target, null));
    }

    private void CheckPaths()
    {
        UpdateProtocol.SafePath(PluginDirectory);
        UpdateProtocol.SafePath(StateDirectory);
        foreach (var file in UpdateProtocol.Files) UpdateProtocol.SafePath(Path.Combine(PluginDirectory, file));
    }
    private FileStream Lock()
    {
        CheckPaths();
        Directory.CreateDirectory(StateDirectory);
        var path = Path.Combine(StateDirectory, "update.lock");
        UpdateProtocol.SafePath(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public bool HasPending => Directory.Exists(PendingDirectory);
    public bool HasUnfinishedTransaction => File.Exists(Journal);

    public void Stage(string from, string to, IReadOnlyDictionary<string, byte[]> dlls,
        CancellationToken cancellationToken = default)
    {
        using var lease = Lock();
        if (HasPending || File.Exists(Journal)) throw new IOException("An update is already pending recovery/application.");
        var work = Path.Combine(StateDirectory, "download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var oldHashes = new List<string>(); var newHashes = new List<string>();
            foreach (var file in UpdateProtocol.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var installed = Path.Combine(PluginDirectory, file);
                UpdateProtocol.ValidateAssembly(installed, file, UpdateProtocol.ParseVersion(from));
                oldHashes.Add(UpdateProtocol.Hash(installed));
                if (!dlls.TryGetValue(file, out var bytes) || bytes.Length > UpdateProtocol.MaxDllBytes)
                    throw new InvalidDataException("Missing/oversized update DLL.");
                var staged = Path.Combine(work, file);
                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                UpdateProtocol.ValidateAssembly(staged, file, UpdateProtocol.ParseVersion(to));
                newHashes.Add(UpdateProtocol.Hash(staged));
            }
            var record = new UpdateRecord(from, to, oldHashes, newHashes);
            UpdateProtocol.WriteAtomic(Path.Combine(work, "pending.txt"), record.Serialize());
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(work, PendingDirectory); // Publishes the complete pair, never a partial download.
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }

    // Called ONLY from the preloader, before either owned assembly is loaded.
    public string ApplyPending(bool enabled)
    {
        using var lease = Lock();
        if (File.Exists(Journal))
        {
            var interrupted = UpdateRecord.Parse(UpdateProtocol.ReadSmall(Journal));
            var committed = Path.Combine(TransactionDirectory, "committed.txt");
            UpdateProtocol.SafePath(committed);
            if (File.Exists(committed) && UpdateProtocol.ReadSmall(committed) == interrupted.Serialize() &&
                InstalledHashesMatch(interrupted.NewHashes))
            {
                WriteReceipt("applied", interrupted.ToVersion);
                Cleanup();
                return "Recovered committed update " + interrupted.ToVersion;
            }
            Rollback(interrupted); // Also recover when updates have since been disabled.
            WriteReceipt("restored", interrupted.FromVersion);
            Cleanup();
            return "Recovered interrupted update; restored " + interrupted.FromVersion;
        }
        if (!enabled || !HasPending) return enabled ? "No pending update." : "Automatic updates disabled.";
        var record = UpdateRecord.Parse(UpdateProtocol.ReadSmall(Path.Combine(PendingDirectory, "pending.txt")));
        if (!InstalledHashesMatch(record.OldHashes))
        {
            WriteReceipt("skipped", record.ToVersion);
            DeleteOwnedDirectory(PendingDirectory); // Manual installs must never be overwritten by an old download.
            return "Pending update discarded: installed DLLs changed.";
        }
        for (var i = 0; i < UpdateProtocol.Files.Count; i++)
        {
            var file = UpdateProtocol.Files[i]; var staged = Path.Combine(PendingDirectory, file);
            UpdateProtocol.SafePath(staged);
            if (UpdateProtocol.Hash(staged) != record.NewHashes[i]) throw new InvalidDataException("Staged DLL hash mismatch.");
            UpdateProtocol.ValidateAssembly(staged, file, UpdateProtocol.ParseVersion(record.ToVersion));
            UpdateProtocol.ValidateAssembly(Path.Combine(PluginDirectory, file), file, UpdateProtocol.ParseVersion(record.FromVersion));
        }
        DeleteOwnedDirectory(TransactionDirectory); // No journal: abandoned pre-transaction backups only.
        Directory.CreateDirectory(TransactionDirectory);
        foreach (var file in UpdateProtocol.Files)
        {
            var backup = Path.Combine(TransactionDirectory, file);
            CopyDurable(Path.Combine(PluginDirectory, file), backup);
        }
        if (!BackupHashesMatch(record.OldHashes)) throw new IOException("Backup verification failed.");
        UpdateProtocol.WriteAtomic(Journal, record.Serialize());
        try
        {
            foreach (var file in UpdateProtocol.Files)
                ReplaceFrom(Path.Combine(PendingDirectory, file), Path.Combine(PluginDirectory, file));
            if (!InstalledHashesMatch(record.NewHashes)) throw new IOException("Installed update verification failed.");
            UpdateProtocol.WriteAtomic(Path.Combine(TransactionDirectory, "committed.txt"), record.Serialize());
        }
        catch
        {
            // If rollback is itself interrupted, the journal remains for the next launch.
            Rollback(record);
            WriteReceipt("restored", record.FromVersion);
            Cleanup();
            throw;
        }
        WriteReceipt("applied", record.ToVersion);
        Cleanup();
        return "Applied update " + record.ToVersion;
    }

    public void DiscardInvalidPending()
    {
        using var lease = Lock();
        if (File.Exists(Journal)) throw new IOException("Cannot discard an unfinished transaction.");
        DeleteOwnedDirectory(PendingDirectory);
    }

    public string? TakeReceipt()
    {
        using var lease = Lock();
        var path = Path.Combine(StateDirectory, "result.txt");
        if (!File.Exists(path)) return null;
        var value = UpdateProtocol.ReadSmall(path);
        File.Delete(path);
        return value;
    }
    private void WriteReceipt(string state, string version) =>
        UpdateProtocol.WriteAtomic(Path.Combine(StateDirectory, "result.txt"), state + "\n" + version);
    private bool InstalledHashesMatch(IReadOnlyList<string> hashes) => UpdateProtocol.Files.Select((f, i) =>
        File.Exists(Path.Combine(PluginDirectory, f)) && UpdateProtocol.Hash(Path.Combine(PluginDirectory, f)) == hashes[i]).All(x => x);
    private bool BackupHashesMatch(IReadOnlyList<string> hashes) => UpdateProtocol.Files.Select((f, i) =>
        File.Exists(Path.Combine(TransactionDirectory, f)) && UpdateProtocol.Hash(Path.Combine(TransactionDirectory, f)) == hashes[i]).All(x => x);
    private void Rollback(UpdateRecord record)
    {
        foreach (var file in UpdateProtocol.Files) UpdateProtocol.SafePath(Path.Combine(TransactionDirectory, file));
        if (!BackupHashesMatch(record.OldHashes)) throw new IOException("Recovery backup invalid; refusing unsafe replacement.");
        for (var i = 0; i < UpdateProtocol.Files.Count; i++)
        {
            var path = Path.Combine(PluginDirectory, UpdateProtocol.Files[i]);
            var hash = UpdateProtocol.Hash(path);
            if (hash != record.OldHashes[i] && hash != record.NewHashes[i])
                throw new IOException("Installed file changed outside the transaction; recovery requires manual repair.");
        }
        foreach (var file in UpdateProtocol.Files)
            ReplaceFrom(Path.Combine(TransactionDirectory, file), Path.Combine(PluginDirectory, file));
        if (!InstalledHashesMatch(record.OldHashes)) throw new IOException("Rollback verification failed.");
    }
    private void ReplaceFrom(string source, string target)
    {
        var temp = target + ".sbo-updating";
        UpdateProtocol.SafePath(temp);
        try { CopyDurable(source, temp); _replace(temp, target); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void CopyDurable(string source, string target)
    {
        UpdateProtocol.SafePath(source); UpdateProtocol.SafePath(target);
        using var input = File.OpenRead(source);
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output); output.Flush(true);
    }
    private void Cleanup()
    {
        // Remove journal last: interrupted cleanup must still be recoverable.
        DeleteOwnedDirectory(PendingDirectory);
        File.Delete(Journal);
        DeleteOwnedDirectory(TransactionDirectory);
    }
    private void DeleteOwnedDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        if (path != PendingDirectory && path != TransactionDirectory) throw new IOException("Not an owned update directory.");
        UpdateProtocol.SafePath(path);
        ValidateTree(path);
        Directory.Delete(path, true);
    }
    private static void ValidateTree(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            UpdateProtocol.SafePath(entry);
            if (Directory.Exists(entry)) ValidateTree(entry);
        }
    }
}
