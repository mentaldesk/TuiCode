using MentalDesk.Tui.Dialogs;

namespace TuiCode.Workbench.Controls;

/// <summary>What <see cref="PickerDialog{T}"/> doesn't expose of its list.</summary>
internal static class PickerList
{
    private const int Chrome = 4;

    /// <summary>The columns a row has in a picker <paramref name="dialogWidth"/> wide.</summary>
    public static int Width(int dialogWidth) => dialogWidth - Chrome;

    public static int? SelectedIndex<T>(this PickerDialog<T> picker) where T : class =>
        List(picker).SelectedItem;

    public static void Select<T>(this PickerDialog<T> picker, int index) where T : class =>
        List(picker).SelectedItem = index;

    private static ListView List<T>(PickerDialog<T> picker) where T : class =>
        picker.SubViews.OfType<ListView>().Single();
}
