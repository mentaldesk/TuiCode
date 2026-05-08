using TuiCode.Workbench.Parts;

namespace TuiCode.Workbench;

public sealed class Workbench : Window
{
    public Workbench(SidebarPart sidebar, EditorPart editor, StatusBarPart statusBar)
    {
        Title = string.Empty;
        BorderStyle = LineStyle.None;

        sidebar.X = 0;
        sidebar.Y = 0;
        sidebar.Width = 30;
        sidebar.Height = Dim.Fill(1);

        editor.X = Pos.Right(sidebar);
        editor.Y = 0;
        editor.Width = Dim.Fill();
        editor.Height = Dim.Fill(1);

        statusBar.X = 0;
        statusBar.Y = Pos.AnchorEnd(1);
        statusBar.Width = Dim.Fill();
        statusBar.Height = 1;

        Add(sidebar, editor, statusBar);
    }
}
