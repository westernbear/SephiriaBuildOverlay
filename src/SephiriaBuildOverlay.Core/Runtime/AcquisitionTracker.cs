namespace SephiriaBuildOverlay.Core.Runtime;

public enum AcquisitionObservation { Ignored, Confirmed, Uncertain }

// Server source IDs survive stacking; client add RPCs contain the destination
// ID only. A repeated/merged destination cannot prove another acquisition.
public sealed class AcquisitionTracker
{
    private RunScope? _scope;
    private readonly HashSet<string> _observedInstances = new(StringComparer.Ordinal);
    private readonly HashSet<string> _serverSources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clientNotifications = new(StringComparer.Ordinal);

    public void Reset() { _scope = null; _observedInstances.Clear(); _serverSources.Clear(); _clientNotifications.Clear(); }

    public void Bind(RunSnapshot snapshot)
    {
        if (!_scope.HasValue || !_scope.Value.Equals(snapshot.Scope)) Reset();
        _scope = snapshot.Scope;
        foreach (var item in snapshot.Inventory)
        {
            _observedInstances.Add(item.InstanceId);
            _serverSources.Add(item.InstanceId); // A replay of an already held item is not a new acquisition.
        }
    }

    public AcquisitionObservation Observe(ActiveBuildState state, RunSnapshot snapshot, string instanceId, string catalogKey,
        AcquisitionEvidence evidence, bool reward = true, bool successful = true)
    {
        if (!snapshot.IsLocalPlayerOwned || !snapshot.Network.Connected || !state.Matches(snapshot) ||
            !_scope.HasValue || !_scope.Value.Equals(snapshot.Scope) || !reward || !successful || string.IsNullOrEmpty(instanceId))
            return AcquisitionObservation.Ignored;
        if (evidence == AcquisitionEvidence.ServerAddition)
        {
            if (snapshot.Network.Role == NetworkRole.Client || !_serverSources.Add(instanceId)) return AcquisitionObservation.Ignored;
            _observedInstances.Add(instanceId);
        }
        else
        {
            // Hosts get the authoritative source event and must ignore the RPC
            // echo, even when that echo arrives on a later frame.
            if (snapshot.Network.Role != NetworkRole.Client) return AcquisitionObservation.Ignored;
            if (evidence == AcquisitionEvidence.ClientAddition && !_clientNotifications.Add(instanceId)) return AcquisitionObservation.Ignored;
            if (evidence == AcquisitionEvidence.ClientMergeUnproven || !_observedInstances.Add(instanceId))
            {
                state.RecordUnprovenMerge(catalogKey);
                return AcquisitionObservation.Uncertain;
            }
        }
        state.RecordArtifact(catalogKey, ArtifactProgressEvent.RewardAcquired, evidence);
        return AcquisitionObservation.Confirmed;
    }
}
