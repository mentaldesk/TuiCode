using MentalDesk.Tui.Paths;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TuiCode.Tests;

internal static class PathPickers
{
    public static PathPicker? Showing(View workbench) => workbench.SubViews.OfType<PathPicker>().SingleOrDefault();

    public static IReadOnlyList<string> Buttons(PathPicker picker) =>
        [.. picker.SubViews.OfType<Button>().Where(b => b.Visible).OrderBy(b => b.Frame.X).Select(ConfirmDialogs.Caption)];
}
