using Terminal.Gui.Drivers;
using Terminal.Gui.Text;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

/// <summary>
/// A <see cref="TextView"/> that can paint its own carets, a bar down the cell each one is before, for the
/// carets a terminal won't show a cursor on: the editor's secondary ones (#106) and a dialog field's (#223).
/// </summary>
public abstract class CaretTextView : TextView
{
    /// <summary>What a painted caret looks like: the terminal's own bar, at the left edge of the cell it's before.</summary>
    public const string Bar = "\u258f";

    protected CaretTextView() =>
        UnwrappedCursorPositionChanged += (_, _) =>
        {
            if (PaintsCarets) SetNeedsDraw();
        };

    /// <summary>Whether the carets are painted here rather than left to the terminal cursor.</summary>
    protected abstract bool PaintsCarets { get; }

    /// <summary>The insertion points to paint, each (column X, row Y) in the drawn model.</summary>
    protected abstract IEnumerable<Point> CaretPositions { get; }

    /// <summary>Paints them, hiding the terminal cursor so it isn't a second caret. Call while drawing content.</summary>
    protected void DrawCarets()
    {
        var style = PaintsCarets ? CursorStyle.Hidden : DefaultCursorStyle;
        if (Cursor.Style != style) Cursor = Cursor with { Style = style };
        if (!PaintsCarets) return;

        var attribute = GetAttributeForRole(VisualRole.Editable);
        foreach (var caret in CaretPositions)
        {
            if (ViewportPosition(caret) is not { } point) continue;
            SetAttribute(attribute);
            AddStr(point.X, point.Y, Bar);
        }
    }

    /// <summary>Where a caret lands in viewport cells, or null while it's off screen.</summary>
    protected Point? ViewportPosition(Point caret)
    {
        var row = caret.Y - Viewport.Y;
        if (row < 0 || row >= Viewport.Height || caret.Y >= Lines) return null;
        var line = GetLine(caret.Y);
        var x = ColumnsBefore(line, Math.Min(caret.X, line.Count)) - Viewport.X;
        return x < 0 || x >= Viewport.Width ? null : new Point(x, row);
    }

    private int ColumnsBefore(List<Cell> line, int column)
    {
        var columns = 0;
        for (var i = 0; i < column; i++)
        {
            var grapheme = line[i].Grapheme;
            columns += grapheme == "\t"
                ? TabWidth > 0 ? TabWidth - columns % TabWidth : 0
                : Math.Max(grapheme.GetColumns(), 1);
        }
        return columns;
    }
}
