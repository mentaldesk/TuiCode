using Terminal.Gui.Views;

namespace TuiCode.Abstractions;

/// <summary>
/// A <see cref="Tabs"/> that claims all four arrows and does nothing with them, so an arrow the focused
/// view can't use switches no tab and moves no focus (#254, #255). Both the editor group and the
/// sidebar are one of these.
/// </summary>
public class PaneTabs : Tabs
{
    public PaneTabs()
    {
        AddCommand(Command.Up, StayPut);
        AddCommand(Command.Down, StayPut);
        AddCommand(Command.Left, StayPut);
        AddCommand(Command.Right, StayPut);
    }

    private static bool? StayPut() => true;
}
