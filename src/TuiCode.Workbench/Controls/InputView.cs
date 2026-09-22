using Terminal.Gui.Drivers;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Workbench.Controls;

/// <summary>
/// A dialog's multi-line field. It draws its own box, heavy while it has focus and single while it hasn't,
/// and paints its own caret rather than leaving it to the terminal cursor, which a terminal profile or a
/// pale theme can leave invisible (#223).
/// </summary>
public sealed class InputView : TextView
{
    private const LineStyle Focused = LineStyle.Heavy;
    private const LineStyle Idle = LineStyle.Single;

    /// <summary>Rows and columns the box costs the dialog around it.</summary>
    public const int Frame = 2;

    public InputView()
    {
        // Otherwise Tab types a tab here instead of moving on to the rest of the dialog.
        TabKeyAddsTab = false;
        WordWrap = true;
        BorderStyle = Idle;
    }

    /// <summary>Whether the box is drawn as the one taking keys.</summary>
    public bool ShowsFocus => BorderStyle == Focused;

    /// <summary>Where the caret is painted, in viewport cells, or null while it isn't on screen.</summary>
    internal Point? Caret
    {
        get
        {
            var row = CurrentRow - Viewport.Y;
            if (row < 0 || row >= Viewport.Height || CurrentRow >= Lines) return null;
            var x = ColumnsBefore(GetLine(CurrentRow), CurrentColumn) - Viewport.X;
            return x < 0 || x >= Viewport.Width ? null : new Point(x, row);
        }
    }

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocused, View? focused)
    {
        BorderStyle = newHasFocus ? Focused : Idle;
        base.OnHasFocusChanged(newHasFocus, previousFocused, focused);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var handled = base.OnDrawingContent(context);
        DrawCaret();
        return handled;
    }

    // As the editor does for the carets a terminal can't draw: hide its cursor and paint the cell instead.
    private void DrawCaret()
    {
        var style = HasFocus ? CursorStyle.Hidden : DefaultCursorStyle;
        if (Cursor.Style != style) Cursor = Cursor with { Style = style };
        if (!HasFocus || Caret is not { } point) return;

        var line = GetLine(CurrentRow);
        var under = CurrentColumn < line.Count ? line[CurrentColumn].Grapheme : " ";
        var attribute = GetAttributeForRole(VisualRole.Editable);
        SetAttribute(new Attribute(attribute.Background, attribute.Foreground, attribute.Style));
        AddStr(point.X, point.Y, under == "\t" ? " " : under);
    }

    private int ColumnsBefore(List<Cell> line, int column)
    {
        var columns = 0;
        for (var i = 0; i < Math.Min(column, line.Count); i++)
        {
            var grapheme = line[i].Grapheme;
            columns += grapheme == "\t"
                ? TabWidth > 0 ? TabWidth - columns % TabWidth : 0
                : Math.Max(grapheme.GetColumns(), 1);
        }
        return columns;
    }
}
