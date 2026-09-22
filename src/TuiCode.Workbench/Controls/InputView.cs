using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Workbench.Controls;

/// <summary>
/// A dialog's multi-line field. It draws its own box, heavy while it has focus and single while it hasn't,
/// and paints its caret the way the editor paints the carets a terminal can't draw, which a terminal profile
/// or a pale theme can otherwise leave invisible (#223).
/// </summary>
public sealed class InputView : CaretTextView
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

    protected override bool PaintsCarets => HasFocus;

    protected override IEnumerable<Point> CaretPositions => [InsertionPoint];

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocused, View? focused)
    {
        BorderStyle = newHasFocus ? Focused : Idle;
        base.OnHasFocusChanged(newHasFocus, previousFocused, focused);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var handled = base.OnDrawingContent(context);
        DrawCarets();
        return handled;
    }
}
