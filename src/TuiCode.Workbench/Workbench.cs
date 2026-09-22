using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Workbench;

public sealed class Workbench : Window
{
    public const string PlainTextName = "Plain Text";

    public SidebarPart Sidebar { get; }
    public EditorPart Editor { get; }
    public StatusBarPart StatusBar { get; }

    public bool IsSidebarVisible { get; private set; } = true;

    private readonly WorkspaceStateStore? _workspaceState;

    // Null while switching folders or shutting down, so closing the old folder's tabs isn't saved as its state.
    private string? _workspaceFolder;

    public Workbench(SidebarPart sidebar, EditorPart editor, StatusBarPart statusBar, WorkspaceStateStore? workspaceState = null)
    {
        _workspaceState = workspaceState;
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

        sidebar.Review.RootProvider = () => sidebar.Explorer.Root;

        editor.FileSaved += (_, file) =>
        {
            statusBar.SetMessage($"Saved: {file.FullName}");
            RefreshReviewIfShowing();
        };

        editor.Group.ActiveTabChanged += (_, tab) =>
        {
            ShowActiveFile(tab);
            SaveWorkspaceState();
        };
        editor.Group.GrammarChanged += (_, tab) =>
        {
            if (ReferenceEquals(tab, editor.Group.ActiveTab)) ShowActiveFile(tab);
        };
    }

    private void ShowActiveFile(EditorTab? tab)
    {
        if (tab is not null) StatusBar.SetMessage(tab.File.FullName);
        else if (Editor.Group.ActiveDiffTab is { } diff) StatusBar.SetMessage(diff.Title);
        else if (Editor.Group.ActiveDocumentTab is { } document) StatusBar.SetMessage(document.Title);
        StatusBar.SetGrammar(tab is { HasSyntax: true } ? tab.Grammar?.Name ?? PlainTextName : null);
    }

    /// <summary>The diff tab's keys, e.g. <c>Alt+↓ next  Alt+↑ prev</c>, from the live bindings.</summary>
    public string DiffKeysHint { get; set; } = string.Empty;

    /// <summary>Show the active tab's cursor position and selection; the host calls this every main-loop iteration.</summary>
    public void ShowCursorPosition()
    {
        var tab = Editor.Group.ActiveTab;
        StatusBar.SetPosition(tab is null ? null : (tab.CursorRow, tab.CursorColumn));
        StatusBar.SetSelection(tab?.CaretCount ?? 1, tab?.CountSelection()?.Characters);
        StatusBar.SetDiffStatus(Editor.Group.ActiveDiffTab is { IsFocused: true } diff
            ? string.Join("  •  ", new[] { diff.Review?.Label, diff.ChangeStatus, DiffKeysHint }.Where(part => !string.IsNullOrEmpty(part)))
            : null);
    }

    /// <summary>A file has been opened in the editor; the host moves the keys into it through the focus service (#228).</summary>
    public event EventHandler? FileOpened;

    /// <summary>Open a file in the editor. Shared by the explorer and the Open dialog.</summary>
    public void OpenFile(IFileInfo file)
    {
        Editor.Open(file);
        StatusBar.SetMessage(file.FullName);
        FileOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Open a file with <paramref name="match"/> selected — where a search result lands.</summary>
    public void OpenMatch(IFileInfo file, TextMatch match)
    {
        Editor.Open(file).Select(match);
        StatusBar.SetMessage($"{file.FullName}:{match.Row + 1}:{match.Column + 1}");
        FileOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Switch the workspace to <paramref name="directory"/>: close every open editor, re-root the
    /// explorer and reopen the files that were open when this folder was last used (#13).
    /// </summary>
    public void OpenFolder(IDirectoryInfo directory)
    {
        _workspaceFolder = null;
        Editor.Group.CloseAll();
        Sidebar.Explorer.Open(directory);
        Sidebar.Search.RunSearch();
        RefreshReviewIfShowing();
        StatusBar.SetMessage($"Opened folder: {directory.FullName}");

        RestoreOpenFiles(directory);
        _workspaceFolder = directory.FullName;
        SaveWorkspaceState();
    }

    /// <summary>Permanently delete <paramref name="item"/> and close its tabs, unsaved changes and all.</summary>
    public void Delete(IFileSystemInfo item)
    {
        Sidebar.Explorer.Delete(item);
        Editor.Group.CloseUnder(item.FullName);
        Sidebar.Search.RunSearch();
        StatusBar.SetMessage($"Deleted: {item.FullName}");
        SaveWorkspaceState();
    }

    /// <summary>Rename or move <paramref name="item"/>; its tabs follow. Returns <paramref name="item"/> itself when the path doesn't change.</summary>
    public IFileSystemInfo Move(IFileSystemInfo item, string relativePath)
    {
        var moved = Sidebar.Explorer.Move(item, relativePath);
        if (ReferenceEquals(moved, item)) return item;

        Editor.Group.Relocate(item.FullName, moved.FullName);
        ShowActiveFile(Editor.Group.ActiveTab);
        Sidebar.Search.RunSearch();
        StatusBar.SetMessage($"Renamed: {moved.FullName}");
        SaveWorkspaceState();
        return moved;
    }

    private void RestoreOpenFiles(IDirectoryInfo directory)
    {
        if (_workspaceState?.Load(directory.FullName) is not { } state) return;

        EditorTab? active = null;
        foreach (var path in state.Files)
        {
            var file = directory.FileSystem.FileInfo.New(path);
            if (!file.Exists) continue;
            try
            {
                var tab = Editor.Open(file);
                if (path == state.ActiveFile) active = tab;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        if (active is not null) Editor.Open(active.File);
    }

    private void SaveWorkspaceState()
    {
        if (_workspaceState is null || _workspaceFolder is null) return;
        var group = Editor.Group;
        _workspaceState.Save(_workspaceFolder, new WorkspaceState(
            group.Tabs.Select(t => t.File.FullName).ToList(),
            (group.ActiveTab ?? group.ActiveDiffTab?.Source)?.File.FullName));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _workspaceFolder = null;
        base.Dispose(disposing);
    }

    public void SetSidebarVisible(bool visible)
    {
        if (IsSidebarVisible == visible) return;
        IsSidebarVisible = visible;
        Sidebar.Visible = visible;
        Editor.X = visible ? Pos.Right(Sidebar) : 0;
        SetNeedsLayout();
        RefreshReviewIfShowing();
    }

    /// <summary>The Review tab refreshes itself when it's switched to; this covers the other times it needs to.</summary>
    public void RefreshReviewIfShowing()
    {
        if (IsSidebarVisible && Sidebar.ActiveTab == SidebarTab.Review) Sidebar.Review.Refresh();
    }

    public void ToggleSidebar() => SetSidebarVisible(!IsSidebarVisible);
}
