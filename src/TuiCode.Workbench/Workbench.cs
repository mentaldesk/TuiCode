using TuiCode.Workbench.Parts;

namespace TuiCode.Workbench;

public sealed class Workbench : Window
{
    public SidebarPart Sidebar { get; }
    public EditorPart Editor { get; }
    public StatusBarPart StatusBar { get; }

    public Workbench(SidebarPart sidebar, EditorPart editor, StatusBarPart statusBar)
    {
        Sidebar = sidebar;
        Editor = editor;
        StatusBar = statusBar;

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

        sidebar.Explorer.FileActivated += (_, file) =>
        {
            editor.Open(file);
            statusBar.SetMessage(file.FullName);
        };

        editor.FileSaved += (_, file) =>
            statusBar.SetMessage($"Saved: {file.FullName}");
    }
}
