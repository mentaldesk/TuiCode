namespace TuiCode.Search;

/// <summary>
/// Pure, TG-free text matching shared by in-editor find (#33) and workspace search. Matching is
/// plain-text, case-insensitive and non-overlapping (VS Code's defaults); case/whole-word/regex
/// options are a follow-up. Queries are single-line, so a match never spans a line break.
/// </summary>
public static class TextSearch
{
    private const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>Split on any line terminator (<c>\r\n</c>, <c>\n</c>, <c>\r</c>); terminators are dropped.</summary>
    public static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    public static IReadOnlyList<TextMatch> FindAll(IReadOnlyList<string> lines, string query)
    {
        var matches = new List<TextMatch>();
        if (string.IsNullOrEmpty(query)) return matches;

        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            var i = line.IndexOf(query, Comparison);
            while (i >= 0)
            {
                matches.Add(new TextMatch(row, i, query.Length));
                i = line.IndexOf(query, i + query.Length, Comparison);
            }
        }
        return matches;
    }

    /// <summary>
    /// Index of the first match starting at or after (<paramref name="row"/>, <paramref name="column"/>),
    /// wrapping to the first match. -1 when there are no matches.
    /// </summary>
    public static int IndexAtOrAfter(IReadOnlyList<TextMatch> matches, int row, int column)
    {
        if (matches.Count == 0) return -1;
        for (var i = 0; i < matches.Count; i++)
            if (Compare(matches[i], row, column) >= 0) return i;
        return 0;
    }

    /// <summary>Index of the first match starting strictly after the position, wrapping. -1 when empty.</summary>
    public static int IndexAfter(IReadOnlyList<TextMatch> matches, int row, int column)
    {
        if (matches.Count == 0) return -1;
        for (var i = 0; i < matches.Count; i++)
            if (Compare(matches[i], row, column) > 0) return i;
        return 0;
    }

    /// <summary>Index of the last match starting strictly before the position, wrapping. -1 when empty.</summary>
    public static int IndexBefore(IReadOnlyList<TextMatch> matches, int row, int column)
    {
        if (matches.Count == 0) return -1;
        for (var i = matches.Count - 1; i >= 0; i--)
            if (Compare(matches[i], row, column) < 0) return i;
        return matches.Count - 1;
    }

    /// <summary>
    /// Replace every match of <paramref name="query"/> in raw <paramref name="text"/>, leaving everything
    /// else — line endings included — byte-for-byte untouched.
    /// </summary>
    public static string ReplaceAll(string text, string query, string replacement, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(query)) return text;

        var sb = new System.Text.StringBuilder(text.Length);
        var last = 0;
        var i = text.IndexOf(query, Comparison);
        while (i >= 0)
        {
            sb.Append(text, last, i - last).Append(replacement);
            last = i + query.Length;
            count++;
            i = text.IndexOf(query, last, Comparison);
        }
        if (count == 0) return text;
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    private static int Compare(TextMatch m, int row, int column) =>
        m.Row != row ? m.Row.CompareTo(row) : m.Column.CompareTo(column);
}
