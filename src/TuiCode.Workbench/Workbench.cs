using TuiCode.Workbench.Parts;

namespace TuiCode.Workbench;

public sealed class Workbench : Window
{
    public SidebarPart Sidebar { get; }
    public EditorPart Editor { get; }
    public StatusBarPart StatusBar { get; }

    public bool IsSidebarVisible { get; private set; } = true;

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

        sidebar.Explorer.FileActivated += (_, file) => OpenFile(file);

        editor.FileSaved += (_, file) =>
            statusBar.SetMessage($"Saved: {file.FullName}");
    }

    /// <summary>Open a file in the editor and focus it. Shared by the explorer and the Open dialog.</summary>
    public void OpenFile(IFileInfo file)
    {
        var tab = Editor.Open(file);
        tab.FocusContent();
        StatusBar.SetMessage(file.FullName);
    }

    /// <summary>
    /// Switch the workspace to <paramref name="directory"/>: close every open editor and re-root
    /// the explorer. Mirrors VS Code's "Open Folder" — the previous workspace is discarded.
    /// </summary>
    public void OpenFolder(IDirectoryInfo directory)
    {
        Editor.Group.CloseAll();
        Sidebar.Explorer.Open(directory);
        StatusBar.SetMessage($"Opened folder: {directory.FullName}");
    }

    public void SetSidebarVisible(bool visible)
    {
        if (IsSidebarVisible == visible) return;
        IsSidebarVisible = visible;
        Sidebar.Visible = visible;
        Editor.X = visible ? Pos.Right(Sidebar) : 0;
        SetNeedsLayout();
    }

    public void ToggleSidebar() => SetSidebarVisible(!IsSidebarVisible);
}
