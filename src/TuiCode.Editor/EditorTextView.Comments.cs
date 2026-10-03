namespace TuiCode.Editor;

// Toggle line comment (#387).
internal sealed partial class EditorTextView
{
    /// <summary>
    /// Uncomments the lines under every caret if each non-blank one starts with <paramref name="marker"/>, else comments them
    /// at their block's smallest indentation, as one undo step.
    /// </summary>
    public void ToggleLineComment(string marker)
    {
        if (ReadOnly) return;
        var cells = Cell.ToCellList(marker);
        var comment = Cell.ToCellList(marker + " ");
        EditLines(Carets, blocks =>
        {
            var rows = blocks
                .Select(block => Enumerable.Range(block.First, block.Last - block.First + 1)
                    .Where(row => LeadingWhitespace(GetLine(row)) < GetLine(row).Count).ToArray())
                .ToArray();
            var uncomment = rows.Any(block => block.Length > 0)
                            && rows.All(block => block.All(row => CommentedAt(GetLine(row), cells) >= 0));
            var changed = new Dictionary<int, (int At, int By)>();
            foreach (var block in rows.Where(block => block.Length > 0))
            {
                var at = block.Min(row => LeadingWhitespace(GetLine(row)));
                foreach (var row in block)
                {
                    var line = GetLine(row);
                    if (uncomment)
                    {
                        var start = CommentedAt(line, cells);
                        var count = cells.Count + (start + cells.Count < line.Count && line[start + cells.Count].Grapheme == " " ? 1 : 0);
                        line.RemoveRange(start, count);
                        changed[row] = (start, -count);
                    }
                    else
                    {
                        line.InsertRange(at, comment);
                        changed[row] = (at, comment.Count);
                    }
                }
            }
            return changed;
        });
    }

    // Where the marker starts on a line that has it after its indentation, else -1.
    private static int CommentedAt(List<Cell> line, List<Cell> marker)
    {
        var start = LeadingWhitespace(line);
        if (start + marker.Count > line.Count) return -1;
        for (var i = 0; i < marker.Count; i++)
        {
            if (line[start + i].Grapheme != marker[i].Grapheme) return -1;
        }
        return start;
    }
}
