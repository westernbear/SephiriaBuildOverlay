using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Runtime;

public enum ArtifactProgressEvent
{
    RewardAcquired,
    AltarEnhanced
}

public sealed class ArtifactProgress
{
    [JsonProperty("confirmedAcquisitions")]
    public int ConfirmedAcquisitions { get; set; }

    [JsonProperty("uncertainMinimum")]
    public int UncertainMinimum { get; set; }

    [JsonProperty("userAdjustment")]
    public int UserAdjustment { get; set; }

    [JsonIgnore]
    public int EffectiveAcquisitions => Math.Max(0, ConfirmedAcquisitions + UncertainMinimum + UserAdjustment);

    [JsonIgnore]
    public bool IsUncertain => UncertainMinimum > 0;
}

public sealed class ActiveBuildState
{
    [JsonProperty("runId")]
    public string RunId { get; private set; } = string.Empty;

    [JsonProperty("sourceBuildId")]
    public Guid SourceBuildId { get; private set; }

    [JsonProperty("artifacts")]
    public Dictionary<string, ArtifactProgress> Artifacts { get; private set; } = new(StringComparer.Ordinal);

    [JsonProperty("miracleAcquired")]
    public bool MiracleAcquired { get; private set; }

    [JsonConstructor]
    private ActiveBuildState() { }

    public ActiveBuildState(string runId, Guid sourceBuildId)
    {
        RunId = !string.IsNullOrWhiteSpace(runId) ? runId : throw new ArgumentException("runId is required", nameof(runId));
        SourceBuildId = sourceBuildId;
    }

    public static ActiveBuildState Activate(BuildPlan plan, RunSnapshot snapshot, ActiveBuildState? priorForRun = null)
    {
        var state = priorForRun is not null && priorForRun.RunId == snapshot.RunId && priorForRun.SourceBuildId == plan.SourceBuildId
            ? priorForRun
            : new ActiveBuildState(snapshot.RunId, plan.SourceBuildId);
        var heldCounts = snapshot.Inventory.GroupBy(x => x.CatalogKey, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        foreach (var target in plan.Artifacts)
        {
            var progress = state.GetOrCreate(target.CatalogKey);
            if (heldCounts.TryGetValue(target.CatalogKey, out var held))
                progress.UncertainMinimum = Math.Max(progress.UncertainMinimum, held);
        }
        state.MiracleAcquired = state.MiracleAcquired ||
            (!string.IsNullOrEmpty(plan.MiracleTarget) && snapshot.CurrentMiracle == plan.MiracleTarget);
        return state;
    }

    public void RecordArtifact(string catalogKey, ArtifactProgressEvent progressEvent)
    {
        if (progressEvent == ArtifactProgressEvent.RewardAcquired)
            GetOrCreate(catalogKey).ConfirmedAcquisitions++;
        // Altar enhancement deliberately does not count as an acquisition.
    }

    public void SetUserAdjustment(string catalogKey, int adjustment) => GetOrCreate(catalogKey).UserAdjustment = adjustment;
    public void MarkMiracleAcquired() => MiracleAcquired = true;

    public int EffectiveAcquisitions(string catalogKey) =>
        Artifacts.TryGetValue(catalogKey, out var progress) ? progress.EffectiveAcquisitions : 0;

    private ArtifactProgress GetOrCreate(string catalogKey)
    {
        if (!Artifacts.TryGetValue(catalogKey, out var progress))
        {
            progress = new ArtifactProgress();
            Artifacts[catalogKey] = progress;
        }
        return progress;
    }
}

public sealed class ActiveStateStore
{
    private readonly string _directory;

    public ActiveStateStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SephiriaBuildOverlay", "runs");
    }

    public void Save(ActiveBuildState state)
    {
        Directory.CreateDirectory(_directory);
        var safeRun = string.Concat(state.RunId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var path = Path.Combine(_directory, $"{state.SourceBuildId:D}-{safeRun}.json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonConvert.SerializeObject(state, Formatting.Indented), new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public ActiveBuildState? Load(Guid buildId, string runId)
    {
        var safeRun = string.Concat(runId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var path = Path.Combine(_directory, $"{buildId:D}-{safeRun}.json");
        if (!File.Exists(path)) return null;
        var state = JsonConvert.DeserializeObject<ActiveBuildState>(File.ReadAllText(path));
        return state is not null && state.RunId == runId && state.SourceBuildId == buildId ? state : null;
    }
}
