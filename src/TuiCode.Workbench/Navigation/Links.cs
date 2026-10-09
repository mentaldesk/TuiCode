using System.Text.RegularExpressions;

namespace TuiCode.Workbench.Navigation;

/// <summary>Finds the <c>http</c>, <c>https</c> or <c>mailto</c> link under a column of a line (#466).</summary>
internal static partial class Links
{
    private const string TrailingPunctuation = ".,;:!?'\"*_";

    /// <summary>The link the character at <paramref name="column"/> (UTF-16) belongs to, or null.</summary>
    public static string? At(string line, int column)
    {
        foreach (Match markdown in MarkdownLink().Matches(line))
        {
            if (column < markdown.Index || column >= markdown.Index + markdown.Length) continue;
            var url = markdown.Groups["url"].Value;
            return IsAllowed(url) ? url : null;
        }

        foreach (Match bare in BareLink().Matches(line))
        {
            var url = Trim(bare.Value);
            if (column >= bare.Index && column < bare.Index + url.Length) return url;
        }
        return null;
    }

    private static bool IsAllowed(string url) => BareLink().Match(url) is { Success: true, Index: 0 } match && match.Length == url.Length;

    private static string Trim(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            if (TrailingPunctuation.Contains(last)
                || (last == ')' && url.Count(c => c == '(') < url.Count(c => c == ')'))
                || (last == ']' && url.Count(c => c == '[') < url.Count(c => c == ']')))
                url = url[..^1];
            else break;
        }
        return url;
    }

    [GeneratedRegex(@"\[[^\[\]]*\]\(<?(?<url>(?:[^()\s<>]|\([^()\s<>]*\))+)>?\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\b(?:https?://|mailto:)[^\s<>""'`]+", RegexOptions.IgnoreCase)]
    private static partial Regex BareLink();
}
