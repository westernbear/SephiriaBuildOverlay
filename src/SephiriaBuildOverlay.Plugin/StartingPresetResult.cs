namespace SephiriaBuildOverlay.Plugin;

internal sealed class StartingPresetResult
{
    public StartingPresetResult(string detail, bool applied = false, string? warning = null)
    { Detail = detail; Applied = applied; Warning = warning; }
    public string Detail { get; }
    public bool Applied { get; }
    public string? Warning { get; }
}
