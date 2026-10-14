namespace TuiCode.Editor;

// Toggle line comment (#387), wrapping each line in block markers when the language has no line comment (#389).
internal sealed partial class CodeEditor
{
    /// <summary>
    /// Uncomments the lines under every caret if each non-blank one starts with <paramref name="open"/> (and ends with
    /// <paramref name="close"/>, if given), else comments them at their block's smallest indentation, as one undo step.
    /// </summary>
    public void ToggleLineComment(string open, string? close = null)
    {
        if (ReadOnly) return;
        var opening = Split(open);
        var closing = close is null ? null : Split(close);
        var prefix = Split(open + " ");
        var suffix = close is null ? null : Split(" " + close);
        EditLines(Carets, blocks =>
        {
            var rows = blocks
                .Select(block => Enumerable.Range(block.First, block.Last - block.First + 1)
                    .Where(row => LeadingWhitespace(Graphemes(row)) < Graphemes(row).Length).ToArray())
                .ToArray();
            var uncomment = rows.Any(block => block.Length > 0)
                            && rows.All(block => block.All(row => CommentedAt(Graphemes(row), opening, closing) >= 0));
            var changed = new Dictionary<int, (int At, int By)>();
            foreach (var block in rows.Where(block => block.Length > 0))
            {
                var at = block.Min(row => LeadingWhitespace(Graphemes(row)));
                foreach (var row in block)
                {
                    var line = Graphemes(row).ToList();
                    if (uncomment)
                    {
                        var start = CommentedAt([.. line], opening, closing);
                        if (closing is not null)
                        {
                            var end = ContentEnd([.. line]) - closing.Length;
                            var spaced = end > start + opening.Length && line[end - 1] == " ";
                            line.RemoveRange(spaced ? end - 1 : end, closing.Length + (spaced ? 1 : 0));
                        }
                        var count = opening.Length + (start + opening.Length < line.Count && line[start + opening.Length] == " " ? 1 : 0);
                        line.RemoveRange(start, count);
                        changed[row] = (start, -count);
                    }
                    else
                    {
                        if (suffix is not null) line.AddRange(suffix);
                        line.InsertRange(at, prefix);
                        changed[row] = (at, prefix.Length);
                    }
                    SetLine(row, string.Concat(line));
                }
            }
            return changed;
        });
    }

    // Where the opening marker starts on a line that has it after its indentation (and the closing one at its end), else -1.
    private static int CommentedAt(string[] line, string[] opening, string[]? closing)
    {
        var start = LeadingWhitespace(line);
        var end = ContentEnd(line);
        if (start + opening.Length + (closing?.Length ?? 0) > end) return -1;
        if (!MarkerAt(line, start, opening)) return -1;
        return closing is null || MarkerAt(line, end - closing.Length, closing) ? start : -1;
    }

    private static bool MarkerAt(string[] line, int start, string[] marker)
    {
        for (var i = 0; i < marker.Length; i++)
        {
            if (line[start + i] != marker[i]) return false;
        }
        return true;
    }

    private static int ContentEnd(string[] line)
    {
        var end = line.Length;
        while (end > 0 && line[end - 1] is " " or "\t") end--;
        return end;
    }
}
