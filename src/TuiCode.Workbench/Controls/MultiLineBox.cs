using MentalDesk.Tui.Fields;

namespace TuiCode.Workbench.Controls;

/// <summary>A dialog's multi-line field, drawn heavy while it has focus and single while it hasn't (#223).</summary>
public static class MultiLineBox
{
    /// <summary>Rows and columns the box costs the dialog around it.</summary>
    public const int Frame = 2;

    // The library only recolours the border on focus.
    public static MultiLineField HeavyWhileFocused(this MultiLineField field)
    {
        field.HasFocusChanged += (_, focus) => field.BorderStyle = focus.NewValue ? LineStyle.Heavy : LineStyle.Single;
        return field;
    }
}
