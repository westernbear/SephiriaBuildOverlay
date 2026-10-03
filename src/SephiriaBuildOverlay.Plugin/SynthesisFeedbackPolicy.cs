namespace SephiriaBuildOverlay.Plugin;

internal static class SynthesisFeedbackPolicy
{
    public static bool NoRecommendation(bool active, bool preparing, bool calculating, bool hasSuggestion) =>
        active && !preparing && !calculating && !hasSuggestion;
}
