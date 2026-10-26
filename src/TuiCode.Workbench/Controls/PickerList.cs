using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Loading;

namespace TuiCode.Workbench.Controls;

/// <summary>What <see cref="PickerDialog{T}"/> doesn't expose of its list.</summary>
internal static class PickerList
{
    private const int Chrome = 4;

    /// <summary>The columns a row has in a picker <paramref name="dialogWidth"/> wide.</summary>
    public static int Width(int dialogWidth) => dialogWidth - Chrome;

    /// <summary>What a picker that loads its rows says in the list while it has none to show.</summary>
    public static string Says<T>(this PickerDialog<T> picker) where T : class =>
        picker.SubViews.OfType<LoadStateView>().Single().Says;
}
