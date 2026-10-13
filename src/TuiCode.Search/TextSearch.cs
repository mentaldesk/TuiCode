using System.Globalization;
using System.Text;

namespace TuiCode.Search;

/// <summary>How a find box matches its query (#527).</summary>
public readonly record struct MatchOptions(bool MatchCase = false, bool WholeWord = false);

/// <summary>
/// Pure, TG-free text matching shared by in-editor find (#33) and workspace search. Matching is
/// plain-text and non-overlapping, with <see cref="MatchOptions"/> for case and whole words.
/// Queries are single-line, so a match never spans a line break.
/// </summary>
public static class TextSearch
{

    /// <summary>Split on any line terminator (<c>\r\n</c>, <c>\n</c>, <c>\r</c>); terminators are dropped.</summary>
    public static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    public static IReadOnlyList<TextMatch> FindAll(IReadOnlyList<string> lines, string query, MatchOptions options = default)
    {
        var matches = new List<TextMatch>();
        if (string.IsNullOrEmpty(query)) return matches;

        for (var row = 0; row < lines.Count; row++)
            foreach (var column in IndexesOf(lines[row], query, options))
                matches.Add(new TextMatch(row, column, query.Length));
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
    public static string ReplaceAll(string text, string query, string replacement, MatchOptions options, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(query)) return text;

        var sb = new StringBuilder(text.Length);
        var last = 0;
        foreach (var i in IndexesOf(text, query, options))
        {
            sb.Append(text, last, i - last).Append(replacement);
            last = i + query.Length;
            count++;
        }
        if (count == 0) return text;
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }

    private static IEnumerable<int> IndexesOf(string text, string query, MatchOptions options)
    {
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var i = text.IndexOf(query, comparison);
        while (i >= 0)
        {
            if (!options.WholeWord || IsWholeWord(text, i, query.Length))
            {
                yield return i;
                i = text.IndexOf(query, i + query.Length, comparison);
            }
            else i = text.IndexOf(query, i + 1, comparison);
        }
    }

    // As in VS Code, a match that starts or ends with punctuation is bounded on that side already: `.Id` finds `x.Id`.
    private static bool IsWholeWord(string text, int start, int length)
    {
        var end = start + length;
        var match = text.AsSpan(start, length);
        var startsWord = start == 0 || !IsWord(Last(text.AsSpan(0, start))) || !IsWord(First(match));
        var endsWord = end == text.Length || !IsWord(First(text.AsSpan(end))) || !IsWord(Last(match));
        return startsWord && endsWord;
    }

    private static Rune First(ReadOnlySpan<char> text)
    {
        Rune.DecodeFromUtf16(text, out var rune, out _);
        return rune;
    }

    private static Rune Last(ReadOnlySpan<char> text)
    {
        Rune.DecodeLastFromUtf16(text, out var rune, out _);
        return rune;
    }

    private static bool IsWord(Rune rune) =>
        rune.Value == '_' || Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static int Compare(TextMatch m, int row, int column) =>
        m.Row != row ? m.Row.CompareTo(row) : m.Column.CompareTo(column);
}
