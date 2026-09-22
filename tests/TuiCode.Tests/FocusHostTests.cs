using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Find;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Focus goes where the readout says and comes back when asked (#228), driven through real keys.
// Boots a TG Application — serialised (#77).
public class FocusHostTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    public FocusHostTests()
    {
        _fs.AddDirectory("/work");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("three\n"));
        _fs.AddFile("/work/Widget.cs", new MockFileData("public class Widget\n{\n    public int Count { get; set; }\n}\n"));
    }

    [Theory]
    [InlineData(CommandIds.FocusSidebar, "Explorer")]
    [InlineData(CommandIds.FindGlobally, "Find")]
    [InlineData(CommandIds.FocusReview, "Review")]
    [InlineData(CommandIds.FocusEditorTabStrip, "Tabs")]
    public async Task Esc_from_anywhere_else_puts_the_keys_back_in_the_editor(string command, string region)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var before = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => commands.TryExecute(command),
            () => workbench.StatusBar.DisplayedFocus == region,
            () => { before = workbench.StatusBar.DisplayedFocus; host.App.InjectKey(Key.Esc); },
            () => workbench.StatusBar.DisplayedFocus == "Editor");

        Assert.Equal(region, before);
    }

    [Fact]
    public async Task Esc_goes_back_to_the_diff_when_the_active_tab_is_one()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.Esc),
            () => workbench.StatusBar.DisplayedFocus == "Diff");
    }

    // #234: the explorer still reports HasFocus after the file opens, and used to keep every key.
    [Theory]
    [InlineData(CommandIds.FocusSidebar)]
    [InlineData(CommandIds.FindGlobally)]
    [InlineData(CommandIds.FocusReview)]
    public async Task Opening_a_file_from_the_Open_dialog_leaves_you_typing_in_it(string from)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        OpenView? dialog = null;

        await HostSteps.Run(host,
            () => commands.TryExecute(from),
            () => host.App.InjectKey(Key.O.WithCtrl),
            () => (dialog = workbench.SubViews.OfType<OpenView>().SingleOrDefault()) is not null,
            () => { foreach (var c in "b.t") host.App.InjectKey(new Key(c)); },
            () => dialog!.VisibleItems.SequenceEqual(["b.txt"]),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab is not null,
            () => host.App.InjectKey(new Key('X')),
            () => workbench.Editor.Group.ActiveTab!.Content.StartsWith('X'));

        Assert.Equal("Editor", workbench.StatusBar.DisplayedFocus);
        Assert.Equal("b.txt", workbench.Editor.Group.ActiveTab?.File.Name);
    }

    [Fact]
    public async Task Opening_a_file_from_a_diff_leaves_you_typing_in_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        OpenView? dialog = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => host.App.InjectKey(Key.O.WithCtrl),
            () => (dialog = workbench.SubViews.OfType<OpenView>().SingleOrDefault()) is not null,
            () => { foreach (var c in "b.t") host.App.InjectKey(new Key(c)); },
            () => dialog!.VisibleItems.SequenceEqual(["b.txt"]),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab?.File.Name == "b.txt",
            () => host.App.InjectKey(new Key('X')),
            () => workbench.Editor.Group.ActiveTab!.Content.StartsWith('X'));

        Assert.Equal("Editor", workbench.StatusBar.DisplayedFocus);
    }

    // Every modal the workbench opens, cancelled from the explorer: the keys go back to the explorer.
    [Theory]
    [InlineData(CommandIds.ShowActions)]
    [InlineData(CommandIds.ShowMnemonics)]
    [InlineData(CommandIds.Open)]
    [InlineData(CommandIds.GoToLine)]
    [InlineData(CommandIds.ShowHelp)]
    [InlineData(CommandIds.ChangeGrammar)]
    [InlineData(CommandIds.GoToSymbol)]
    [InlineData(CommandIds.CompareToRevision)]
    [InlineData(CommandIds.OpenPullRequest)]
    public async Task A_modal_hands_the_keys_back_to_the_region_it_was_opened_from(string command)
    {
        _git.Refs = [new GitRef("main", GitRefKind.Branch)];
        _gitHub.OpenPullRequests = [new GitHubPullRequestSummary(1, "A change", "someone")];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/Widget.cs")),
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => commands.TryExecute(command),
            () => Modals(workbench) == 1,
            () => host.App.InjectKey(Key.Esc),
            () => Modals(workbench) == 0,
            () => workbench.StatusBar.DisplayedFocus == "Explorer");
    }

    // #197's second report: after stepping through a review, Up/Down scrolled the file list and nothing got them back.
    [Fact]
    public async Task The_reported_review_sequence_leaves_the_arrow_keys_in_the_diff()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _git.RepoFiles["main:a.txt"] = "one\nTWO\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var line = 0;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            // A save refreshes the review pane, which is what used to snatch the keys back.
            () => commands.TryExecute(CommandIds.SaveActiveEditor),
            () => { line = workbench.Editor.Group.ActiveDiffTab!.CurrentBufferLine; host.App.InjectKey(Key.CursorDown); },
            () => workbench.Editor.Group.ActiveDiffTab!.CurrentBufferLine > line);

        Assert.Equal("Diff", workbench.StatusBar.DisplayedFocus);
    }

    // #197's first report: Ctrl+F after crossing panes typed into the buffer instead of the search box.
    [Fact]
    public async Task The_reported_find_sequence_types_into_the_search_box()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        FindBarView? bar = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => commands.TryExecute(CommandIds.FindGlobally),
            () => workbench.StatusBar.DisplayedFocus == "Find",
            () => host.App.InjectKey(Key.CursorLeft),
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => host.App.InjectKey(Key.Esc),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.F.WithCtrl),
            () => (bar = workbench.SubViewsDeep().OfType<FindBarView>().SingleOrDefault()) is not null,
            () => { foreach (var c in "two") host.App.InjectKey(new Key(c)); },
            () => bar!.Query == "two");

        Assert.Equal("one\ntwo\n", workbench.Editor.Group.ActiveTab!.Content);
    }

    // #196's background lookup returns seconds later, by which time the keys have moved on.
    [Fact]
    public async Task A_pull_request_header_that_arrives_late_leaves_the_keys_where_they_are()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _git.RepoFiles["main:a.txt"] = "one\nTWO\n";
        _gitHub.PullRequest = new GitHubPullRequest(1, "A change", "main", "feature", default);
        _gitHub.HoldPullRequest = new TaskCompletionSource();
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => _gitHub.HoldPullRequest!.SetResult(),
            () => workbench.Sidebar.Review.Review?.PullRequest is not null,
            () => workbench.StatusBar.DisplayedFocus == "Diff");
    }

    // Terminal.Gui leaves HasFocus set on a view the keyboard has left, and its SetFocus is then a no-op:
    // the diff kept the flag, so nothing could hand the keys back to it for the rest of the session (#197).
    [Fact]
    public async Task A_stale_HasFocus_neither_keeps_the_keys_nor_locks_the_region_out()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var stale = false;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => { stale = workbench.Editor.Group.ActiveDiffTab!.HasFocus; host.App.InjectKey(Key.Esc); },
            () => workbench.StatusBar.DisplayedFocus == "Diff");

        Assert.True(stale, "the diff was expected to still report focus it no longer has");
    }

    [Fact]
    public async Task A_focus_move_with_nowhere_to_land_says_so_in_the_status_bar()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.Esc),
            () => workbench.StatusBar.DisplayedText.Contains("Nothing to focus in Editor"));

        Assert.Equal("Explorer", workbench.StatusBar.DisplayedFocus);
    }

    // The workbench's own three parts are the sidebar, the editor and the status bar; anything else is a modal.
    private static int Modals(Workbench.Workbench workbench) => workbench.SubViews.Count - 3;

    [Fact]
    public async Task Saving_leaves_the_keys_in_the_diff()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var afterSave = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => commands.TryExecute(CommandIds.SaveActiveEditor),
            () => { afterSave = workbench.StatusBar.DisplayedFocus; });

        Assert.Equal("Diff", afterSave);
    }

    [Fact]
    public async Task Showing_the_review_pane_in_the_background_leaves_the_keys_in_the_diff()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var afterShow = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => commands.TryExecute(CommandIds.ToggleSidebar),
            () => { workbench.Sidebar.ShowTab(SidebarTab.Review); workbench.SetSidebarVisible(true); },
            () => { afterShow = workbench.StatusBar.DisplayedFocus; });

        Assert.Equal("Diff", afterShow);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(Syntax), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
