namespace SephiriaBuildOverlay.Core.Runtime;

public enum ObservedActionOutcome { Pending, Succeeded, Invalidated }

// A changed revision alone is not evidence that the requested action succeeded.
public sealed class ActionOutcomeObserver
{
    private readonly RunSnapshot _before;
    private readonly RecommendedAction _action;
    private readonly ScreenCandidate _target;
    private bool _rewardObserved;

    public ActionOutcomeObserver(RunSnapshot before, RecommendedAction action)
    {
        _before = before;
        _action = action;
        _target = before.Candidates.Single(x => x.Token == action.TargetToken);
    }

    public void RecordReward(string catalogKey)
    {
        if (_target.Kind is CandidateKind.Artifact or CandidateKind.Tablet && _target.CatalogKey == catalogKey)
            _rewardObserved = true;
    }

    public ObservedActionOutcome Observe(RunSnapshot after)
    {
        if (after.RunId != _before.RunId || after.LocalPlayerId != _before.LocalPlayerId || !after.IsLocalPlayerOwned)
            return ObservedActionOutcome.Invalidated;

        var succeeded = _action.Kind switch
        {
            ActionKind.Select or ActionKind.Buy => _target.Kind switch
            {
                CandidateKind.Artifact or CandidateKind.Tablet => _rewardObserved || after.Inventory.Count(x => x.CatalogKey == _target.CatalogKey) >
                    _before.Inventory.Count(x => x.CatalogKey == _target.CatalogKey),
                CandidateKind.Weapon => after.CurrentWeapon == _target.CatalogKey && after.CurrentWeapon != _before.CurrentWeapon,
                CandidateKind.Miracle => after.MiracleKeys.Contains(_target.CatalogKey!) && !_before.MiracleKeys.Contains(_target.CatalogKey!),
                _ => false
            },
            ActionKind.Reroll => after.Screen == _before.Screen &&
                !CandidateSet(_before).SequenceEqual(CandidateSet(after)),
            ActionKind.AbandonOrConvert => after.Screen != _before.Screen,
            _ => false
        };
        return succeeded ? ObservedActionOutcome.Succeeded : ObservedActionOutcome.Pending;
    }

    private static IEnumerable<string> CandidateSet(RunSnapshot snapshot) => snapshot.Candidates
        .Where(x => x.Kind is not (CandidateKind.Reroll or CandidateKind.AbandonOrConvert))
        .Select(x => x.Token + ":" + x.Kind + ":" + x.CatalogKey).OrderBy(x => x, StringComparer.Ordinal);
}
