using System.Text.RegularExpressions;

namespace TuiCode.Editor;

/// <summary>Which lines of a Markdown file are fenced code, so folding and link paste agree on it.</summary>
internal static partial class MarkdownFences
{
    /// <summary>For each line, whether it is in a fenced code block, its fence lines included.</summary>
    public static bool[] CodeLines(IReadOnlyList<string> lines)
    {
        var code = new bool[lines.Count];
        string? fence = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var match = Fence().Match(lines[i]);
            if (fence is null)
            {
                if (match.Success && !(match.Groups[1].Value[0] == '`' && match.Groups[2].Value.Contains('`')))
                    fence = match.Groups[1].Value;
            }
            else if (match.Success && match.Groups[1].Value[0] == fence[0] && match.Groups[1].Length >= fence.Length
                     && match.Groups[2].Value.Trim().Length == 0)
            {
                fence = null;
                code[i] = true;
            }
            code[i] |= fence is not null;
        }
        return code;
    }

    [GeneratedRegex(@"^\s*(?:>\s*)*(`{3,}|~{3,})(.*)$")]
    private static partial Regex Fence();
}
