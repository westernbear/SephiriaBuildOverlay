using System.Reflection;
using SephiriaBuildOverlay.Core.Updates;

// A reproducible staging probe; never replaces installed DLLs itself.
if (args.Length < 2 || (args[0] != "stage-package" && args[0] != "stage-latest"))
    throw new ArgumentException("stage-package <BepInExRoot> <release.zip> <targetVersion> | stage-latest <BepInExRoot>");
var store = new UpdateStore(args[1]);
var name = AssemblyName.GetAssemblyName(Path.Combine(store.PluginDirectory, UpdateProtocol.Files[1]));
var version = $"{name.Version!.Major}.{name.Version.Minor}.{name.Version.Build}";
if (args[0] == "stage-package")
{
    if (args.Length != 4) throw new ArgumentException("Expected release ZIP and version.");
    var bytes = File.ReadAllBytes(args[2]);
    var dlls = AutoUpdateClient.ExtractOwnedDlls(bytes, args[3]);
    store.Stage(version, args[3], dlls);
    Console.WriteLine($"Staged verified local package {version} -> {args[3]}; no installed files replaced.");
}
else
{
    using var client = new AutoUpdateClient();
    var updated = await client.StageLatestAsync(version, store, default);
    Console.WriteLine(updated == null ? "No newer release/pending already exists." : $"GitHub download verified and staged {version} -> {updated}; no installed files replaced.");
}
