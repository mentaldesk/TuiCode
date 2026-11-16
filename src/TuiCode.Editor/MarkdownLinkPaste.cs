using System.Text.RegularExpressions;

namespace TuiCode.Editor;

/// <summary>Whether pasting a URL over a selection in Markdown makes it a link, <c>[text](url)</c> (#685).</summary>
internal static partial class MarkdownLinkPaste
{
    /// <summary>The <c>http</c> or <c>https</c> address <paramref name="text"/> holds and nothing else, or null.</summary>
    public static string? Url(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace)) return null;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : null;
    }

    /// <summary>Whether the text from <paramref name="start"/> to <paramref name="end"/> (UTF-16) of <paramref name="row"/> may become a link's text.</summary>
    public static bool CanWrap(IReadOnlyList<string> lines, int row, int start, int end)
    {
        if (end <= start) return false;
        var line = lines[row];
        if (Url(line[start..end]) is not null || InFence(lines, row)) return false;
        return !Overlaps(InlineCode().Matches(line), start, end) && !Overlaps(Link().Matches(line), start, end);
    }

    private static bool Overlaps(MatchCollection matches, int start, int end) =>
        matches.Any(m => start < m.Index + m.Length && m.Index < end);

    // True on a fence line itself too.
    private static bool InFence(IReadOnlyList<string> lines, int row)
    {
        string? fence = null;
        for (var i = 0; i <= row; i++)
        {
            var match = Fence().Match(lines[i]);
            if (fence is null)
            {
                if (match.Success && !(match.Groups[1].Value[0] == '`' && match.Groups[2].Value.Contains('`'))) fence = match.Groups[1].Value;
                if (i == row) return fence is not null;
            }
            else
            {
                if (i == row) return true;
                if (match.Success && match.Groups[1].Value[0] == fence[0] && match.Groups[1].Length >= fence.Length && match.Groups[2].Value.Trim().Length == 0)
                    fence = null;
            }
        }
        return false;
    }

    [GeneratedRegex(@"^\s*(?:>\s*)*(`{3,}|~{3,})(.*)$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"(?<!`)(`+)(?!`).*?(?<!`)\1(?!`)")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"!?\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Link();
}
