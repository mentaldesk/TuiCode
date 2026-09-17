using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Parts;

namespace TuiCode.Workbench;

public sealed class Workbench : Window
{
    public const string PlainTextName = "Plain Text";

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

        sidebar.Search.RootProvider = () => sidebar.Explorer.Root;
        sidebar.Search.OpenBuffers = new EditorBuffers(editor.Group);
        sidebar.Search.MatchActivated += (_, hit) => OpenMatch(hit.File, hit.Match);
        sidebar.Search.Message += (_, message) => statusBar.SetMessage(message);

        editor.FileSaved += (_, file) =>
            statusBar.SetMessage($"Saved: {file.FullName}");

        editor.Group.ActiveTabChanged += (_, tab) => ShowActiveFile(tab);
        editor.Group.GrammarChanged += (_, tab) =>
        {
            if (ReferenceEquals(tab, editor.Group.ActiveTab)) ShowActiveFile(tab);
        };
    }

    private void ShowActiveFile(EditorTab? tab)
    {
        if (tab is not null) StatusBar.SetMessage(tab.File.FullName);
        StatusBar.SetGrammar(tab is { HasSyntax: true } ? tab.Grammar?.Name ?? PlainTextName : null);
    }

    /// <summary>Open a file in the editor and focus it. Shared by the explorer and the Open dialog.</summary>
    public void OpenFile(IFileInfo file)
    {
        var tab = Editor.Open(file);
        tab.FocusContent();
        StatusBar.SetMessage(file.FullName);
    }

    /// <summary>Open a file with <paramref name="match"/> selected — where a search result lands.</summary>
    public void OpenMatch(IFileInfo file, TextMatch match)
    {
        var tab = Editor.Open(file);
        tab.Select(match);
        tab.FocusContent();
        StatusBar.SetMessage($"{file.FullName}:{match.Row + 1}:{match.Column + 1}");
    }

    /// <summary>
    /// Switch the workspace to <paramref name="directory"/>: close every open editor and re-root
    /// the explorer. Mirrors VS Code's "Open Folder" — the previous workspace is discarded.
    /// </summary>
    public void OpenFolder(IDirectoryInfo directory)
    {
        Editor.Group.CloseAll();
        Sidebar.Explorer.Open(directory);
        Sidebar.Search.RunSearch();
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
