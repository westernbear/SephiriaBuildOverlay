using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Runtime;

public enum ScreenKind
{
    None,
    ArtifactReward,
    Shop,
    WeaponUpgrade,
    MiracleChoice,
    TabletBoard,
    Inventory
}

public enum CandidateKind
{
    Artifact,
    Item,
    Weapon,
    Miracle,
    Reroll,
    AbandonOrConvert,
    Tablet
}

public sealed class ScreenCandidate
{
    public ScreenCandidate(
        string token,
        CandidateKind kind,
        string? catalogKey,
        int moneyCost = 0,
        int diceCost = 0,
        bool isFreeReroll = false,
        bool isSelectable = true,
        bool automaticActionAllowed = true,
        string? additionalCostDescription = null)
    {
        Token = token;
        Kind = kind;
        CatalogKey = catalogKey;
        MoneyCost = moneyCost;
        DiceCost = diceCost;
        IsFreeReroll = isFreeReroll;
        IsSelectable = isSelectable;
        AutomaticActionAllowed = automaticActionAllowed;
        AdditionalCostDescription = additionalCostDescription;
    }

    public string Token { get; }
    public CandidateKind Kind { get; }
    public string? CatalogKey { get; }
    public int MoneyCost { get; }
    public int DiceCost { get; }
    public bool IsFreeReroll { get; }
    public bool IsSelectable { get; }
    public bool AutomaticActionAllowed { get; }
    public string? AdditionalCostDescription { get; }
}

public sealed class InventoryArtifact
{
    public InventoryArtifact(string instanceId, string catalogKey, int x = -1, int y = -1)
    {
        InstanceId = instanceId;
        CatalogKey = catalogKey;
        X = x;
        Y = y;
    }

    public string InstanceId { get; }
    public string CatalogKey { get; }
    public int X { get; }
    public int Y { get; }
}

public sealed class RunSnapshot
{
    public RunSnapshot(
        string runId,
        string localPlayerId,
        long revision,
        ScreenKind screen,
        IReadOnlyList<ScreenCandidate> candidates,
        IReadOnlyList<InventoryArtifact> inventory,
        string? currentWeapon,
        string? currentMiracle,
        int money,
        int sharedDice,
        bool isLocalPlayerOwned = true,
        bool serverRequestPending = false,
        IReadOnlyList<string>? miracleKeys = null)
    {
        RunId = runId;
        LocalPlayerId = localPlayerId;
        Revision = revision;
        Screen = screen;
        Candidates = Array.AsReadOnly(candidates.ToArray());
        Inventory = Array.AsReadOnly(inventory.ToArray());
        CurrentWeapon = currentWeapon;
        CurrentMiracle = currentMiracle;
        MiracleKeys = Array.AsReadOnly((miracleKeys ?? (currentMiracle is null ? Array.Empty<string>() : new[] { currentMiracle })).ToArray());
        Money = money;
        SharedDice = sharedDice;
        IsLocalPlayerOwned = isLocalPlayerOwned;
        ServerRequestPending = serverRequestPending;
    }

    public string RunId { get; }
    public string LocalPlayerId { get; }
    public long Revision { get; }
    public ScreenKind Screen { get; }
    public IReadOnlyList<ScreenCandidate> Candidates { get; }
    public IReadOnlyList<InventoryArtifact> Inventory { get; }
    public string? CurrentWeapon { get; }
    public string? CurrentMiracle { get; }
    public IReadOnlyList<string> MiracleKeys { get; }
    public int Money { get; }
    public int SharedDice { get; }
    public bool IsLocalPlayerOwned { get; }
    public bool ServerRequestPending { get; }
    public SnapshotIdentity Identity => new(RunId, LocalPlayerId, Revision);
}

public readonly struct SnapshotIdentity : IEquatable<SnapshotIdentity>
{
    public SnapshotIdentity(string runId, string playerId, long revision)
    {
        RunId = runId;
        PlayerId = playerId;
        Revision = revision;
    }

    public string RunId { get; }
    public string PlayerId { get; }
    public long Revision { get; }
    public bool Equals(SnapshotIdentity other) => RunId == other.RunId && PlayerId == other.PlayerId && Revision == other.Revision;
    public override bool Equals(object? obj) => obj is SnapshotIdentity other && Equals(other);
    public override int GetHashCode() => ((RunId?.GetHashCode() ?? 0) * 397) ^ (PlayerId?.GetHashCode() ?? 0) ^ Revision.GetHashCode();
}

public enum ActionKind
{
    Select,
    Buy,
    Reroll,
    Move,
    Rotate,
    AbandonOrConvert
}

public enum DiceRisk
{
    None,
    SharedDiceSpentBeforeMiracle,
    LastSharedDieSpentBeforeMiracle
}

public sealed class RecommendedAction
{
    public RecommendedAction(
        ActionKind kind,
        string targetToken,
        string reason,
        SnapshotIdentity basedOn,
        int moneyCost = 0,
        int diceCost = 0,
        DiceRisk diceRisk = DiceRisk.None,
        string? expectedResult = null,
        bool automaticBindingAllowed = true)
    {
        Kind = kind;
        TargetToken = targetToken;
        Reason = reason;
        BasedOn = basedOn;
        MoneyCost = moneyCost;
        DiceCost = diceCost;
        DiceRisk = diceRisk;
        ExpectedResult = expectedResult ?? string.Empty;
        AutomaticBindingAllowed = automaticBindingAllowed;
    }

    public ActionKind Kind { get; }
    public string TargetToken { get; }
    public string Reason { get; }
    public SnapshotIdentity BasedOn { get; }
    public int MoneyCost { get; }
    public int DiceCost { get; }
    public DiceRisk DiceRisk { get; }
    public string ExpectedResult { get; }
    public bool AutomaticBindingAllowed { get; }
}

public sealed class Recommendation
{
    public Recommendation(RecommendedAction? action, string message)
    {
        Action = action;
        Message = message;
    }

    public RecommendedAction? Action { get; }
    public string Message { get; }
}
