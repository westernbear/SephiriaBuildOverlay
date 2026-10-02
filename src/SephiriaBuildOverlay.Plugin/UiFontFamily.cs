namespace SephiriaBuildOverlay.Plugin;

internal static class UiFontFamily
{
    // Unity does not reliably fall through an array containing an absent font;
    // select an installed family before creating the legacy dynamic atlas.
    public static string? Select(IEnumerable<string> installed)
    {
        var names = installed.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        foreach (var preferred in new[] { "Galmuri11", "Galmuri9", "Malgun Gothic", "맑은 고딕", "Segoe UI", "Arial" })
        {
            var match = names.FirstOrDefault(x => string.Equals(x, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }
}
