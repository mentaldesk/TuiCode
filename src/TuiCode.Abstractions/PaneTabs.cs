using System.Text;
using Terminal.Gui.ViewBase;
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

    private static readonly Rune NoHotKey = (Rune)0xffff;

    // TG 2.5 turns the tab's hotkey off but not its header's, which hides the '_' in COMMIT_EDITMSG (#452).
    protected override void OnSubViewAdded(View view)
    {
        base.OnSubViewAdded(view);
        if (view.Border.View is not { } border) return;
        foreach (var header in border.SubViews.OfType<ITitleView>().OfType<View>())
        {
            header.HotKeySpecifier = NoHotKey;
            ShowTitle(view);
        }
        border.SubViewAdded += (_, e) =>
        {
            if (e.SubView is ITitleView) e.SubView.HotKeySpecifier = NoHotKey;
        };
    }

    /// <summary>Redraws <paramref name="tab"/>'s header after its title changes; TG would place it at the old width.</summary>
    public static void ShowTitle(View tab)
    {
        if (tab.Border.View is not BorderView { TitleView: { } header }) return;
        header.Text = tab.Title;
        header.TextFormatter.ConstrainToSize = null;
        if (header is ITitleView title) title.MeasuredTabLength = 0;
        tab.SetNeedsLayout();
    }
}
