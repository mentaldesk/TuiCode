using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Navigation;

/// <summary>An item that still matches what's typed, with the indexes of <see cref="CompletionItem.Label"/>'s chars that matched.</summary>
public sealed record CompletionMatch(CompletionItem Item, IReadOnlyList<int> Marks);

/// <summary>The completion list's rules, free of Terminal.Gui: when it opens, how it narrows, what accepting puts in, and its rows.</summary>
public static class Completions
{
    public const int MaxHeight = 10;

    private const int LabelMax = 40;

    public static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Where the name the caret is at the end of starts, in UTF-16 chars.</summary>
    public static int WordStart(string line, int character)
    {
        character = Math.Clamp(character, 0, line.Length);
        var start = character;
        while (start > 0 && IsNameChar(line[start - 1])) start--;
        return start;
    }

    /// <summary>
    /// Whether typing the char before <paramref name="character"/> opens the list by itself: a server's trigger character,
    /// returned, or a letter of a name, as an empty string. Null when it doesn't.
    /// </summary>
    public static string? Trigger(string line, int character, IReadOnlySet<string> triggers)
    {
        if (character <= 0 || character > line.Length) return null;
        if (triggers.Where(t => character >= t.Length && string.CompareOrdinal(line, character - t.Length, t, 0, t.Length) == 0)
                .OrderByDescending(t => t.Length).FirstOrDefault() is { } trigger)
            return trigger;
        var start = WordStart(line, character);
        return start < character && (char.IsLetter(line[start]) || line[start] == '_') ? "" : null;
    }

    /// <summary>
    /// The items <paramref name="prefix"/> still matches, best first: by case, then from the start, then by the server's order.
    /// Items that would show and put in the same, as overloads do before their detail is known, are one row.
    /// </summary>
    public static IReadOnlyList<CompletionMatch> Narrow(IReadOnlyList<CompletionItem> items, string prefix) =>
        [.. items
            .DistinctBy(item => (item.Label, item.Kind, item.Detail, item.Text))
            .Select(item => (Item: item, Rank: Rank(item.Filter, prefix)))
            .Where(m => m.Rank is not null)
            .OrderBy(m => m.Rank)
            .ThenBy(m => m.Item.Sort, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Item.Label, StringComparer.Ordinal)
            .Select(m => new CompletionMatch(m.Item, Marks(m.Item.Label, prefix)))];

    // 0: starts with it as typed; 1: in any case; 2: its letters in order from a word's start; null: no match.
    private static int? Rank(string text, string prefix)
    {
        if (prefix.Length == 0 || text.StartsWith(prefix, StringComparison.Ordinal)) return 0;
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 1;
        for (var start = 0; start < text.Length; start++)
            if (IsWordStart(text, start) && Subsequence(text, start, prefix) is not null) return 2;
        return null;
    }

    private static IReadOnlyList<int> Marks(string label, string prefix)
    {
        if (prefix.Length == 0) return [];
        if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return [.. Enumerable.Range(0, prefix.Length)];
        for (var start = 0; start < label.Length; start++)
            if (IsWordStart(label, start) && Subsequence(label, start, prefix) is { } marks) return marks;
        return [];
    }

    private static bool IsWordStart(string text, int at) =>
        at == 0 || (char.IsUpper(text[at]) && !char.IsUpper(text[at - 1])) || (IsNameChar(text[at]) && !IsNameChar(text[at - 1]));

    private static List<int>? Subsequence(string text, int start, string prefix)
    {
        if (char.ToLowerInvariant(text[start]) != char.ToLowerInvariant(prefix[0])) return null;
        var marks = new List<int> { start };
        var at = start + 1;
        foreach (var c in prefix.AsSpan(1))
        {
            while (at < text.Length && char.ToLowerInvariant(text[at]) != char.ToLowerInvariant(c)) at++;
            if (at == text.Length) return null;
            marks.Add(at++);
        }
        return marks;
    }

    /// <summary>
    /// What accepting <paramref name="item"/> replaces on <paramref name="row"/>, and with what: its edit's range where the server
    /// gave one, moved on by what's been typed since it was asked at <paramref name="asked"/>; else the name before the caret.
    /// </summary>
    public static (TextMatch Range, string Text) Apply(CompletionItem item, int row, int wordStart, int asked, int caret)
    {
        if (item.Edit is { } edit && edit.Line == row && edit.Start <= caret)
        {
            var end = edit.End >= asked ? edit.End + (caret - asked) : edit.End;
            end = Math.Max(end, edit.Start);
            return (new TextMatch(row, edit.Start, end - edit.Start), edit.NewText);
        }
        return (new TextMatch(row, wordStart, Math.Max(caret - wordStart, 0)), item.InsertText ?? item.Label);
    }

    /// <summary>The kind as one column: <c>ƒ</c> for anything called, else a letter.</summary>
    public static string KindMark(int kind) => kind switch
    {
        2 or 3 or 4 => "ƒ",
        1 => "t",
        5 => "f",
        6 or 12 => "v",
        7 => "C",
        8 => "I",
        9 => "M",
        10 => "p",
        11 => "u",
        13 => "E",
        14 => "k",
        15 => "s",
        16 or 21 => "c",
        17 => "F",
        18 => "r",
        19 => "D",
        20 => "e",
        22 => "S",
        23 => "!",
        24 => "o",
        25 => "T",
        _ => " ",
    };

    /// <summary>Columns for the name, as wide as the longest up to a cap, and the row's width with its detail.</summary>
    public static (int Label, int Row) Widths(IReadOnlyList<CompletionMatch> matches, Func<CompletionItem, string?>? detailOf = null)
    {
        var label = Math.Min(matches.Select(m => m.Item.Label.GetColumns()).DefaultIfEmpty(0).Max(), LabelMax);
        var detail = matches.Select(m => Detail(detailOf?.Invoke(m.Item) ?? m.Item.Detail).GetColumns()).DefaultIfEmpty(0).Max();
        return (label, RowWidth(label, detail));
    }

    public static int RowWidth(int label, int detail) => 1 + 2 + label + (detail > 0 ? 2 + detail : 0) + 1;

    public static string Detail(string? detail) => (detail ?? "").ReplaceLineEndings(" ").Trim();
}
