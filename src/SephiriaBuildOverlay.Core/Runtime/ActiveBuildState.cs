using Newtonsoft.Json;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Runtime;

public enum ArtifactProgressEvent
{
    RewardAcquired,
    AltarEnhanced
}

public enum AcquisitionEvidence { ServerAddition, ClientAddition, ClientMergeUnproven, InventoryMinimum }

public sealed class ArtifactProgress
{
    [JsonProperty("confirmedAcquisitions")]
    public int ConfirmedAcquisitions { get; set; }

    [JsonProperty("uncertainMinimum")]
    public int UncertainMinimum { get; set; }

    [JsonProperty("userAdjustment")]
    public int UserAdjustment { get; set; }

    [JsonProperty("unprovenNotification")]
    public bool UnprovenNotification { get; set; }

    [JsonProperty("lastEvidence")]
    public AcquisitionEvidence? LastEvidence { get; set; }

    [JsonIgnore]
    public int EffectiveAcquisitions => Math.Max(0, ConfirmedAcquisitions + UncertainMinimum + UserAdjustment);

    [JsonIgnore]
    public bool IsUncertain => UncertainMinimum > 0 || UnprovenNotification;
}

public sealed class ActiveBuildState
{
    [JsonProperty("runId")]
    public string RunId { get; private set; } = string.Empty;

    [JsonProperty("localPlayerId")]
    public string LocalPlayerId { get; private set; } = string.Empty;

    [JsonProperty("sessionId")]
    public string SessionId { get; private set; } = string.Empty;

    [JsonProperty("sourceBuildId")]
    public Guid SourceBuildId { get; private set; }

    [JsonProperty("artifacts")]
    public Dictionary<string, ArtifactProgress> Artifacts { get; private set; } = new(StringComparer.Ordinal);

    [JsonProperty("miracleAcquired")]
    public bool MiracleAcquired { get; private set; }

    [JsonConstructor]
    private ActiveBuildState() { }

    public ActiveBuildState(string runId, Guid sourceBuildId, string localPlayerId = "player", string sessionId = "offline")
    {
        RunId = !string.IsNullOrWhiteSpace(runId) ? runId : throw new ArgumentException("runId is required", nameof(runId));
        SourceBuildId = sourceBuildId;
        LocalPlayerId = localPlayerId;
        SessionId = sessionId;
    }

    public static ActiveBuildState Activate(BuildPlan plan, RunSnapshot snapshot, ActiveBuildState? priorForRun = null)
    {
        var state = priorForRun is not null && priorForRun.Matches(snapshot) && priorForRun.SourceBuildId == plan.SourceBuildId
            ? priorForRun
            : new ActiveBuildState(snapshot.RunId, plan.SourceBuildId, snapshot.LocalPlayerId, snapshot.Network.SessionId);
        var heldCounts = snapshot.Inventory.GroupBy(x => x.CatalogKey, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        foreach (var target in plan.Artifacts)
        {
            var progress = state.GetOrCreate(target.CatalogKey);
            if (heldCounts.TryGetValue(target.CatalogKey, out var held))
            {
                progress.UncertainMinimum = Math.Max(progress.UncertainMinimum, Math.Max(0, held - progress.ConfirmedAcquisitions));
                if (progress.UncertainMinimum > 0) progress.LastEvidence ??= AcquisitionEvidence.InventoryMinimum;
            }
        }
        state.MiracleAcquired = state.MiracleAcquired ||
            (!string.IsNullOrEmpty(plan.MiracleTarget) && snapshot.MiracleKeys.Contains(plan.MiracleTarget!));
        return state;
    }

    public bool Matches(RunSnapshot snapshot) => RunId == snapshot.RunId && LocalPlayerId == snapshot.LocalPlayerId && SessionId == snapshot.Network.SessionId;

    public void RecordArtifact(string catalogKey, ArtifactProgressEvent progressEvent, AcquisitionEvidence evidence = AcquisitionEvidence.ServerAddition)
    {
        if (progressEvent == ArtifactProgressEvent.RewardAcquired)
        {
            var progress = GetOrCreate(catalogKey);
            progress.ConfirmedAcquisitions++;
            progress.LastEvidence = evidence;
        }
        // Altar enhancement deliberately does not count as an acquisition.
    }

    public void RecordUnprovenMerge(string catalogKey)
    {
        var progress = GetOrCreate(catalogKey);
        progress.UnprovenNotification = true;
        progress.LastEvidence = AcquisitionEvidence.ClientMergeUnproven;
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
        var path = StatePath(state.SourceBuildId, state.RunId, state.LocalPlayerId, state.SessionId);
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

    public ActiveBuildState? Load(Guid buildId, RunSnapshot snapshot) => Load(buildId, snapshot.RunId, snapshot.LocalPlayerId, snapshot.Network.SessionId);

    public ActiveBuildState? Load(Guid buildId, string runId, string localPlayerId = "player", string sessionId = "offline")
    {
        var path = StatePath(buildId, runId, localPlayerId, sessionId);
        if (!File.Exists(path)) return null;
        var state = JsonConvert.DeserializeObject<ActiveBuildState>(File.ReadAllText(path));
        return state is not null && state.RunId == runId && state.SourceBuildId == buildId &&
            state.LocalPlayerId == localPlayerId && state.SessionId == sessionId ? state : null;
    }

    private string StatePath(Guid buildId, string runId, string playerId, string sessionId)
    {
        // Hash length-delimited scope fields: sanitizing ':' to '_' aliases
        // distinct rooms/players and may exceed Windows filename limits.
        var key = $"{runId.Length}:{runId}{playerId.Length}:{playerId}{sessionId.Length}:{sessionId}";
        using var hash = System.Security.Cryptography.SHA256.Create();
        var digest = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", "");
        return Path.Combine(_directory, $"{buildId:D}-{digest}.json");
    }
}
