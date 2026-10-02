using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using SephiriaBuildOverlay.Core.Updates;
using Xunit;

namespace SephiriaBuildOverlay.Tests;

public sealed class UpdateFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "sbo-update-test-" + Guid.NewGuid().ToString("N"));
    public UpdateStore Store { get; }
    public Dictionary<string, byte[]> Old { get; } = Dlls("0.1.9");
    public Dictionary<string, byte[]> New { get; } = Dlls("0.1.11");
    public UpdateFixture()
    {
        Store = new UpdateStore(Root);
        Directory.CreateDirectory(Store.PluginDirectory);
        foreach (var file in Old) File.WriteAllBytes(Path.Combine(Store.PluginDirectory, file.Key), file.Value);
        Directory.CreateDirectory(Path.Combine(Root, "config"));
        File.WriteAllText(Path.Combine(Root, "config", "keep.cfg"), "user settings");
        File.WriteAllText(Path.Combine(Store.PluginDirectory, "OtherMod.dll"), "other mod");
    }
    public static Dictionary<string, byte[]> Dlls(string version) => UpdateProtocol.Files.ToDictionary(f => f, f =>
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(Path.GetFileNameWithoutExtension(f),
            new Version(version + ".0")), f, ModuleKind.Dll);
        using var memory = new MemoryStream(); assembly.Write(memory); return memory.ToArray();
    });
    public void Stage() => Store.Stage("0.1.9", "0.1.11", New);
    public void AssertInstalled(Dictionary<string, byte[]> expected)
    {
        foreach (var file in expected) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(Store.PluginDirectory, file.Key)));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(Root, "config", "keep.cfg")));
        Assert.Equal("other mod", File.ReadAllText(Path.Combine(Store.PluginDirectory, "OtherMod.dll")));
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    public byte[] Zip(Action<ZipArchive>? edit = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Add(zip, "update-protocol.txt", Encoding.UTF8.GetBytes("1\n0.1.11\n"));
            foreach (var file in New) Add(zip, "BepInEx/plugins/SephiriaBuildOverlay/" + file.Key, file.Value);
            Add(zip, "BepInEx/core/loader.dll", new byte[] { 1, 2, 3 });
            edit?.Invoke(zip);
        }
        return memory.ToArray();
    }
    public static void Add(ZipArchive zip, string name, byte[] bytes, int attrs = 0)
    { var entry = zip.CreateEntry(name); entry.ExternalAttributes = attrs; using var stream = entry.Open(); stream.Write(bytes); }
    public static JObject Metadata(byte[] zip, string version = "0.1.11") => new()
    {
        ["tag_name"] = "v" + version, ["draft"] = false, ["prerelease"] = false,
        ["assets"] = new JArray(new[] { ".zip", ".sha256" }.Select(ext => new JObject
        {
            ["name"] = "SephiriaBuildOverlay-" + version + ext,
            ["state"] = "uploaded", ["size"] = ext == ".zip" ? zip.Length : 100,
            ["digest"] = "sha256:" + UpdateProtocol.Hash(zip),
            ["browser_download_url"] = AutoUpdateClient.ReleaseRoot + "v" + version + "/SephiriaBuildOverlay-" + version + ext
        }))
    };
}

public sealed class AutoUpdateTests
{
    [Theory]
    [InlineData("0.1.9", "0.1.11", true)]
    [InlineData("0.1.11", "0.1.11", false)]
    [InlineData("0.1.12", "0.1.11", false)]
    [InlineData("1.2.0", "1.10.0", true)]
    public void VersionsCompareNumericallyAndNeverDowngrade(string installed, string latest, bool expected)
    { Assert.Equal(expected, AutoUpdateClient.ParseRelease(UpdateFixture.Metadata(new byte[100], latest).ToString(), installed) != null); }

    [Theory]
    [InlineData("01.1.1")][InlineData("1.0")][InlineData("1.2.3.4")][InlineData("1.0.1-beta")]
    [InlineData("../1.0.0")][InlineData("1.0.0+test")][InlineData("1. 0.0")][InlineData("9999999999.0.0")]
    public void RejectUnsafeOrUnstableVersions(string value) => Assert.Throws<InvalidDataException>(() => UpdateProtocol.ParseVersion(value));

    [Theory]
    [InlineData("http://github.com/westernbear/SephiriaBuildOverlay/releases/download/v1/test.zip")]
    [InlineData("https://github.com.evil.test/westernbear/SephiriaBuildOverlay/releases/download/v1/a.zip")]
    [InlineData("https://github.com/evil/SephiriaBuildOverlay/releases/download/v1/a.zip")]
    [InlineData("https://user@github.com/westernbear/SephiriaBuildOverlay/releases/download/v1/a.zip")]
    [InlineData("https://github.com:444/westernbear/SephiriaBuildOverlay/releases/download/v1/a.zip")]
    [InlineData("https://api.github.com/repos/westernbear/SephiriaBuildOverlay/releases/latest?token=x")]
    [InlineData("https://release-assets.githubusercontent.com/anything")]
    [InlineData("file:///C:/bad.dll")]
    public void RejectForeignDownloadUrls(string value) => Assert.False(AutoUpdateClient.AllowedDownloadUri(new Uri(value)));

    [Fact]
    public void DraftsPrereleasesAndMissingDigestsAreNotAccepted()
    {
        var data = UpdateFixture.Metadata(new byte[100]);
        data["draft"] = true; Assert.Null(AutoUpdateClient.ParseRelease(data.ToString(), "0.1.9"));
        data["draft"] = false; data["prerelease"] = true; Assert.Null(AutoUpdateClient.ParseRelease(data.ToString(), "0.1.9"));
        data["prerelease"] = false; data["assets"]![0]!["digest"] = null;
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ParseRelease(data.ToString(), "0.1.9"));
    }

    [Fact]
    public void DuplicateAssetsForeignUrlsAndSchemaChangesFailClosed()
    {
        var data = UpdateFixture.Metadata(new byte[100]);
        ((JArray)data["assets"]!).Add(data["assets"]![0]!.DeepClone());
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ParseRelease(data.ToString(), "0.1.9"));
        data = UpdateFixture.Metadata(new byte[100]); data["assets"]![0]!["browser_download_url"] = "https://evil.test/a.zip";
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ParseRelease(data.ToString(), "0.1.9"));
        data = UpdateFixture.Metadata(new byte[100]); data.Remove("draft");
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ParseRelease(data.ToString(), "0.1.9"));
        Assert.ThrowsAny<Exception>(() => AutoUpdateClient.ParseRelease("{broken", "0.1.9"));
        Assert.ThrowsAny<Exception>(() => AutoUpdateClient.ParseRelease("{\"draft\":false,\"draft\":true}", "0.1.9"));
    }

    [Fact]
    public void ExtractsOnlyOwnedDllsNotLoaderConfigOrOtherMods()
    {
        using var fixture = new UpdateFixture();
        var dlls = AutoUpdateClient.ExtractOwnedDlls(fixture.Zip(z => UpdateFixture.Add(z, "BepInEx/config/evil.cfg", new byte[] { 1 })), "0.1.11");
        Assert.Equal(UpdateProtocol.Files.OrderBy(x => x), dlls.Keys.OrderBy(x => x));
    }

    [Theory]
    [InlineData("../outside.dll")][InlineData("/absolute.dll")][InlineData("C:/drive.dll")]
    [InlineData("BepInEx\\escape.dll")][InlineData("BepInEx/./escape.dll")][InlineData("x//file")]
    [InlineData("UPDATE-PROTOCOL.TXT")]
    public void RejectUnsafeAndCaseDuplicateZipEntries(string name)
    {
        using var fixture = new UpdateFixture();
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ExtractOwnedDlls(fixture.Zip(z => UpdateFixture.Add(z, name, new byte[] { 1 })), "0.1.11"));
    }

    [Fact]
    public void RejectLinksZipBombsAndIncompatibleProtocols()
    {
        using var fixture = new UpdateFixture();
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ExtractOwnedDlls(fixture.Zip(z => UpdateFixture.Add(z, "link", new byte[] { 1 }, unchecked((int)0xa1ff0000))), "0.1.11"));
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ExtractOwnedDlls(fixture.Zip(z => UpdateFixture.Add(z, "bomb", new byte[17 * 1024 * 1024])), "0.1.11"));
        Assert.Throws<InvalidDataException>(() => AutoUpdateClient.ExtractOwnedDlls(fixture.Zip(), "0.1.12"));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    { public override bool CanSeek => false; }

    [Fact]
    public async Task CompleteDownloadVerifiesBothChecksumsStagesButNeverReplacesLiveDlls()
    {
        using var fixture = new UpdateFixture(); var zip = fixture.Zip(); var requests = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var uri = request.RequestUri!.AbsoluteUri; requests.Add(uri);
            Assert.False(request.Headers.Contains("Authorization"));
            return Task.FromResult(uri == AutoUpdateClient.LatestUrl ? Response(Encoding.UTF8.GetBytes(UpdateFixture.Metadata(zip).ToString())) :
                uri.EndsWith(".sha256") ? Response(Encoding.ASCII.GetBytes(UpdateProtocol.Hash(zip) + "  SephiriaBuildOverlay-0.1.11.zip\n")) : Response(zip));
        }));
        using var client = new AutoUpdateClient(http);
        Assert.Equal("0.1.11", await client.StageLatestAsync("0.1.9", fixture.Store, default));
        Assert.Equal(3, requests.Count); fixture.AssertInstalled(fixture.Old); Assert.True(fixture.Store.HasPending);
        Assert.Null(await client.StageLatestAsync("0.1.9", fixture.Store, default)); Assert.Equal(3, requests.Count);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task CorruptChecksumOrArchiveNeverStages(bool corruptChecksum)
    {
        using var fixture = new UpdateFixture(); var zip = fixture.Zip();
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri == AutoUpdateClient.LatestUrl ?
            Response(Encoding.UTF8.GetBytes(UpdateFixture.Metadata(zip).ToString())) : request.RequestUri.AbsoluteUri.EndsWith(".sha256") ?
            Response(Encoding.ASCII.GetBytes((corruptChecksum ? new string('0', 64) : UpdateProtocol.Hash(zip)) + "  SephiriaBuildOverlay-0.1.11.zip")) : Response(new byte[zip.Length]))));
        using var client = new AutoUpdateClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.StageLatestAsync("0.1.9", fixture.Store, default));
        Assert.False(fixture.Store.HasPending); fixture.AssertInstalled(fixture.Old);
    }

    [Fact]
    public async Task TimeoutAndShutdownCancellationLeaveInstalledFilesUntouched()
    {
        using var fixture = new UpdateFixture();
        using var http = new HttpClient(new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response(Array.Empty<byte>()); }));
        using var client = new AutoUpdateClient(http, TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<TimeoutException>(() => client.StageLatestAsync("0.1.9", fixture.Store, default));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.StageLatestAsync("0.1.9", fixture.Store, stop.Token));
        fixture.AssertInstalled(fixture.Old); Assert.False(fixture.Store.HasPending);
    }

    [Fact]
    public async Task ResponseWithoutContentLengthIsStillBoundedAndUnsafeRedirectRejected()
    {
        using var fixture = new UpdateFixture();
        using var http = new HttpClient(new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new NonSeekableStream(new byte[AutoUpdateClient.MaxMetadataBytes + 1])) };
            Assert.Null(response.Content.Headers.ContentLength);
            return Task.FromResult(response);
        }));
        using var client = new AutoUpdateClient(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.StageLatestAsync("0.1.9", fixture.Store, default));
        using var redirectHttp = new HttpClient(new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://evil.test/a"); return Task.FromResult(response);
        }));
        using var redirectClient = new AutoUpdateClient(redirectHttp);
        await Assert.ThrowsAsync<InvalidDataException>(() => redirectClient.StageLatestAsync("0.1.9", fixture.Store, default));
        fixture.AssertInstalled(fixture.Old);
    }

    [Fact]
    public void PreloaderContractDoesNotReferenceCoreUnityOrWrongCecilVersion()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(AppContext.BaseDirectory, "SephiriaBuildOverlay.Updater.dll"));
        Assert.DoesNotContain(assembly.MainModule.AssemblyReferences, r => r.Name.StartsWith("SephiriaBuildOverlay.Core") || r.Name.StartsWith("UnityEngine") || r.Name == "Newtonsoft.Json");
        Assert.Equal(new Version(0, 10, 4, 0), assembly.MainModule.AssemblyReferences.Single(r => r.Name == "Mono.Cecil").Version);
        var patcher = assembly.MainModule.Types.Single(t => t.FullName == "SephiriaBuildOverlay.Updater.Patcher");
        Assert.True(patcher.Methods.Single(m => m.Name == "Initialize").IsPublic);
        Assert.True(patcher.Methods.Single(m => m.Name == "Patch").IsStatic);
        Assert.True(patcher.Properties.Single(p => p.Name == "TargetDLLs").GetMethod.IsPublic);
    }
}
