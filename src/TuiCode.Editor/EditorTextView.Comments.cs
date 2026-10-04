namespace TuiCode.Editor;

// Toggle line comment (#387), wrapping each line in block markers when the language has no line comment (#389).
internal sealed partial class EditorTextView
{
    /// <summary>
    /// Uncomments the lines under every caret if each non-blank one starts with <paramref name="open"/> (and ends with
    /// <paramref name="close"/>, if given), else comments them at their block's smallest indentation, as one undo step.
    /// </summary>
    public void ToggleLineComment(string open, string? close = null)
    {
        if (ReadOnly) return;
        var opening = Cell.ToCellList(open);
        var closing = close is null ? null : Cell.ToCellList(close);
        var prefix = Cell.ToCellList(open + " ");
        var suffix = close is null ? null : Cell.ToCellList(" " + close);
        EditLines(Carets, blocks =>
        {
            var rows = blocks
                .Select(block => Enumerable.Range(block.First, block.Last - block.First + 1)
                    .Where(row => LeadingWhitespace(GetLine(row)) < GetLine(row).Count).ToArray())
                .ToArray();
            var uncomment = rows.Any(block => block.Length > 0)
                            && rows.All(block => block.All(row => CommentedAt(GetLine(row), opening, closing) >= 0));
            var changed = new Dictionary<int, (int At, int By)>();
            foreach (var block in rows.Where(block => block.Length > 0))
            {
                var at = block.Min(row => LeadingWhitespace(GetLine(row)));
                foreach (var row in block)
                {
                    var line = GetLine(row);
                    if (uncomment)
                    {
                        var start = CommentedAt(line, opening, closing);
                        if (closing is not null)
                        {
                            var end = ContentEnd(line) - closing.Count;
                            var spaced = end > start + opening.Count && line[end - 1].Grapheme == " ";
                            line.RemoveRange(spaced ? end - 1 : end, closing.Count + (spaced ? 1 : 0));
                        }
                        var count = opening.Count + (start + opening.Count < line.Count && line[start + opening.Count].Grapheme == " " ? 1 : 0);
                        line.RemoveRange(start, count);
                        changed[row] = (start, -count);
                    }
                    else
                    {
                        if (suffix is not null) line.AddRange(suffix);
                        line.InsertRange(at, prefix);
                        changed[row] = (at, prefix.Count);
                    }
                }
            }
            return changed;
        });
    }

    // Where the opening marker starts on a line that has it after its indentation (and the closing one at its end), else -1.
    private static int CommentedAt(List<Cell> line, List<Cell> opening, List<Cell>? closing)
    {
        var start = LeadingWhitespace(line);
        var end = ContentEnd(line);
        if (start + opening.Count + (closing?.Count ?? 0) > end) return -1;
        if (!MarkerAt(line, start, opening)) return -1;
        return closing is null || MarkerAt(line, end - closing.Count, closing) ? start : -1;
    }

    private static bool MarkerAt(List<Cell> line, int start, List<Cell> marker)
    {
        for (var i = 0; i < marker.Count; i++)
        {
            if (line[start + i].Grapheme != marker[i].Grapheme) return false;
        }
        return true;
    }

    private static int ContentEnd(List<Cell> line)
    {
        var end = line.Count;
        while (end > 0 && line[end - 1].Grapheme is " " or "\t") end--;
        return end;
    }
}
