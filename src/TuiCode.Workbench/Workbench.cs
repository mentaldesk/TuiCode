using MentalDesk.Tui;
using MentalDesk.Tui.Chrome;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Assistant;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.History;
using TuiCode.Workbench.Languages;
using TuiCode.Workbench.Navigation;
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
    public PanelPart Panel { get; }
    public HistoryView History => Panel.History;

    /// <summary>The AI assistance sidebar on the right (#619).</summary>
    public AssistantPart AssistantSidebar { get; }

    public AssistantView Assistant => AssistantSidebar.Assistant;

    /// <summary>A dialog is open over the workbench: every view the host adds beside the six parts is one.</summary>
    public bool HasDialog => SubViews.Any(view => view != MenuBar && view != Sidebar && view != Editor && view != Panel && view != AssistantSidebar
        && view != StatusBar && view is not QuickInfoView);

    /// <summary>The user has the History panel on (#534); it shows only while the active tab has history.</summary>
    public bool IsHistoryOn { get; private set; }

    public bool IsHistoryShowing { get; private set; }

    public event EventHandler? HistoryOnChanged;

    public event EventHandler? HistoryShowingChanged;

    public bool IsAssistantShowing => AssistantSidebar.Visible;

    public event EventHandler? AssistantShowingChanged;

    public bool IsSidebarVisible { get; private set; } = true;

    /// <summary>The width the user asked for (#209). A terminal too narrow for it clamps <see cref="DrawnSidebarWidth"/>, not this.</summary>
    public int SidebarWidth { get; private set; } = SidebarSizing.Default;

    /// <summary>The width actually laid out: <see cref="SidebarWidth"/> clamped to the terminal.</summary>
    public int DrawnSidebarWidth { get; private set; } = SidebarSizing.Default;

    /// <summary>The AI assistance sidebar's width the user dragged it to; null opens it at <see cref="AssistantSizing.DefaultFor"/>.</summary>
    public int? AssistantWidth { get; private set; }

    /// <summary>The AI assistance sidebar's width laid out, clamped to the terminal; 0 while it's hidden.</summary>
    public int DrawnAssistantWidth { get; private set; }

    private readonly WorkspaceStateStore? _workspaceState;
    private readonly FilePositionStore? _positions;

    // Null while switching folders or shutting down, so closing the old folder's tabs isn't saved as its state.
    private string? _workspaceFolder;

    public Workbench(SidebarPart sidebar, EditorPart editor, StatusBarPart statusBar, WorkspaceStateStore? workspaceState = null, FilePositionStore? positions = null,
        PanelPart? panel = null, RecoveryStore? recovery = null, AssistantPart? assistant = null)
    {
        Panel = panel ?? new PanelPart();
        AssistantSidebar = assistant ?? new AssistantPart();
        _workspaceState = workspaceState;
        _positions = positions;
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

        Panel.X = Pos.Right(sidebar);
        Panel.Y = Pos.Bottom(editor);
        Panel.Width = Dim.Fill();
        Panel.Height = 0;
        Panel.Visible = false;

        AssistantSidebar.X = Pos.AnchorEnd(0);
        AssistantSidebar.Y = Pos.Bottom(MenuBar);
        AssistantSidebar.Width = 0;
        AssistantSidebar.Height = Dim.Fill(1);
        AssistantSidebar.Visible = false;

        statusBar.X = 0;
        statusBar.Y = Pos.AnchorEnd(1);
        statusBar.Width = Dim.Fill();
        statusBar.Height = 1;

        Add(MenuBar, sidebar, editor, Panel, AssistantSidebar, statusBar);

        sidebar.Explorer.FileActivated += (_, file) => OpenFile(file);

        sidebar.Search.RootProvider = () => sidebar.Explorer.Root;
        sidebar.Search.OpenBuffers = new EditorBuffers(editor.Groups);
        sidebar.Search.MatchActivated += (_, hit) => OpenMatch(hit.File, hit.Match);
        sidebar.Search.Message += (_, message) => statusBar.ShowMessage(message);

        sidebar.Review.RootProvider = () => sidebar.Explorer.Root;
        sidebar.Review.RefreshFailed += (_, error) =>
        {
            statusBar.Withdraw(_reviewFailure);
            _reviewFailure = statusBar.ShowNews($"Couldn't refresh the review: {error}", Severity.Error);
        };
        sidebar.Review.Refreshed += (_, _) => statusBar.Withdraw(_reviewFailure);

        editor.FileSaved += (_, file) =>
        {
            statusBar.ShowMessage($"Saved: {file.FullName}");
            RefreshReviewIfShowing();
        };

        editor.Groups.Copied += (_, outcome) => ShowCopy(outcome);

        if (recovery is not null)
        {
            UnsavedEdits = new UnsavedEdits(editor.Groups, recovery);
            UnsavedEdits.Recovered += (_, _) => OnRecovered();
        }

        editor.Groups.EditorOpening += (_, tab) =>
        {
            if (tab.IsRecovered) return;
            if (_positions?.Load(tab.File.FullName) is { } position) tab.RestoreViewPosition(position);
        };
        editor.Groups.EditorClosing += (_, tab) => RememberPositions([tab]);

        editor.Groups.ActiveTabChanged += (_, tab) =>
        {
            ShowActiveFile(tab);
            SaveWorkspaceState();
        };
        editor.Groups.GrammarChanged += (_, tab) =>
        {
            if (ReferenceEquals(tab, editor.Group.ActiveTab)) ShowActiveFile(tab);
        };
    }

    /// <summary>Keeps dirty tabs' edits through TuiCode ending unexpectedly (#579); null keeps nothing.</summary>
    public UnsavedEdits? UnsavedEdits { get; }

    private int _recovered;
    private StatusMessage? _reviewFailure;
    private StatusMessage? _languageTrouble;
    private LanguageServer? _troubledServer;

    // Tabs come back one by one, so the count waits for the loop to finish opening them.
    private void OnRecovered()
    {
        if (_recovered++ > 0 || App is not { } app) return;
        app.AddTimeout(TimeSpan.Zero, () =>
        {
            StatusBar.ShowNews($"⟲ Recovered unsaved edits in {Plural.Count(_recovered, "file")}");
            _recovered = 0;
            return false;
        });
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
        ShowLanguageTrouble(tab);
    }

    // Once per server: a restarted server is a new one, and takes the old one's trouble off.
    private void ShowLanguageTrouble(EditorTab? tab)
    {
        if (tab is null || Languages?.ServerFor(tab) is not { } server || server == _troubledServer) return;
        var trouble = LanguageTrouble(server);
        if (trouble is null && server.Spec.LanguageId != _troubledServer?.Spec.LanguageId) return;
        StatusBar.Withdraw(_languageTrouble);
        (_troubledServer, _languageTrouble) = trouble is var (text, severity) ? (server, StatusBar.ShowNews(text, severity)) : (null, null);
    }

    private void ShowActiveFile(EditorTab? tab)
    {
        StatusBar.SetResting(tab?.File.FullName ?? Editor.Group.ActiveDiffTab?.Title ?? Editor.Group.ActiveDocumentTab?.Title);
        ShowLanguage(tab);
        ShowLanguageTrouble(tab);
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

    /// <summary>What the language server needs from the user, if anything.</summary>
    private static (string Text, Severity Severity)? LanguageTrouble(LanguageServer running) => running switch
    {
        { State: LanguageServerState.Missing } server => (server.Spec.NotInstalled, Severity.Info),
        { State: LanguageServerState.Stopped, Failure: { } failure } server => ($"{server.Spec.Name} language server stopped: {failure}", Severity.Error),
        _ => null,
    };

    private void ShowCopy(CopyOutcome outcome)
    {
        if (outcome is CopyOutcome.Failed(var reason))
        {
            StatusBar.ShowMessage($"Copy failed: {reason}", Severity.Error);
            return;
        }
        var (lines, characters, throughTerminal) = (CopyOutcome.Copied)outcome;
        var message = $"Copied {Plural.Count(lines, "line")}  •  {Plural.Count(characters, "character")}{(throughTerminal ? " through the terminal" : "")}";
        StatusBar.ShowMessage(message);
    }

    /// <summary>Show the active tab's cursor position and selection; the host calls this every main-loop iteration.</summary>
    public void ShowCursorPosition()
    {
        var tab = Editor.Group.ActiveTab;
        StatusBar.SetPosition(tab is null ? null : (tab.CursorRow, tab.CursorColumn));
        StatusBar.SetSelection(tab?.CaretCount ?? 1, tab?.CountSelection()?.Characters);
        StatusBar.SetDiffStatus(Editor.Group.ActiveDiffTab is { IsFocused: true } diff
            ? string.Join("  •  ", new[] { BranchReview.SpotLabel(diff.Review, Sidebar.Review.Review), diff.History?.Label, diff.LineCounts, diff.ChangeStatus }.Where(part => !string.IsNullOrEmpty(part)))
            : null);
    }

    /// <summary>A file has been opened in the editor; the host moves the keys into it through the focus service (#228).</summary>
    public event EventHandler? FileOpened;

    /// <summary>A folder's saved tabs are back, and <see cref="EditorGroups.Focused"/> is the group that was focused.</summary>
    public event EventHandler? TabsRestored;

    /// <summary>Open a file in the editor. Shared by the explorer and the Open dialog.</summary>
    public void OpenFile(IFileInfo file)
    {
        Editor.Open(file);
        FileOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reopens the most recently closed file tab where it was (#578).</summary>
    public void ReopenClosedTab()
    {
        if (Editor.Groups.ReopenClosed() is null) return;
        FileOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Open a file with <paramref name="match"/> selected — where a search result lands.</summary>
    public void OpenMatch(IFileInfo file, TextMatch match)
    {
        var tab = Editor.Open(file);
        tab.Select(match);
        tab.RevealLines(match.Row, match.Row);
        StatusBar.ShowMessage($"{file.FullName}:{match.Row + 1}:{match.Column + 1}");
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
        SetHistoryOn(false);
        ShowAssistant(false);
        Languages?.OpenFolder(directory.FullName);
        RememberPositions(Editor.Groups.Tabs);
        UnsavedEdits?.Discard(Editor.Groups.Tabs);
        Editor.Groups.CloseAll();
        Sidebar.ShowNoFolder(false);
        Sidebar.Explorer.Open(directory);
        Sidebar.Search.RunSearch();
        RefreshReviewIfShowing();
        StatusBar.ShowMessage($"Opened folder: {directory.FullName}");

        if (!rememberTabs) return;
        RestoreOpenFiles(directory);
        _workspaceFolder = directory.FullName;
        SaveWorkspaceState();
    }

    /// <summary>Closes the focused group's tabs but its active one, unsaved changes and all.</summary>
    public void CloseOtherTabs()
    {
        Editor.Group.Close(Editor.Group.Others);
        SaveWorkspaceState();
    }

    /// <summary>Closes every tab in both groups, unsaved changes and all.</summary>
    public void CloseAllTabs()
    {
        foreach (var group in Editor.Groups.All.Reverse())
            group.Close([.. group.TabCollection]);
        SaveWorkspaceState();
    }

    /// <summary>Permanently delete <paramref name="item"/> and close its tabs, unsaved changes and all.</summary>
    public void Delete(IFileSystemInfo item)
    {
        Sidebar.Explorer.Delete(item);
        AfterRemoving(item, $"Deleted: {item.FullName}");
    }

    /// <summary>Move <paramref name="item"/> to <paramref name="trash"/>; throws, changing nothing, when it can't.</summary>
    public void Trash(IFileSystemInfo item, ITrash trash)
    {
        Sidebar.Explorer.Trash(item, trash);
        AfterRemoving(item, $"🗑 Moved '{item.Name}' to the {trash.Name}");
    }

    private void AfterRemoving(IFileSystemInfo item, string message)
    {
        Editor.Groups.CloseUnder(item.FullName);
        Sidebar.Search.RunSearch();
        StatusBar.ShowMessage(message);
        SaveWorkspaceState();
    }

    /// <summary>Rename or move <paramref name="item"/>; its tabs follow. Returns <paramref name="item"/> itself when the path doesn't change.</summary>
    public IFileSystemInfo Move(IFileSystemInfo item, string relativePath)
    {
        var moved = Sidebar.Explorer.Move(item, relativePath);
        if (ReferenceEquals(moved, item)) return item;

        Editor.Groups.Relocate(item.FullName, moved.FullName);
        ShowActiveFile(Editor.Group.ActiveTab);
        Sidebar.Search.RunSearch();
        StatusBar.ShowMessage($"Renamed: {moved.FullName}");
        SaveWorkspaceState();
        return moved;
    }

    private void RestoreOpenFiles(IDirectoryInfo directory)
    {
        if (_workspaceState?.Load(directory.FullName) is not { } state) return;

        var groups = Editor.Groups;
        var first = Restore(groups.First, state.Files, state.ActiveFile);
        var second = state.Second is { } split ? Restore(groups.Second, split.Files, split.ActiveFile) : null;
        if (first is not null) groups.Holding(first)?.Focus(first.File.FullName);
        if (second is not null) groups.Holding(second)?.Focus(second.File.FullName);
        groups.Focused = state.SecondFocused && groups.IsSplit ? groups.Second : groups.First;
        TabsRestored?.Invoke(this, EventArgs.Empty);
        SetHistoryOn(state.History);
        ShowAssistant(state.Assistant);

        EditorTab? Restore(EditorGroup group, IReadOnlyList<string> files, string? activeFile)
        {
            EditorTab? active = null;
            foreach (var path in files)
            {
                var file = directory.FileSystem.FileInfo.New(path);
                if ((!file.Exists && UnsavedEdits?.Has(path) != true) || groups.Holding(path) is not null) continue;
                try
                {
                    var tab = group.OpenOrFocus(file);
                    if (path == activeFile) active = tab;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
            return active;
        }
    }

    private void SaveWorkspaceState()
    {
        if (_workspaceState is null || _workspaceFolder is null) return;
        var groups = Editor.Groups;
        _workspaceState.Save(_workspaceFolder, new WorkspaceState(Files(groups.First), ActiveFile(groups.First))
        {
            Second = groups.IsSplit ? new WorkspaceGroup(Files(groups.Second), ActiveFile(groups.Second)) : null,
            SecondFocused = groups.IsSplit && ReferenceEquals(groups.Focused, groups.Second),
            History = IsHistoryOn,
            Assistant = IsAssistantShowing,
        });

        static List<string> Files(EditorGroup group) => [.. group.Tabs.Select(t => t.File.FullName)];
        static string? ActiveFile(EditorGroup group) => (group.ActiveTab ?? group.ActiveDiffTab?.Source)?.File.FullName;
    }

    private void RememberPositions(IEnumerable<EditorTab> tabs) =>
        _positions?.Save(tabs.Select(tab => (tab.File.FullName, tab.ViewPosition)));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsavedEdits?.Dispose();
            _workspaceFolder = null;
            RememberPositions(Editor.Groups.Tabs);
        }
        base.Dispose(disposing);
    }

    /// <summary>Records the width the user asked for and lays out as much of it as the terminal allows.</summary>
    public void SetSidebarWidth(int width)
    {
        SidebarWidth = Math.Max(SidebarSizing.Min, width);
        ApplySidebarWidths();
    }

    /// <summary>The widest the left sidebar may be drawn now, leaving the AI sidebar its floor while it's open.</summary>
    public int MaxSidebarWidth => SidebarSizing.MaxFor(Viewport.Width, IsAssistantShowing ? AssistantSizing.Min : 0);

    /// <summary>Moves the width by <paramref name="columns"/> from what's on screen, clamped. Returns the new width.</summary>
    public int NudgeSidebarWidth(int columns) => ResizeSidebarTo(DrawnSidebarWidth + columns);

    /// <summary>Sets the width to <paramref name="width"/>, clamped to the terminal. Returns the new width.</summary>
    public int ResizeSidebarTo(int width)
    {
        var clamped = SidebarSizing.Clamp(width, Viewport.Width, IsAssistantShowing ? AssistantSizing.Min : 0);
        SetSidebarWidth(clamped);
        return clamped;
    }

    /// <summary>Records the AI sidebar's width the user asked for; null goes back to the width it first opens at.</summary>
    public void SetAssistantWidth(int? width)
    {
        AssistantWidth = width is { } w ? Math.Max(AssistantSizing.Min, w) : null;
        ApplySidebarWidths();
    }

    /// <summary>The widest the AI sidebar may be drawn now, beside the left sidebar as it's drawn.</summary>
    public int MaxAssistantWidth => AssistantSizing.MaxFor(Viewport.Width, IsSidebarVisible ? DrawnSidebarWidth : 0);

    /// <summary>Sets the AI sidebar's width to <paramref name="width"/>, clamped to the terminal. Returns the new width.</summary>
    public int ResizeAssistantTo(int width)
    {
        var clamped = Math.Clamp(width, AssistantSizing.Min, MaxAssistantWidth);
        SetAssistantWidth(clamped);
        return clamped;
    }

    /// <summary>True when <paramref name="column"/> is the AI sidebar's left border, where a drag starts.</summary>
    public bool IsOnAssistantBorder(int column) => IsAssistantShowing && column == AssistantSidebar.FrameToScreen().Left;

    /// <summary>The width that puts the AI sidebar's left border under screen column <paramref name="column"/>.</summary>
    public int AssistantWidthForBorderAt(int column) => AssistantSidebar.FrameToScreen().Right - column;

    /// <summary>True when <paramref name="column"/> is the sidebar's right border — the one column a drag starts in (#252).</summary>
    public bool IsOnSidebarBorder(int column) => IsSidebarVisible && column == Sidebar.FrameToScreen().Right - 1;

    /// <summary>The width that puts the sidebar's right border under screen column <paramref name="column"/>.</summary>
    public int SidebarWidthForBorderAt(int column) => column - Sidebar.FrameToScreen().Left + 1;

    protected override void OnSubViewsLaidOut(LayoutEventArgs args)
    {
        base.OnSubViewsLaidOut(args);
        ApplySidebarWidths();
        ApplyPanelHeight();
    }

    public void SetHistoryOn(bool on)
    {
        if (IsHistoryOn == on) return;
        IsHistoryOn = on;
        HistoryOnChanged?.Invoke(this, EventArgs.Empty);
        SaveWorkspaceState();
    }

    /// <summary>Puts the History tab in the panel under the editor, or takes it out.</summary>
    public void ShowHistory(bool showing)
    {
        if (IsHistoryShowing == showing) return;
        IsHistoryShowing = showing;
        Panel.SetOpen(showing);
        ApplyPanelHeight();
        HistoryShowingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the AI assistance sidebar on the right, or hides it.</summary>
    public void ShowAssistant(bool showing)
    {
        if (IsAssistantShowing == showing) return;
        AssistantSidebar.Visible = showing;
        ApplySidebarWidths();
        SaveWorkspaceState();
        AssistantShowingChanged?.Invoke(this, EventArgs.Empty);
    }

    // The panel gives the editor back its rows when History is closed.
    private void ApplyPanelHeight()
    {
        // Less the menu bar and the status bar.
        var available = Viewport.Height - 2;
        var height = Panel.IsOpen ? PanelPart.RowsFor(available) : 0;
        Panel.Visible = height > 0;
        if (Panel.Height is DimAbsolute { Size: var size } && size == height) return;
        Panel.Height = height;
        Editor.Height = Dim.Fill(1 + height);
        SetNeedsLayout();
    }

    private void ApplySidebarWidths()
    {
        var assistant = IsAssistantShowing ? AssistantWidth ?? AssistantSizing.DefaultFor(Viewport.Width) : (int?)null;
        // Nothing to clamp against before the first layout, and the default would draw for a frame.
        var (left, right) = Viewport.Width > 0
            ? SidebarSizing.Fit(Viewport.Width, IsSidebarVisible ? SidebarWidth : null, assistant)
            : (SidebarWidth, assistant ?? 0);
        if (IsSidebarVisible && left != DrawnSidebarWidth)
        {
            DrawnSidebarWidth = left;
            Sidebar.Width = left;
            SetNeedsLayout();
        }
        if (right == DrawnAssistantWidth) return;
        DrawnAssistantWidth = right;
        AssistantSidebar.X = Pos.AnchorEnd(right);
        AssistantSidebar.Width = right;
        Editor.Width = Dim.Fill(right);
        Panel.Width = Dim.Fill(right);
        SetNeedsLayout();
    }

    public void SetSidebarVisible(bool visible)
    {
        if (IsSidebarVisible == visible) return;
        IsSidebarVisible = visible;
        Sidebar.Visible = visible;
        Editor.X = visible ? Pos.Right(Sidebar) : 0;
        Panel.X = Editor.X;
        ApplySidebarWidths();
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
