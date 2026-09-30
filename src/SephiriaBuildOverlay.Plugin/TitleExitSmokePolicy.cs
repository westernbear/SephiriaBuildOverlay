namespace SephiriaBuildOverlay.Plugin;

internal static class TitleExitSmokePolicy
{
    public const string Argument = "--sbo-exit-smoke";
    public static bool Enabled(IEnumerable<string> arguments) => arguments.Any(x => x == Argument);
    public static bool CanQuit(bool requested, float elapsed, bool titleOpen, bool hasAvatar, bool pending) =>
        requested && elapsed >= 15f && titleOpen && !hasAvatar && !pending;
}
