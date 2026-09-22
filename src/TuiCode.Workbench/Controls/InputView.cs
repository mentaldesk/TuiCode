namespace TuiCode.Workbench.Controls;

/// <summary>
/// A dialog's multi-line field. It draws its own box, heavy while it has focus and single while it hasn't,
/// so a glance says which control the keys go to (#223). Where they land inside it is the terminal's own
/// cursor, as it is in the editor.
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

    protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocused, View? focused)
    {
        BorderStyle = newHasFocus ? Focused : Idle;
        base.OnHasFocusChanged(newHasFocus, previousFocused, focused);
    }
}
