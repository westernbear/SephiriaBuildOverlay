using SephiriaBuildOverlay.Core.Solvers;

namespace SephiriaBuildOverlay.Plugin;

internal sealed class TabletSynthesisRecipe
{
    public TabletSynthesisRecipe(string first, int firstRotation, string second, int secondRotation, bool canRotate, bool nativeAllowed = true)
    { First = first; FirstRotation = firstRotation; Second = second; SecondRotation = secondRotation; CanRotate = canRotate; NativeAllowed = nativeAllowed; }
    public string First { get; }
    public int FirstRotation { get; }
    public string Second { get; }
    public int SecondRotation { get; }
    public bool CanRotate { get; }
    public bool NativeAllowed { get; }
}

internal sealed class TabletSynthesisSuggestion
{
    public TabletSynthesisSuggestion(TabletSynthesisRecipe recipe, BoardObjective before, BoardOptimizationResult result, bool losesRotation)
    { Recipe = recipe; Before = before; Result = result; LosesRotation = losesRotation; }
    public TabletSynthesisRecipe Recipe { get; }
    public BoardObjective Before { get; }
    public BoardOptimizationResult Result { get; }
    public bool LosesRotation { get; }
    public string Reason => Result.After.CompareBenefits(Before) > 0
        ? $"필수 레벨 {Before.RequiredLevel} → {Result.After.RequiredLevel} · 추천 레벨 {Before.RecommendedLevel} → {Result.After.RecommendedLevel} · 슬롯 1칸 확보"
        : "빌드 효과 유지 · 슬롯 1칸 확보";
}

// Recommendations only. Native eligibility and query rotation are captured
// on the main thread; nothing here references Unity or sends a merge request.
internal static class TabletSynthesisPlanner
{
    public static TabletSynthesisSuggestion? Recommend(BoardOptimizationInput input, IEnumerable<TabletSynthesisRecipe> recipes,
        CancellationToken token = default, int evaluationBudget = 6000)
    {
        token.ThrowIfCancellationRequested();
        if (input.Unavailable is not null || input.Cells.Count > 64 || input.Artifacts.Any(x => x.Condition == ArtifactCondition.Unknown) || evaluationBudget < 1) return null;
        var solver = new JointBoardPlanner();
        var original = solver.Solve(input, token, Math.Min(512, evaluationBudget));
        if (original.Unavailable is not null) return null;
        var remaining = evaluationBudget - original.Evaluations;
        TabletSynthesisSuggestion? best = null;
        // Each pair is evaluated with its actual material rotations, shared
        // condition and resulting rotation permission, not a sum of tooltips.
        foreach (var recipe in recipes.Take(48))
        {
            token.ThrowIfCancellationRequested();
            if (remaining <= 0) break;
            var merged = Merge(input, recipe);
            if (merged is null) continue;
            var result = solver.Solve(merged, token, Math.Min(256, remaining));
            remaining -= Math.Max(1, result.Evaluations);
            if (result.Unavailable is not null || result.After.CompareBenefits(original.After) < 0) continue;
            var losesRotation = !recipe.CanRotate && input.Tablets.Where(x => x.Id == recipe.First || x.Id == recipe.Second)
                .Any(x => x.Options.Select(o => o.Rotation).Distinct().Count() > 1);
            var candidate = new TabletSynthesisSuggestion(recipe, original.After, result, losesRotation);
            if (best is null || result.After.CompareBenefits(best.Result.After) > 0 ||
                result.After.CompareBenefits(best.Result.After) == 0 && (best.LosesRotation && !losesRotation ||
                    best.LosesRotation == losesRotation && result.After.CompareTo(best.Result.After) > 0)) best = candidate;
        }
        return best;
    }

    internal static BoardOptimizationInput? Merge(BoardOptimizationInput input, TabletSynthesisRecipe recipe)
    {
        if (!recipe.NativeAllowed || recipe.First == recipe.Second || recipe.FirstRotation is < 0 or > 3 || recipe.SecondRotation is < 0 or > 3) return null;
        var a = input.Tablets.FirstOrDefault(x => x.Id == recipe.First);
        var b = input.Tablets.FirstOrDefault(x => x.Id == recipe.Second);
        if (a is null || b is null || !a.Movable || !b.Movable || a.Key == "2101" || b.Key == "2101" ||
            !input.Items.ContainsKey(a.Id) || !input.Items.ContainsKey(b.Id)) return null;
        var options = new List<BoardTabletOption>();
        var angles = recipe.CanRotate ? new[] { 0, 1, 2, 3 } : new[] { 0 };
        foreach (var cell in input.Cells)
        foreach (var angle in angles)
        {
            var first = a.Options.FirstOrDefault(o => o.Position.Equals(cell.Position) && o.Rotation == (recipe.FirstRotation + angle) % 4);
            var second = b.Options.FirstOrDefault(o => o.Position.Equals(cell.Position) && o.Rotation == (recipe.SecondRotation + angle) % 4);
            if (first is null || second is null) continue;
            options.Add(new BoardTabletOption(cell.Position, angle, first.Effects.Concat(second.Effects),
                first.Conditions.Count > 0 ? first.Conditions : second.Conditions));
        }
        if (!options.Any(x => x.Position.Equals(a.Position) && x.Rotation == 0)) return null;
        var id = "merge:" + a.Id + ":" + b.Id;
        var items = input.Items.Where(x => x.Key != a.Id && x.Key != b.Id).ToDictionary(x => x.Key, x => x.Value);
        items[id] = a.Position;
        var tablet = new BoardTablet(id, "2101", a.Position, 0, true, options);
        return new BoardOptimizationInput(input.Width, input.Height, input.Storage, input.Cells, input.Artifacts,
            input.Tablets.Where(x => x.Id != a.Id && x.Id != b.Id).Concat(new[] { tablet }), items, input.Goals, input.Unavailable, input.GloballyActive, input.Combos);
    }
}
