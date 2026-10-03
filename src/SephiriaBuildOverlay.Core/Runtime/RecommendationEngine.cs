using SephiriaBuildOverlay.Core.Catalog;
using SephiriaBuildOverlay.Core.Models;

namespace SephiriaBuildOverlay.Core.Runtime;

public sealed class RecommendationEngine
{
    private readonly IReadOnlyDictionary<string, CatalogBinding> _bindings;

    public RecommendationEngine(IReadOnlyDictionary<string, CatalogBinding>? bindings = null)
    {
        _bindings = bindings ?? new Dictionary<string, CatalogBinding>();
    }

    public Recommendation Recommend(BuildPlan plan, ActiveBuildState state, RunSnapshot snapshot)
    {
        if (!snapshot.IsLocalPlayerOwned) return new Recommendation(null, "로컬 소유 플레이어가 아니므로 행동하지 않습니다.");
        if (snapshot.ServerRequestPending) return new Recommendation(null, "이전 서버 응답을 기다리는 중입니다.");

        return snapshot.Screen switch
        {
            ScreenKind.ArtifactReward => RecommendArtifact(plan, state, snapshot, false),
            ScreenKind.Shop => RecommendArtifact(plan, state, snapshot, true),
            ScreenKind.WeaponUpgrade => RecommendWeapon(plan, state, snapshot),
            ScreenKind.MiracleChoice => RecommendMiracle(plan, state, snapshot),
            _ => new Recommendation(null, "현재 화면에서 확인할 변경 동작이 없습니다.")
        };
    }

    private Recommendation RecommendArtifact(BuildPlan plan, ActiveBuildState state, RunSnapshot snapshot, bool shop)
    {
        var remaining = plan.Artifacts
            .Where(target => target.Role is TargetRole.Required or TargetRole.Recommended)
            .Select(target => new { Target = target, Remaining = target.DesiredAcquisitions - state.EffectiveAcquisitions(target.CatalogKey) })
            .Where(x => x.Remaining > 0)
            .OrderBy(x => x.Target.Role == TargetRole.Required ? 0 : 1)
            .ThenBy(x => x.Target.Priority)
            .ToArray();

        Recommendation? blocked = null;
        foreach (var goal in remaining)
        {
            var candidate = snapshot.Candidates.Where(x => x.IsSelectable &&
                (x.Kind == CandidateKind.Artifact || x.Kind == CandidateKind.Item) && x.CatalogKey == goal.Target.CatalogKey)
                .OrderBy(x => InventoryAdmissionPolicy.BlockReason(x.Admission) is null ? 0 : 1).FirstOrDefault();
            if (candidate is null) continue;
            var blockReason = InventoryAdmissionPolicy.BlockReason(candidate.Admission);
            var action = new RecommendedAction(
                shop ? ActionKind.Buy : ActionKind.Select,
                candidate.Token,
                $"{(goal.Target.Role == TargetRole.Required ? "필수" : "추천")} 목표, {goal.Remaining}회 남음",
                snapshot.Identity,
                candidate.MoneyCost,
                candidate.DiceCost,
                DiceWarning(plan, state, snapshot, candidate),
                $"{goal.Target.CatalogKey} 획득" + CostDetail(candidate),
                IsAutomaticAllowed(goal.Target.CatalogKey) && candidate.AutomaticActionAllowed && blockReason is null,
                executionBlockReason: blockReason);
            var affordability = candidate.MoneyCost > snapshot.Money ? " (재화 부족: 실행 시 중단)" : string.Empty;
            var result = new Recommendation(action, blockReason ?? action.Reason + affordability + CostDetail(candidate));
            if (blockReason is null) return result;
            blocked ??= result;
        }

        // Retain the target's frame and explain why it cannot be acquired. Do
        // not spend rerolls, abandon it, or invent a discard/sale automatically.
        if (blocked is not null) return blocked;
        var items = snapshot.Candidates.Where(x => x.IsSelectable && InventoryAdmissionPolicy.IsInventoryItem(x.Kind)).ToArray();
        if (items.Length > 0 && items.All(x => x.Admission == InventoryAdmission.Full))
        {
            var conversion = snapshot.Candidates.FirstOrDefault(x => x.IsSelectable && x.Kind == CandidateKind.AbandonOrConvert);
            return conversion is null ? new Recommendation(null, InventoryAdmissionPolicy.BlockReason(InventoryAdmission.Full)!) :
                ConvertReward(plan, state, snapshot, conversion, "가방이 가득 차고 빌드 목표 후보가 없어 주사위 변환을 제안합니다.");
        }

        var freeReroll = snapshot.Candidates.FirstOrDefault(x => RerollAvailable(x, snapshot) && x.IsFreeReroll);
        if (freeReroll is not null)
            return Reroll(plan, state, snapshot, freeReroll, "목표가 없어 무료 리롤을 제안합니다.");
        var paidReroll = snapshot.Candidates.FirstOrDefault(x => RerollAvailable(x, snapshot) && !x.IsFreeReroll);
        if (paidReroll is not null)
            return Reroll(plan, state, snapshot, paidReroll, "목표가 없어 유료 리롤을 제안합니다.");
        var abandon = snapshot.Candidates.FirstOrDefault(x => x.IsSelectable && x.Kind == CandidateKind.AbandonOrConvert);
        if (abandon is not null)
            return ConvertReward(plan, state, snapshot, abandon, "빌드 밖 후보만 있어 게임의 포기/주사위 변환을 제안합니다.");
        return new Recommendation(null, "빌드 목표가 없고 리롤/포기도 불가능합니다. 수동 결정을 기다립니다.");
    }

    private static Recommendation ConvertReward(BuildPlan plan, ActiveBuildState state, RunSnapshot snapshot, ScreenCandidate candidate, string reason)
    {
        var action = new RecommendedAction(ActionKind.AbandonOrConvert, candidate.Token, reason, snapshot.Identity,
            candidate.MoneyCost, candidate.DiceCost, DiceWarning(plan, state, snapshot, candidate),
            candidate.AdditionalCostDescription, candidate.AutomaticActionAllowed);
        return new Recommendation(action, reason);
    }

    private Recommendation RecommendWeapon(BuildPlan plan, ActiveBuildState state, RunSnapshot snapshot)
    {
        if (plan.WeaponPath.Count == 0) return new Recommendation(null, "무기 목표가 없습니다.");
        var currentIndex = snapshot.CurrentWeapon is null ? -1 : IndexOf(plan.WeaponPath, snapshot.CurrentWeapon);
        var nextIndex = Math.Min(currentIndex + 1, plan.WeaponPath.Count - 1);
        if (currentIndex == plan.WeaponPath.Count - 1) return new Recommendation(null, "최종 목표 무기를 이미 보유했습니다.");
        var next = plan.WeaponPath[nextIndex];
        var candidate = snapshot.Candidates.FirstOrDefault(x => x.IsSelectable && x.Kind == CandidateKind.Weapon && x.CatalogKey == next);
        if (candidate is not null)
        {
            var action = new RecommendedAction(ActionKind.Select, candidate.Token, $"목표 무기 경로의 다음 단계: {next}",
                snapshot.Identity, candidate.MoneyCost, candidate.DiceCost, DiceWarning(plan, state, snapshot, candidate),
                $"무기 {next}", IsAutomaticAllowed(next) && candidate.AutomaticActionAllowed);
            return new Recommendation(action, action.Reason);
        }
        var reroll = snapshot.Candidates.FirstOrDefault(x => RerollAvailable(x, snapshot));
        return reroll is not null
            ? Reroll(plan, state, snapshot, reroll, "목표 무기 자식이 없어 리롤을 제안합니다.")
            : new Recommendation(null, "목표 무기 자식이 없고 리롤할 수 없습니다.");
    }

    private Recommendation RecommendMiracle(BuildPlan plan, ActiveBuildState state, RunSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(plan.MiracleTarget)) return new Recommendation(null, "나무 뿌리 목표가 없습니다.");
        if (state.MiracleAcquired || snapshot.MiracleKeys.Contains(plan.MiracleTarget!))
            return new Recommendation(null, "목표 나무 뿌리 능력을 이미 획득했습니다.");
        var target = snapshot.Candidates.FirstOrDefault(x => x.IsSelectable && x.Kind == CandidateKind.Miracle && x.CatalogKey == plan.MiracleTarget);
        if (target is not null)
        {
            var action = new RecommendedAction(ActionKind.Select, target.Token, "목표 나무 뿌리 능력", snapshot.Identity,
                target.MoneyCost, target.DiceCost, DiceRisk.None, $"기적 {plan.MiracleTarget}", IsAutomaticAllowed(plan.MiracleTarget) && target.AutomaticActionAllowed);
            return new Recommendation(action, action.Reason);
        }
        var reroll = snapshot.Candidates.FirstOrDefault(x => RerollAvailable(x, snapshot));
        return reroll is not null
            ? Reroll(plan, state, snapshot, reroll, "목표 나무 뿌리 능력이 없어 리롤을 최우선 제안합니다.")
            : new Recommendation(null, "목표 나무 뿌리 능력이 없고 리롤할 수 없습니다.");
    }

    private Recommendation Reroll(BuildPlan plan, ActiveBuildState state, RunSnapshot snapshot, ScreenCandidate candidate, string reason)
    {
        var action = new RecommendedAction(ActionKind.Reroll, candidate.Token, reason, snapshot.Identity,
            candidate.MoneyCost, candidate.DiceCost, DiceWarning(plan, state, snapshot, candidate), "후보 갱신" + CostDetail(candidate), candidate.AutomaticActionAllowed);
        return new Recommendation(action, reason + CostDetail(candidate));
    }

    private static string CostDetail(ScreenCandidate candidate) => string.IsNullOrEmpty(candidate.AdditionalCostDescription)
        ? string.Empty : " · " + candidate.AdditionalCostDescription;

    private static bool RerollAvailable(ScreenCandidate candidate, RunSnapshot snapshot) => candidate.IsSelectable &&
        candidate.Kind == CandidateKind.Reroll && candidate.DiceCost <= snapshot.SharedDice && candidate.MoneyCost <= snapshot.Money;

    private static DiceRisk DiceWarning(BuildPlan plan, ActiveBuildState state, RunSnapshot snapshot, ScreenCandidate candidate)
    {
        if (state.MiracleAcquired || string.IsNullOrEmpty(plan.MiracleTarget) || snapshot.MiracleKeys.Contains(plan.MiracleTarget!) || candidate.DiceCost <= 0 || candidate.IsFreeReroll)
            return DiceRisk.None;
        if (snapshot.Screen == ScreenKind.MiracleChoice) return DiceRisk.None;
        return snapshot.SharedDice - candidate.DiceCost <= 0
            ? DiceRisk.LastSharedDieSpentBeforeMiracle
            : DiceRisk.SharedDiceSpentBeforeMiracle;
    }

    private bool IsAutomaticAllowed(string catalogKey) =>
        _bindings.TryGetValue(catalogKey, out var binding) && binding.AllowsAutomaticAction;

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var index = 0; index < list.Count; index++) if (list[index] == value) return index;
        return -1;
    }
}
