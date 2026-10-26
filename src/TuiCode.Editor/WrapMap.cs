namespace TuiCode.Editor;

/// <summary>
/// Every line's rows and the screen row each line starts on, re-measuring only lines that changed: wrapped (#378),
/// or one row a line, and none for a line a fold hides (#477).
/// </summary>
internal sealed class WrapMap
{
    private static readonly int[] OneRow = [0];

    private Dictionary<string, int[]> _byText = new(ReferenceEqualityComparer.Instance);
    private string[] _texts = [];
    private bool[]? _hidden;
    private int[][] _starts = [OneRow];
    private int[] _firstRow = [0, 1];

    /// <summary>The width lines wrap at, or 0 when they don't.</summary>
    public int Width { get; private set; }
    public int TabWidth { get; private set; }

    public int Rows => _firstRow[^1];

    public int Lines => _starts.Length;

    /// <summary>
    /// A line keeps its rows while its text is the same string instance, as <see cref="LineSnapshot"/> keeps it.
    /// A <paramref name="width"/> of 0 doesn't wrap; a line <paramref name="hidden"/> marks takes no rows. False when no rows moved.
    /// </summary>
    public bool Update(IReadOnlyList<string> lines, Func<int, IReadOnlyList<string>> graphemes, int width, int tabWidth, bool[]? hidden = null)
    {
        var hiddenChanged = !ReferenceEquals(hidden, _hidden);
        if (width != Width || tabWidth != TabWidth)
        {
            (Width, TabWidth) = (width, tabWidth);
            _byText = new Dictionary<string, int[]>(ReferenceEqualityComparer.Instance);
            _texts = [];
        }
        else if (!hiddenChanged && Same(lines))
        {
            return false;
        }

        var byText = new Dictionary<string, int[]>(width == 0 ? 0 : lines.Count, ReferenceEqualityComparer.Instance);
        var starts = new int[lines.Count][];
        var changed = hiddenChanged || lines.Count != _starts.Length;
        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i];
            int[] rows;
            if (width == 0)
            {
                rows = OneRow;
            }
            else
            {
                if (!byText.TryGetValue(text, out rows!) && !_byText.TryGetValue(text, out rows!))
                    rows = text.Length == 0 ? OneRow : WrapLayout.RowStarts(graphemes(i), width, tabWidth);
                byText.TryAdd(text, rows);
            }
            starts[i] = rows;
            if (!changed && !ReferenceEquals(rows, _starts[i])) changed = true;
        }

        _byText = byText;
        _texts = [.. lines];
        _hidden = hidden;
        if (!changed) return false;

        _starts = starts.Length == 0 ? [OneRow] : starts;
        _firstRow = new int[_starts.Length + 1];
        for (var i = 0; i < _starts.Length; i++)
            _firstRow[i + 1] = _firstRow[i] + (IsHidden(i) ? 0 : _starts[i].Length);
        return true;
    }

    public int[] Starts(int line) => _starts[line];

    public bool IsHidden(int line) => _hidden is { } hidden && line < hidden.Length && hidden[line];

    /// <summary>The row <paramref name="line"/> starts on; for a hidden line, the row after the fold that hides it.</summary>
    public int FirstRow(int line) => _firstRow[Math.Clamp(line, 0, Lines)];

    /// <summary>The line showing on <paramref name="screenRow"/>, never a hidden one, and which of its rows that is.</summary>
    public (int Line, int Row) At(int screenRow)
    {
        screenRow = Math.Clamp(screenRow, 0, Math.Max(Rows - 1, 0));
        // The last line starting at or before the row: hidden lines start where the line after them does.
        int low = 0, high = Lines - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (_firstRow[mid] <= screenRow) low = mid;
            else high = mid - 1;
        }
        while (low > 0 && IsHidden(low)) low--;
        return (low, screenRow - _firstRow[low]);
    }

    private bool Same(IReadOnlyList<string> lines)
    {
        if (lines.Count != _texts.Length) return false;
        for (var i = 0; i < lines.Count; i++)
            if (!ReferenceEquals(lines[i], _texts[i])) return false;
        return true;
    }
}
