using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SephiriaBuildOverlay.Core.Runtime;

namespace SephiriaBuildOverlay.Plugin;

// Opt-in local, read-only test interface. No expression evaluator, arbitrary
// reflection target, network listener, or action/confirm command is exposed.
internal sealed class RuntimeDiagnostics
{
    private readonly string _directory;
    private Guid _lastRequest;
    private float _nextRead;
    public RuntimeDiagnostics(string directory) => _directory = directory;

    public void Tick(float now, UnityGameGateway gateway, Func<object> overlayState)
    {
        if (now < _nextRead) return;
        _nextRead = now + .5f;
        var path = Path.Combine(_directory, "request.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024) return;
            var request = JObject.Parse(File.ReadAllText(path));
            if (!Guid.TryParse((string?)request["id"], out var id) || id == Guid.Empty || id == _lastRequest) return;
            _lastRequest = id;
            object result;
            var command = (string?)request["command"];
            if (command == "snapshot")
            {
                var snapshot = gateway.CaptureOnMainThread(freshDiscovery: true);
                result = new { snapshot, overlay = overlayState(), board = gateway.ReadBoardDiagnostics() };
            }
            else if (command == "catalog")
                result = new { entities = gateway.DiscoverCatalogEntities() };
            else if (command == "preview")
            {
                gateway.CaptureOnMainThread(freshDiscovery: true);
                result = new { previewShown = gateway.PreviewGhostRendering(), gameActionsAllowed = false, durationSeconds = 10 };
            }
            else result = new { error = "Only snapshot/catalog/UI-only preview commands are allowed." };
            Directory.CreateDirectory(_directory);
            var output = Path.Combine(_directory, id.ToString("D") + ".json");
            var temporary = output + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(new { id, command, utc = DateTime.UtcNow, result }, Formatting.Indented));
            if (File.Exists(output)) File.Replace(temporary, output, null); else File.Move(temporary, output);
        }
        catch (Exception)
        {
            // A half-written request is retried on the next poll; never interrupt
            // the game's Update loop because an external test process disappeared.
        }
    }
}
