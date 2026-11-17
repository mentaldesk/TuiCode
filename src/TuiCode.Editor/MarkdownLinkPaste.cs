using System.Text.RegularExpressions;

namespace TuiCode.Editor;

/// <summary>Whether pasting a URL over a selection in Markdown makes it a link, <c>[text](url)</c> (#685).</summary>
internal static partial class MarkdownLinkPaste
{
    /// <summary>The <c>http</c> or <c>https</c> address <paramref name="text"/> holds and nothing else, or null; null too when its parentheses don't balance.</summary>
    public static string? Url(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace) || !Balanced(trimmed, '(', ')')) return null;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : null;
    }

    /// <summary>Whether the text from <paramref name="start"/> to <paramref name="end"/> (UTF-16) of <paramref name="line"/>, which isn't fenced code, may become a link's text.</summary>
    public static bool CanWrap(string line, int start, int end)
    {
        if (end <= start || !Balanced(line[start..end], '[', ']')) return false;
        return !Overlaps(InlineCode().Matches(line), start, end) && !Overlaps(Link().Matches(line), start, end);
    }

    private static bool Overlaps(MatchCollection matches, int start, int end) =>
        matches.Any(m => start < m.Index + m.Length && m.Index < end);

    private static bool Balanced(string text, char open, char close)
    {
        var depth = 0;
        foreach (var c in text)
        {
            if (c == open) depth++;
            else if (c == close && --depth < 0) return false;
        }
        return depth == 0;
    }

    [GeneratedRegex(@"(?<!`)(`+)(?!`).*?(?<!`)\1(?!`)")]
    private static partial Regex InlineCode();

    // An inline or reference link or image, an autolink, or a bare address.
    [GeneratedRegex(@"!?\[[^\]]*\](?:\([^)]*\)|\[[^\]]*\])|<[A-Za-z][A-Za-z0-9+.-]{1,31}:[^\s<>]*>|(?:https?://|www\.)[^\s<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Link();
}
