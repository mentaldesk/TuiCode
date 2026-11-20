using System.Text;

namespace TuiCode.Workbench.Languages;

/// <summary>Applies a server's text edits to a file's text, keeping its line endings as they are.</summary>
public static class TextEdits
{
    /// <summary>The text with every edit made, or null when one falls outside it or two overlap.</summary>
    public static string? Apply(string text, IReadOnlyList<TextEdit> edits)
    {
        var starts = LineStarts(text);
        var spans = new List<(int Start, int End, string NewText)>();
        foreach (var edit in edits)
        {
            if (Offset(text, starts, edit.StartLine, edit.StartCharacter) is not { } start
                || Offset(text, starts, edit.EndLine, edit.EndCharacter) is not { } end
                || end < start)
                return null;
            spans.Add((start, end, edit.NewText));
        }
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        for (var i = 1; i < spans.Count; i++)
            if (spans[i].Start < spans[i - 1].End) return null;

        var result = new StringBuilder(text);
        // Back to front, so the offsets still to come stay where they were.
        for (var i = spans.Count - 1; i >= 0; i--)
            result.Remove(spans[i].Start, spans[i].End - spans[i].Start).Insert(spans[i].Start, spans[i].NewText);
        return result.ToString();
    }

    /// <summary>Where each edit's new text starts once all of them are made, in edit order.</summary>
    public static IReadOnlyList<(int Line, int Character)> NewStarts(IReadOnlyList<TextEdit> edits)
    {
        var sorted = edits.Select((e, i) => (Edit: e, Index: i))
            .OrderBy(e => e.Edit.StartLine).ThenBy(e => e.Edit.StartCharacter).ToList();
        var places = new (int Line, int Character)[edits.Count];
        var lineShift = 0;
        var (shiftedLine, characterShift) = (-1, 0);
        foreach (var (edit, index) in sorted)
        {
            var line = edit.StartLine + lineShift;
            var character = edit.StartCharacter + (edit.StartLine == shiftedLine ? characterShift : 0);
            places[index] = (line, character);

            var newLines = edit.NewText.Split('\n');
            var endCharacter = newLines.Length == 1 ? character + edit.NewText.Length : newLines[^1].Length;
            lineShift += newLines.Length - 1 - (edit.EndLine - edit.StartLine);
            (shiftedLine, characterShift) = (edit.EndLine, endCharacter - edit.EndCharacter);
        }
        return places;
    }

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            if (text[i] is '\n' or '\r') starts.Add(i + 1);
        }
        return starts;
    }

    private static int? Offset(string text, List<int> starts, int line, int character)
    {
        if (line < 0 || line >= starts.Count || character < 0) return null;
        var start = starts[line];
        var end = line + 1 < starts.Count ? starts[line + 1] : text.Length;
        while (end > start && text[end - 1] is '\n' or '\r') end--;
        return start + character <= end ? start + character : null;
    }
}
