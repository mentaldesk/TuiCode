using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.History;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// The History panel under the editor (#534), against a fake git. Boots a TG Application — serialised (#77).
public class FileHistoryHostTests : StaticConfigurationTest
{
    private static readonly DateTimeOffset Day = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    // Newest first: an edit, a pure rename from old.txt, an edit before it, and the commit that created it.
    private static readonly GitFileCommit EditA = Commit('1', "Ada", "Edit a", "a.txt", "a.txt", 4);
    private static readonly GitFileCommit Rename = Commit('2', "Bo", "Rename old to a", "a.txt", "old.txt", 3, renameOnly: true);
    private static readonly GitFileCommit EditOld = Commit('3', "Bo", "Edit old", "old.txt", "old.txt", 2);
    private static readonly GitFileCommit AddOld = Commit('4', "Cy", "Add old", "old.txt", null, 1);

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public FileHistoryHostTests()
    {
        _keybindings = new KeybindingService(_commands);
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\nthree\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("b\n"));
        _fs.AddFile("/work/untracked.txt", new MockFileData("u\n"));
        _fs.AddFile("/elsewhere/loose.txt", new MockFileData("l\n"));
        _git.Root = Full("/work");
        _git.FileCommits[Full("/work/a.txt")] = [EditA, Rename, EditOld, AddOld];
        _git.FileCommits[Full("/work/b.txt")] = [Commit('5', "Di", "Add b", "b.txt", null, 1)];
        AddVersions(EditA, "one\ntwo\n", "one\ntwo\nthree\n");
        AddVersions(Rename, "one\ntwo\n", "one\ntwo\n");
        AddVersions(EditOld, "one\n", "one\ntwo\n");
        AddVersions(AddOld, null, "one\n");
    }

    [Fact]
    public async Task Ctrl_G_H_opens_the_panel_under_the_editor_with_the_keys_in_its_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var history = workbench.History;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 30),
            () => Open(workbench, "/work/a.txt"),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.H),
            () => workbench.StatusBar.DisplayedFocus == "History");

        Assert.True(workbench.IsHistoryShowing);
        Assert.True(history.ListHasFocus);
        Assert.Equal("History: a.txt", history.Title);
        Assert.Equal(["Edit a", "Rename old to a", "Edit old", "Add old"], history.Listed.Select(c => c.Subject));
        Assert.Equal("1111111  4 days ago    Ada  Edit a", history.Rows[0].TrimEnd());
        Assert.Equal("4444444  24 hours ago  Cy   Add old", history.Rows[3].TrimEnd());

        var editor = workbench.Editor.Frame;
        var panel = workbench.Panel.Frame;
        Assert.Equal(editor.X, panel.X);
        Assert.Equal(editor.Width, panel.Width);
        Assert.Equal(editor.Bottom, panel.Y);
        Assert.Equal(workbench.Sidebar.Frame.Bottom, panel.Bottom);
        Assert.True(editor.Height >= FileHistory.EditorFloor, $"The editor has {editor.Height} rows");
        Assert.True(history.Viewport.Height - 1 >= 5, $"The list has {history.Viewport.Height - 1} rows");
        Assert.Equal("Hide file history", Title());
    }

    [Fact]
    public async Task With_no_file_open_it_cannot_be_shown()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var before = true;

        await HostSteps.Run(host,
            () => { before = _commands.IsEnabled(CommandIds.ToggleFileHistory); Open(workbench, "/work/a.txt"); },
            () => workbench.StatusBar.DisplayedFocus == "Editor");

        Assert.False(before);
        Assert.True(_commands.IsEnabled(CommandIds.ToggleFileHistory));
    }

    [Fact]
    public async Task Hiding_it_gives_the_rows_back_to_the_editor_and_Esc_leaves_it_showing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var heights = new List<int>();
        var showingAfterEsc = false;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => { heights.Add(workbench.Editor.Frame.Height); _commands.TryExecute(CommandIds.ToggleFileHistory); },
            () => workbench.StatusBar.DisplayedFocus == "History",
            () => { heights.Add(workbench.Editor.Frame.Height); host.App.InjectKey(Key.Esc); },
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { showingAfterEsc = workbench.IsHistoryShowing; _commands.TryExecute(CommandIds.ToggleFileHistory); },
            () => !workbench.IsHistoryShowing,
            () => heights.Add(workbench.Editor.Frame.Height));

        Assert.True(showingAfterEsc);
        Assert.True(heights[1] < heights[0]);
        Assert.Equal(heights[0], heights[2]);
        Assert.Equal("Show file history", Title());
    }

    [Theory]
    [InlineData("/work/untracked.txt", "untracked.txt isn't tracked by git.")]
    [InlineData("/elsewhere/loose.txt", "loose.txt isn't in a git repository.")]
    public async Task Turning_it_on_for_a_file_with_no_history_says_why(string path, string reason)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Open(workbench, path),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => workbench.StatusBar.Message == reason);

        Assert.True(workbench.IsHistoryOn);
        Assert.False(workbench.IsHistoryShowing);
        Assert.Equal("Editor", workbench.StatusBar.DisplayedFocus);
    }

    [Fact]
    public async Task It_folds_away_on_tabs_with_no_history_and_comes_back_on_a_tracked_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var showing = new List<bool>();
        var hideable = false;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => workbench.IsHistoryShowing,
            () => Open(workbench, "/work/untracked.txt"),
            () => !workbench.IsHistoryShowing,
            () => { workbench.Editor.Group.Restore(_fs.FileInfo.New(Full("/work/new.txt")), ["never saved"]); return true; },
            () => { showing.Add(workbench.IsHistoryShowing); Open(workbench, "/work/b.txt"); },
            () => workbench.IsHistoryShowing && workbench.History.Listed.Count == 1,
            () => { workbench.CloseAllTabs(); return true; },
            () => { showing.Add(workbench.IsHistoryShowing); hideable = _commands.IsEnabled(CommandIds.ToggleFileHistory); Open(workbench, "/work/a.txt"); },
            () => workbench.IsHistoryShowing && workbench.History.Listed.Count == 4);

        Assert.Equal([false, false], showing);
        Assert.True(hideable);
        Assert.Equal("History: a.txt", workbench.History.Title);
    }

    [Fact]
    public async Task Typing_in_the_list_filters_by_hash_author_or_subject()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var history = workbench.History;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => history.ListHasFocus,
            () => host.App.InjectKey(Key.B),
            () => host.App.InjectKey(Key.O));

        Assert.Equal("bo", history.Filter);
        Assert.Equal([Rename, EditOld], history.Listed);
    }

    [Fact]
    public async Task Enter_opens_the_commits_change_to_the_file_with_the_keys_in_the_diff()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        DiffTab? diff = null;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => workbench.History.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => (diff = workbench.Editor.Group.ActiveDiffTab) is not null && workbench.StatusBar.DisplayedFocus == "Diff");

        Assert.Equal("a.txt 1111111^ ↔ 1111111", diff!.Title);
        Assert.Equal(["one", "two", ""], diff.LeftLines);
        Assert.Equal(["one", "two", "three", ""], diff.RightLines);
        Assert.Equal(0, diff.History!.Index);
        Assert.True(workbench.IsHistoryShowing);
    }

    [Fact]
    public async Task Stepping_walks_older_skipping_a_rename_closes_the_diff_it_leaves_and_moves_the_selection()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var history = workbench.History;
        var commitStatus = "";
        var reads = 0;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => history.ListHasFocus,
            () => { reads = _git.FileCommitReads.Count; host.App.InjectKey(Key.Enter); },
            () => workbench.Editor.Group.ActiveDiffTab is { History.Index: 0, IsFocused: true },
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => workbench.Editor.Group.ActiveDiffTab is { History.Index: 2 },
            () => { workbench.ShowCursorPosition(); commitStatus = workbench.StatusBar.DisplayedText; });

        var diffs = workbench.Editor.Group.TabCollection.OfType<DiffTab>().ToList();
        var diff = Assert.Single(diffs);
        Assert.Equal("a.txt 3333333^ ↔ 3333333", diff.Title);
        Assert.Equal(EditOld, history.SelectedCommit);
        Assert.Contains("Commit 3 of 4", commitStatus);
        Assert.True(commitStatus.IndexOf("Commit 3 of 4", StringComparison.Ordinal) < commitStatus.IndexOf("Change 1 of 1", StringComparison.Ordinal));
        Assert.Equal(reads, _git.FileCommitReads.Count);
    }

    [Fact]
    public async Task Stepping_stops_at_both_ends_and_newer_lands_on_the_last_change()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var messages = new List<string>();
        var landed = -1;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => workbench.History.ListHasFocus,
            () => host.App.InjectKey(Key.CursorUp.WithAlt),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is { History.Index: 0, IsFocused: true },
            () => _commands.TryExecute(CommandIds.PreviousChange),
            () => { messages.Add(workbench.StatusBar.Message); workbench.Editor.Group.CloseDiff(workbench.Editor.Group.ActiveDiffTab!); },
            () => { workbench.History.FocusList(); Select(workbench, AddOld); },
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is { History.Index: 3, IsFocused: true },
            () => _commands.TryExecute(CommandIds.NextChange),
            () => _commands.TryExecute(CommandIds.NextChange),
            () => messages.Add(workbench.StatusBar.Message),
            () => _commands.TryExecute(CommandIds.PreviousChange),
            () => workbench.Editor.Group.ActiveDiffTab is { History.Index: 2 } diff && (landed = diff.CurrentChange) > 0);

        Assert.Equal(["Newest commit in the history", "Oldest commit in the history"], messages);
        Assert.Equal(workbench.Editor.Group.ActiveDiffTab!.ChangeCount, landed);
    }

    [Fact]
    public async Task Stepping_works_with_the_panel_hidden()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => workbench.History.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is { History.Index: 0, IsFocused: true },
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => !workbench.IsHistoryShowing,
            () => _commands.TryExecute(CommandIds.NextChange),
            () => _commands.TryExecute(CommandIds.NextChange),
            () => workbench.Editor.Group.ActiveDiffTab is { History.Index: 2 });
    }

    [Fact]
    public async Task Next_change_in_a_diff_opened_any_other_way_stops_at_its_last_change()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => workbench.IsHistoryShowing,
            () => { workbench.Editor.Group.Compare(workbench.Editor.Group.ActiveTab!, "HEAD", () => ["zero", ""]); return true; },
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => _commands.TryExecute(CommandIds.NextChange),
            () => _commands.TryExecute(CommandIds.NextChange),
            () => _commands.TryExecute(CommandIds.NextChange));

        var diff = Assert.Single(workbench.Editor.Group.TabCollection.OfType<DiffTab>());
        Assert.Null(diff.History);
        Assert.Equal("a.txt ↔ HEAD", diff.Title);
    }

    [Fact]
    public async Task The_newest_rows_show_before_the_rest_and_a_long_history_is_cut_at_200()
    {
        var commits = Enumerable.Range(0, FileHistory.Limit + 1)
            .Select(i => new GitFileCommit($"{i:x40}", "Ada", Day.AddHours(-i), $"Commit {i}", "a.txt", "a.txt"))
            .ToList();
        _git.FileCommits[Full("/work/a.txt")] = commits;
        var rest = new TaskCompletionSource();
        _git.FullHistoryGate = rest.Task;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var history = workbench.History;
        var first = 0;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt"),
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => history.ListHasFocus,
            () => { first = history.Commits.Count; rest.SetResult(); },
            () => history.Commits.Count == FileHistory.Limit);

        Assert.Equal(FileHistory.FirstPage, first);
        Assert.Equal(FileHistory.CappedNote, history.Note);
        Assert.Equal("Commit 199", history.Commits[^1].Subject);
    }

    [Fact]
    public async Task Whether_it_was_on_is_kept_with_the_workspace()
    {
        var store = new WorkspaceStateStore(_fs, "/state.json");
        store.Save(Full("/work"), new WorkspaceState([Full("/work/a.txt")], Full("/work/a.txt")) { History = true });
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), store);
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/work"))),
            () => workbench.IsHistoryShowing,
            () => _commands.TryExecute(CommandIds.ToggleFileHistory),
            () => !workbench.IsHistoryShowing);

        Assert.False(store.Load(Full("/work"))!.History);
    }

    [Fact]
    public void Gh_is_Show_file_history_on_Ctrl_G_H()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        Assert.Equal("gh", CommandMnemonics.For(CommandIds.ToggleFileHistory));
        Assert.Equal(CommandScope.Global, _commands.ScopeOf(CommandIds.ToggleFileHistory));
        var binding = Assert.Single(_keybindings.Bindings, b => b.CommandId == CommandIds.ToggleFileHistory);
        Assert.Equal("Ctrl+G h", binding.Display);
    }

    private static GitFileCommit Commit(char digit, string author, string subject, string path, string? parent, int daysOld, bool renameOnly = false) =>
        new(new string(digit, 40), author, Day.AddDays(-daysOld), subject, path, parent, renameOnly);

    private void AddVersions(GitFileCommit commit, string? before, string after)
    {
        _git.RepoFiles[$"{commit.Hash}:{commit.Path}"] = after;
        if (before is not null) _git.RepoFiles[$"{commit.Hash}^:{commit.ParentPath}"] = before;
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private string Title() => _commands.Registered.Single(c => c.Id == CommandIds.ToggleFileHistory).Title;

    private void Open(Workbench.Workbench workbench, string path) => workbench.OpenFile(_fs.FileInfo.New(Full(path)));

    private static void Select(Workbench.Workbench workbench, GitFileCommit commit) => workbench.History.Select(commit);

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New(Full("/work")));
        workbench.History.Now = () => Day;
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        workbench.History.Now = () => Day;
        return new WorkbenchHost(workbench, _commands, _keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git);
    }
}
