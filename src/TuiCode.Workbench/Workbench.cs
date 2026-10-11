using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Workbench;

public sealed class Workbench : Window
{
    public const string PlainTextName = "Plain Text";

    public SidebarPart Sidebar { get; }
    public EditorPart Editor { get; }
    public StatusBarPart StatusBar { get; }
    public MenuBar MenuBar { get; } = new();

    /// <summary>A dialog is open over the workbench: every view the host adds beside the four parts is one.</summary>
    public bool HasDialog => SubViews.Any(view => view != MenuBar && view != Sidebar && view != Editor && view != StatusBar);

    public bool IsSidebarVisible { get; private set; } = true;

    /// <summary>The width the user asked for (#209). A terminal too narrow for it clamps <see cref="DrawnSidebarWidth"/>, not this.</summary>
    public int SidebarWidth { get; private set; } = SidebarSizing.Default;

    /// <summary>The width actually laid out: <see cref="SidebarWidth"/> clamped to the terminal.</summary>
    public int DrawnSidebarWidth { get; private set; } = SidebarSizing.Default;

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
        sidebar.Y = Pos.Bottom(MenuBar);
        sidebar.Width = SidebarSizing.Default;
        sidebar.Height = Dim.Fill(1);

        editor.X = Pos.Right(sidebar);
        editor.Y = Pos.Bottom(MenuBar);
        editor.Width = Dim.Fill();
        editor.Height = Dim.Fill(1);

        statusBar.X = 0;
        statusBar.Y = Pos.AnchorEnd(1);
        statusBar.Width = Dim.Fill();
        statusBar.Height = 1;

        Add(MenuBar, sidebar, editor, statusBar);

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

        editor.Group.Copied += (_, outcome) => ShowCopy(outcome);

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

    /// <summary>The language servers for the open folder; null runs none.</summary>
    public LanguageServers? Languages
    {
        get;
        set
        {
            if (field is not null) field.StateChanged -= OnLanguageServerChanged;
            field = value;
            if (value is not null) value.StateChanged += OnLanguageServerChanged;
        }
    }

    private void OnLanguageServerChanged(object? sender, EventArgs e)
    {
        var tab = Editor.Group.ActiveTab;
        ShowLanguage(tab);
        if (tab is not null && Languages?.ServerFor(tab) is { State: LanguageServerState.Missing or LanguageServerState.Stopped })
            StatusBar.SetMessage(FileMessage(tab));
    }

    private void ShowActiveFile(EditorTab? tab)
    {
        if (tab is not null) StatusBar.SetMessage(FileMessage(tab));
        else if (Editor.Group.ActiveDiffTab is { } diff) StatusBar.SetMessage(diff.Title);
        else if (Editor.Group.ActiveDocumentTab is { } document) StatusBar.SetMessage(document.Title);
        ShowLanguage(tab);
        StatusBar.SetWrap(tab is { WordWrap: true });
    }

    private void ShowLanguage(EditorTab? tab)
    {
        if (tab is not { HasSyntax: true })
        {
            StatusBar.SetGrammar(null);
            return;
        }
        var name = tab.Grammar?.Name ?? PlainTextName;
        StatusBar.SetGrammar(Languages?.ServerFor(tab)?.State switch
        {
            LanguageServerState.Loading => $"{name} ◌ loading",
            LanguageServerState.Ready => $"{name} ● ready",
            LanguageServerState.Stopped => $"{name} stopped",
            _ => name,
        });
    }

    /// <summary>The file's path, or what its language server needs from the user.</summary>
    private string FileMessage(EditorTab tab) => Languages?.ServerFor(tab) switch
    {
        { State: LanguageServerState.Missing } server => server.Spec.NotInstalled,
        { State: LanguageServerState.Stopped, Failure: { } failure } server => $"{server.Spec.Name} language server stopped: {failure}",
        _ => tab.File.FullName,
    };

    /// <summary>How long a successful copy's message shows before the file path comes back.</summary>
    internal TimeSpan CopyMessageDuration { get; set; } = TimeSpan.FromSeconds(4);

    private void ShowCopy(CopyOutcome outcome)
    {
        if (outcome is CopyOutcome.Failed(var reason))
        {
            StatusBar.SetError($"Copy failed: {reason}");
            return;
        }
        var (lines, characters, throughTerminal) = (CopyOutcome.Copied)outcome;
        var message = $"Copied {Count(lines, "line")}  •  {Count(characters, "character")}{(throughTerminal ? " through the terminal" : "")}";
        StatusBar.SetMessage(message);
        App?.AddTimeout(CopyMessageDuration, () =>
        {
            if (StatusBar.Message == message) ShowActiveFile(Editor.Group.ActiveTab);
            return false;
        });

        static string Count(int n, string noun) => $"{n:N0} {noun}{(n == 1 ? "" : "s")}";
    }

    /// <summary>Show the active tab's cursor position and selection; the host calls this every main-loop iteration.</summary>
    public void ShowCursorPosition()
    {
        var tab = Editor.Group.ActiveTab;
        StatusBar.SetPosition(tab is null ? null : (tab.CursorRow, tab.CursorColumn));
        StatusBar.SetSelection(tab?.CaretCount ?? 1, tab?.CountSelection()?.Characters);
        StatusBar.SetDiffStatus(Editor.Group.ActiveDiffTab is { IsFocused: true } diff
            ? string.Join("  •  ", new[] { BranchReview.SpotLabel(diff.Review, Sidebar.Review.Review), diff.LineCounts, diff.ChangeStatus }.Where(part => !string.IsNullOrEmpty(part)))
            : null);
    }

    /// <summary>A file has been opened in the editor; the host moves the keys into it through the focus service (#228).</summary>
    public event EventHandler? FileOpened;

    /// <summary>Open a file in the editor. Shared by the explorer and the Open dialog.</summary>
    public void OpenFile(IFileInfo file)
    {
        var tab = Editor.Open(file);
        StatusBar.SetMessage(FileMessage(tab));
        FileOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Open a file with <paramref name="match"/> selected — where a search result lands.</summary>
    public void OpenMatch(IFileInfo file, TextMatch match)
    {
        var tab = Editor.Open(file);
        tab.Select(match);
        tab.RevealLines(match.Row, match.Row);
        StatusBar.SetMessage($"{file.FullName}:{match.Row + 1}:{match.Column + 1}");
        FileOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Open what the command line asked for (#263): the workspace, or "No folder open" when there's none (#451),
    /// then every file it named, each at the position it carried (#265), with the first one active (#266).
    /// Positions are 1-based on the command line; the cursor is 0-based.
    /// Call it from the running loop, not before <c>Application.Init</c>: a tab opened before the first
    /// layout loses the keyboard to a session-restored one, and there's no viewport to reveal in yet.
    /// </summary>
    public void OpenStartupTarget(StartupTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Workspace is { } workspace) OpenFolder(workspace, target.RememberTabs);
        else if (target.Files.Count == 0) throw new ArgumentException("Nothing to open.", nameof(target));
        else Sidebar.ShowNoFolder(true);
        foreach (var startup in target.Files)
            Place(startup);
        // Opening left the last tab active, and only the visible tab has a viewport to reveal in.
        if (target.Files is [var first, _, ..]) Place(first);
    }

    private void Place(StartupFile startup)
    {
        OpenFile(startup.File);
        if (startup.Position is not { } position || Editor.Group.ActiveTab is not { } tab) return;
        tab.MoveCursor(position.Line - 1, position.Column - 1);
        tab.RevealLines(position.Line - 1, position.Line - 1);
    }

    /// <summary>The folders this workspace has been switched to before, most recently used first.</summary>
    public IReadOnlyList<string> RecentFolders => _workspaceState?.Folders() ?? [];

    /// <summary>
    /// Switch the workspace to <paramref name="directory"/>: close every open editor, re-root the
    /// explorer and reopen the files that were open when this folder was last used (#13).
    /// </summary>
    public void OpenFolder(IDirectoryInfo directory, bool rememberTabs = true)
    {
        _workspaceFolder = null;
        Languages?.OpenFolder(directory.FullName);
        Editor.Group.CloseAll();
        Sidebar.ShowNoFolder(false);
        Sidebar.Explorer.Open(directory);
        Sidebar.Search.RunSearch();
        RefreshReviewIfShowing();
        StatusBar.SetMessage($"Opened folder: {directory.FullName}");

        if (!rememberTabs) return;
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

    /// <summary>Records the width the user asked for and lays out as much of it as the terminal allows.</summary>
    public void SetSidebarWidth(int width)
    {
        SidebarWidth = Math.Max(SidebarSizing.Min, width);
        ApplyDrawnSidebarWidth();
    }

    /// <summary>Moves the width by <paramref name="columns"/> from what's on screen, clamped. Returns the new width.</summary>
    public int NudgeSidebarWidth(int columns) => ResizeSidebarTo(DrawnSidebarWidth + columns);

    /// <summary>Sets the width to <paramref name="width"/>, clamped to the terminal. Returns the new width.</summary>
    public int ResizeSidebarTo(int width)
    {
        var clamped = SidebarSizing.Clamp(width, Viewport.Width);
        SetSidebarWidth(clamped);
        return clamped;
    }

    /// <summary>True when <paramref name="column"/> is the sidebar's right border — the one column a drag starts in (#252).</summary>
    public bool IsOnSidebarBorder(int column) => IsSidebarVisible && column == Sidebar.FrameToScreen().Right - 1;

    /// <summary>The width that puts the sidebar's right border under screen column <paramref name="column"/>.</summary>
    public int SidebarWidthForBorderAt(int column) => column - Sidebar.FrameToScreen().Left + 1;

    protected override void OnSubViewsLaidOut(LayoutEventArgs args)
    {
        base.OnSubViewsLaidOut(args);
        ApplyDrawnSidebarWidth();
    }

    private void ApplyDrawnSidebarWidth()
    {
        // Nothing to clamp against before the first layout, and the default would draw for a frame.
        var width = Viewport.Width > 0 ? SidebarSizing.Clamp(SidebarWidth, Viewport.Width) : SidebarWidth;
        if (width == DrawnSidebarWidth) return;
        DrawnSidebarWidth = width;
        Sidebar.Width = width;
        SetNeedsLayout();
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
