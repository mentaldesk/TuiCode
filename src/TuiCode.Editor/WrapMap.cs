namespace TuiCode.Editor;

/// <summary>Every line's wrapped rows (#378) and the screen row each line starts on, re-measuring only lines that changed.</summary>
internal sealed class WrapMap
{
    private static readonly int[] OneRow = [0];

    private Dictionary<string, int[]> _byText = new(ReferenceEqualityComparer.Instance);
    private string[] _texts = [];
    private int[][] _starts = [OneRow];
    private int[] _firstRow = [0, 1];

    public int Width { get; private set; }
    public int TabWidth { get; private set; }

    public int Rows => _firstRow[^1];

    public int Lines => _starts.Length;

    /// <summary>A line keeps its rows while its text is the same string instance, as <see cref="LineSnapshot"/> keeps it. False when no rows moved.</summary>
    public bool Update(IReadOnlyList<string> lines, Func<int, IReadOnlyList<string>> graphemes, int width, int tabWidth)
    {
        if (width != Width || tabWidth != TabWidth)
        {
            (Width, TabWidth) = (width, tabWidth);
            _byText = new Dictionary<string, int[]>(ReferenceEqualityComparer.Instance);
            _texts = [];
        }
        else if (Same(lines))
        {
            return false;
        }

        var byText = new Dictionary<string, int[]>(lines.Count, ReferenceEqualityComparer.Instance);
        var starts = new int[lines.Count][];
        var changed = lines.Count != _starts.Length;
        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i];
            if (!byText.TryGetValue(text, out var rows) && !_byText.TryGetValue(text, out rows))
                rows = text.Length == 0 ? OneRow : WrapLayout.RowStarts(graphemes(i), width, tabWidth);
            byText.TryAdd(text, rows);
            starts[i] = rows;
            if (!changed && !ReferenceEquals(rows, _starts[i])) changed = true;
        }

        _byText = byText;
        _texts = [.. lines];
        if (!changed) return false;

        _starts = starts.Length == 0 ? [OneRow] : starts;
        _firstRow = new int[_starts.Length + 1];
        for (var i = 0; i < _starts.Length; i++)
            _firstRow[i + 1] = _firstRow[i] + _starts[i].Length;
        return true;
    }

    public int[] Starts(int line) => _starts[line];

    public int FirstRow(int line) => _firstRow[Math.Clamp(line, 0, Lines)];

    public (int Line, int Row) At(int screenRow)
    {
        screenRow = Math.Clamp(screenRow, 0, Rows - 1);
        var line = Array.BinarySearch(_firstRow, 0, Lines, screenRow);
        if (line < 0) line = ~line - 1;
        return (line, screenRow - _firstRow[line]);
    }

    private bool Same(IReadOnlyList<string> lines)
    {
        if (lines.Count != _texts.Length) return false;
        for (var i = 0; i < lines.Count; i++)
            if (!ReferenceEquals(lines[i], _texts[i])) return false;
        return true;
    }
}
