using Point = System.Drawing.Point;

namespace TuiCode.Editor;

// Selecting occurrences (#113); see "Multiple cursors and undo" in AGENTS.md.
internal sealed partial class EditorTextView
{
    // The text last selected from a bare caret, whose occurrences match only as whole words or whitespace runs.
    private string? _wholeOccurrence;

    /// <summary>Adds a selection at the next occurrence of the primary selection, wrapping at the end of the buffer.</summary>
    public void SelectNextOccurrence() => SelectOccurrence(forward: true);

    /// <summary>Adds a selection at the previous occurrence of the primary selection, wrapping at the start of the buffer.</summary>
    public void SelectPreviousOccurrence() => SelectOccurrence(forward: false);

    /// <summary>Selects every occurrence of the primary selection, or of the word or whitespace at the primary caret.</summary>
    public void SelectAllOccurrences()
    {
        var lines = GraphemeLines();
        if (PrimaryCaret.Anchor is null && !SelectRunsAtCarets(lines)) return;

        var primary = PrimaryCaret;
        var matches = FindOccurrences(lines, primary);
        var carets = matches.Select(m => new Caret(m.End, m.Start)).ToList();
        var index = carets.FindIndex(c => c.Start == primary.Start);
        if (index < 0) return;
        SetCarets([carets[index], .. carets.Where((_, i) => i != index)], reveal: false);
    }

    private void SelectOccurrence(bool forward)
    {
        var lines = GraphemeLines();
        if (PrimaryCaret.Anchor is null)
        {
            SelectRunsAtCarets(lines);
            return;
        }

        var carets = Carets;
        var primary = carets[0];
        var unselected = FindOccurrences(lines, primary)
            .Where(m => !carets.Any(c => c.Anchor is not null && c.Start == m.Start && c.End == m.End))
            .ToList();
        if (unselected.Count == 0) return;

        var (start, end) = forward
            ? unselected.FirstOrDefault(m => !Caret.Before(m.Start, primary.End), unselected[0])
            : unselected.LastOrDefault(m => !Caret.Before(primary.Start, m.End), unselected[^1]);
        SetCarets([new Caret(end, start), .. carets]);
    }

    // Selects the word or whitespace run at every caret without a selection. False if the primary caret has none.
    private bool SelectRunsAtCarets(string[][] lines)
    {
        var carets = Carets;
        if (Occurrences.RunAt(lines, carets[0].Position) is not { } run) return false;

        _wholeOccurrence = TextBetween(run.Start, run.End);
        SetCarets([.. carets.Select(caret =>
            caret.Anchor is null && Occurrences.RunAt(lines, caret.Position) is { } r ? new Caret(r.End, r.Start) : caret)]);
        return true;
    }

    private List<(Point Start, Point End)> FindOccurrences(string[][] lines, Caret primary)
    {
        var needle = Enumerable.Range(primary.Start.Y, primary.End.Y - primary.Start.Y + 1)
            .Select(row => lines[row][(row == primary.Start.Y ? primary.Start.X : 0)..(row == primary.End.Y ? primary.End.X : lines[row].Length)])
            .ToArray();
        var whole = primary.Start.Y == primary.End.Y && TextBetween(primary.Start, primary.End) == _wholeOccurrence;
        return Occurrences.Find(lines, needle, whole);
    }

    private string[][] GraphemeLines() =>
        [.. Enumerable.Range(0, Lines).Select(row => GetLine(row).Select(cell => cell.Grapheme).ToArray())];
}
