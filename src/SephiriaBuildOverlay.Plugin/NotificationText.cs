using System.Net;
using System.Text.RegularExpressions;

namespace SephiriaBuildOverlay.Plugin;

// Native TMP formatting is not interpreted by our plain-text bubble labels.
// Preserve message content, numbers and ordinary angle-bracket comparisons.
internal static class NotificationText
{
    private static readonly Regex Tags = new(@"</?(?:color|indent|size|b|i|u|s|mark|alpha|align|cspace|mspace|voffset|line-height|link|sprite|font|style|nobr|noparse|pos|space|margin|br|tag)(?:[-_]id)?\b[^>\r\n]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Placeholders = new(@"(?:\[+|\{+)?/?\b(?:INDENT|COLOR)[-_]ID\b(?:\s*[:=]\s*[^\s\]}<>]+|[_-]\d+)?(?:\]+|\}+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string Plain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"<#(?:[a-f0-9]{6}|[a-f0-9]{8})>", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Placeholders.Replace(Tags.Replace(text, ""), "");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        return string.Join("\n", text.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0));
    }
}
