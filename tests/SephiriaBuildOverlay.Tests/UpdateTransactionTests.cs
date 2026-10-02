using SephiriaBuildOverlay.Core.Updates;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public sealed class UpdateTransactionTests
{
    [Fact]
    public void DisabledUpdaterKeepsPendingThenAppliesPairAndOneTimeReceipt()
    {
        using var f = new UpdateFixture(); f.Stage();
        Assert.Contains("disabled", f.Store.ApplyPending(false)); f.AssertInstalled(f.Old); Assert.True(f.Store.HasPending);
        Assert.Equal("Applied update 0.1.11", f.Store.ApplyPending(true)); f.AssertInstalled(f.New);
        Assert.False(f.Store.HasPending); Assert.Equal("applied\n0.1.11", f.Store.TakeReceipt()); Assert.Null(f.Store.TakeReceipt());
    }

    [Fact]
    public void SecondReplacementFailureRollsBackBothDlls()
    {
        using var f = new UpdateFixture(); f.Stage(); var calls = 0;
        var store = new UpdateStore(f.Root, (source, target) =>
        {
            if (++calls == 2) throw new IOException("simulated sharing violation"); File.Replace(source, target, null);
        });
        Assert.Throws<IOException>(() => store.ApplyPending(true)); f.AssertInstalled(f.Old);
        Assert.Equal("restored\n0.1.9", store.TakeReceipt()); Assert.False(store.HasPending);
    }

    [Fact]
    public void InterruptedReplacementAndRollbackRecoverOnNextLaunchEvenIfDisabled()
    {
        using var f = new UpdateFixture(); f.Stage(); var calls = 0;
        var interrupted = new UpdateStore(f.Root, (source, target) =>
        { if (++calls > 1) throw new IOException("process interrupted"); File.Replace(source, target, null); });
        Assert.Throws<IOException>(() => interrupted.ApplyPending(true));
        Assert.Equal(f.New[UpdateProtocol.Files[0]], File.ReadAllBytes(Path.Combine(f.Store.PluginDirectory, UpdateProtocol.Files[0])));
        Assert.Contains("Recovered interrupted", f.Store.ApplyPending(false)); f.AssertInstalled(f.Old);
        Assert.Equal("restored\n0.1.9", f.Store.TakeReceipt());
    }

    [Fact]
    public void ManualInstallationInvalidatesOldPendingUpdateWithoutOverwritingIt()
    {
        using var f = new UpdateFixture(); f.Stage();
        var manual = UpdateFixture.Dlls("0.1.12"); foreach (var file in manual) File.WriteAllBytes(Path.Combine(f.Store.PluginDirectory, file.Key), file.Value);
        Assert.Contains("discarded", f.Store.ApplyPending(true)); f.AssertInstalled(manual); Assert.False(f.Store.HasPending);
    }

    [Fact]
    public void RecoveryRefusesToOverwriteManualChangesOrInvalidBackups()
    {
        using var f = new UpdateFixture(); f.Stage(); var calls = 0;
        var interrupted = new UpdateStore(f.Root, (source, target) =>
        { if (++calls > 1) throw new IOException("interrupted"); File.Replace(source, target, null); });
        Assert.Throws<IOException>(() => interrupted.ApplyPending(true));
        var file = Path.Combine(f.Store.PluginDirectory, UpdateProtocol.Files[0]); File.WriteAllText(file, "manually changed");
        Assert.Throws<IOException>(() => f.Store.ApplyPending(false)); Assert.Equal("manually changed", File.ReadAllText(file));
        File.WriteAllBytes(file, f.New[UpdateProtocol.Files[0]]);
        File.WriteAllText(Path.Combine(f.Store.StateDirectory, "transaction", UpdateProtocol.Files[1]), "corrupt backup");
        Assert.Throws<IOException>(() => f.Store.ApplyPending(true));
    }

    [Fact]
    public void CorruptStagedDllFailsBeforeChangingAnyInstalledFile()
    {
        using var f = new UpdateFixture(); f.Stage();
        File.WriteAllText(Path.Combine(f.Store.PendingDirectory, UpdateProtocol.Files[1]), "broken");
        Assert.Throws<InvalidDataException>(() => f.Store.ApplyPending(true)); f.AssertInstalled(f.Old);
    }

    [Fact]
    public void WrongAssemblyIdentityAndVersionNeverPublishPendingDirectory()
    {
        using var f = new UpdateFixture();
        var wrong = new Dictionary<string, byte[]>(f.New) { [UpdateProtocol.Files[0]] = f.New[UpdateProtocol.Files[1]] };
        Assert.Throws<InvalidDataException>(() => f.Store.Stage("0.1.9", "0.1.11", wrong));
        Assert.Throws<InvalidDataException>(() => f.Store.Stage("0.1.9", "0.1.12", f.New));
        Assert.False(f.Store.HasPending); Assert.Empty(Directory.GetDirectories(f.Store.StateDirectory)); f.AssertInstalled(f.Old);
    }

    [Fact]
    public void ConcurrentLockAndCancelledStagingFailSafely()
    {
        using var f = new UpdateFixture(); Directory.CreateDirectory(f.Store.StateDirectory);
        using (var lease = new FileStream(Path.Combine(f.Store.StateDirectory, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => f.Stage());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => f.Store.Stage("0.1.9", "0.1.11", f.New, cancellation.Token));
        Assert.False(f.Store.HasPending); f.AssertInstalled(f.Old);
    }

    [Fact]
    public void CommittedJournalRecoversWithoutRollingBackSuccessfulUpdate()
    {
        using var f = new UpdateFixture(); f.Stage();
        var recordText = File.ReadAllText(Path.Combine(f.Store.PendingDirectory, "pending.txt"));
        var tx = Path.Combine(f.Store.StateDirectory, "transaction"); Directory.CreateDirectory(tx);
        foreach (var file in UpdateProtocol.Files)
        {
            File.WriteAllBytes(Path.Combine(tx, file), f.Old[file]);
            File.WriteAllBytes(Path.Combine(f.Store.PluginDirectory, file), f.New[file]);
        }
        File.WriteAllText(Path.Combine(tx, "journal.txt"), recordText); File.WriteAllText(Path.Combine(tx, "committed.txt"), recordText);
        Assert.Contains("Recovered committed", f.Store.ApplyPending(true)); f.AssertInstalled(f.New); Assert.False(f.Store.HasPending);
        Assert.Equal("applied\n0.1.11", f.Store.TakeReceipt());
    }

    [Fact]
    public void ProtocolRejectsUnknownVersionsAndOversizedRecords()
    {
        Assert.Throws<InvalidDataException>(() => UpdateRecord.Parse("SBO-UPDATE-2\nanything"));
        Assert.Throws<InvalidDataException>(() => new UpdateRecord("0.1.11", "0.1.9", new[] { new string('0', 64), new string('0', 64) }, new[] { new string('0', 64), new string('0', 64) }));
        using var f = new UpdateFixture(); f.Stage();
        File.WriteAllText(Path.Combine(f.Store.PendingDirectory, "pending.txt"), new string('x', 4097));
        Assert.Throws<InvalidDataException>(() => f.Store.ApplyPending(true)); f.AssertInstalled(f.Old);
    }

    [Fact]
    public void InvalidDownloadCanBeDiscardedButActiveRecoveryCannot()
    {
        using var f = new UpdateFixture(); f.Stage(); f.Store.DiscardInvalidPending(); Assert.False(f.Store.HasPending);
        f.Stage(); var calls = 0;
        var interrupted = new UpdateStore(f.Root, (source, target) =>
        { if (++calls > 1) throw new IOException("interrupted"); File.Replace(source, target, null); });
        Assert.Throws<IOException>(() => interrupted.ApplyPending(true));
        Assert.Throws<IOException>(() => f.Store.DiscardInvalidPending()); Assert.True(f.Store.HasUnfinishedTransaction);
    }
}
