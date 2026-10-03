using Terminal.Gui.Text;

namespace TuiCode.Editor;

/// <summary>Where a wrapped line breaks (#378). Columns are grapheme indexes; tabs reach the next stop from the start of their row.</summary>
internal static class WrapLayout
{
    /// <summary>The grapheme each row starts at. Spaces at a break may run past the edge rather than start a row.</summary>
    public static int[] RowStarts(IReadOnlyList<string> graphemes, int width, int tabWidth)
    {
        width = Math.Max(width, 1);
        List<int>? starts = null;
        var (start, column, lastSpace) = (0, 0, -1);
        for (var i = 0; i < graphemes.Count; i++)
        {
            var grapheme = graphemes[i];
            var cells = Cells(grapheme, column, tabWidth);
            if (column + cells > width && i > start && grapheme != " ")
            {
                var afterSpace = lastSpace >= start ? lastSpace + 1 : i;
                var carried = Columns(graphemes, afterSpace, i, tabWidth);
                (start, column) = afterSpace < i && carried + Cells(grapheme, carried, tabWidth) <= width ? (afterSpace, carried) : (i, 0);
                (starts ??= [0]).Add(start);
                lastSpace = -1;
                cells = Cells(grapheme, column, tabWidth);
            }
            column += cells;
            if (grapheme is " " or "\t") lastSpace = i;
        }
        return starts?.ToArray() ?? [0];
    }

    public static int RowOf(int[] starts, int column)
    {
        var row = Array.BinarySearch(starts, column);
        return row >= 0 ? row : ~row - 1;
    }

    public static int X(IReadOnlyList<string> graphemes, int[] starts, int column, int tabWidth) =>
        Columns(graphemes, starts[RowOf(starts, column)], column, tabWidth);

    /// <summary>The column under cell <paramref name="x"/>; past the end of a row that isn't the line's last, its last column.</summary>
    public static int ColumnAt(IReadOnlyList<string> graphemes, int[] starts, int row, int x, int tabWidth)
    {
        var start = starts[row];
        var end = row + 1 < starts.Length ? starts[row + 1] : graphemes.Count;
        var column = 0;
        for (var i = start; i < end; i++)
        {
            column += Cells(graphemes[i], column, tabWidth);
            if (column > x) return i;
        }
        return row + 1 < starts.Length ? end - 1 : end;
    }

    private static int Columns(IReadOnlyList<string> graphemes, int from, int to, int tabWidth)
    {
        var column = 0;
        for (var i = from; i < to; i++)
            column += Cells(graphemes[i], column, tabWidth);
        return column;
    }

    private static int Cells(string grapheme, int column, int tabWidth) =>
        grapheme == "\t" ? tabWidth > 0 ? tabWidth - column % tabWidth : 0 : Math.Max(grapheme.GetColumns(), 1);
}
