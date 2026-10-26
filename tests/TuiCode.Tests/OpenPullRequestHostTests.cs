using MentalDesk.Tui.Loading;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `opr` through the host against a fake git and gh. Boots a TG Application — serialised (#77).
public class OpenPullRequestHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public OpenPullRequestHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/pr-132/b.txt", new MockFileData("bravo\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "b.txt")];
        _gitHub.OpenPullRequests =
        [
            new GitHubPullRequestSummary(132, "Command scopes should be fixed", "jamescrosswell", "fix-scopes", ReviewRequested: true),
            new GitHubPullRequestSummary(129, "Delete, rename and move files", "octocat", "delete-rename"),
        ];
    }

    [Fact]
    public async Task Opr_lists_the_open_pull_requests()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        IReadOnlyList<string> rows = [];

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.O),
            () => host.App.InjectKey(Key.P),
            () => host.App.InjectKey(Key.R),
            () => Picker(workbench) is { State: LoadState.Loaded },
            () => { rows = Picker(workbench)!.VisibleItems; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(["#132", "#129"], rows);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task Typing_filters_the_list_and_Enter_checks_the_selected_pull_request_out_in_its_own_worktree()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var worktree = _fs.Path.GetFullPath("/pr-129");
        _fs.AddFile("/pr-129/b.txt", new MockFileData("bravo\n"));

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench) is { State: LoadState.Loaded },
            () => Type(host, "octo"),
            () => Picker(workbench)!.VisibleItems is ["#129"],
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal([worktree], _git.Worktrees);
        Assert.Equal([(worktree, 129)], _gitHub.Checkouts);
        Assert.Equal(worktree, workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal(SidebarTab.Review, workbench.Sidebar.ActiveTab);
    }

    [Fact]
    public async Task A_pull_request_whose_branch_is_already_checked_out_opens_that_worktree_instead()
    {
        var existing = _fs.Path.GetFullPath("/opr");
        _fs.AddFile("/opr/b.txt", new MockFileData("bravo\n"));
        _git.BranchWorktrees["fix-scopes"] = existing;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench) is { State: LoadState.Loaded },
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Empty(_git.Worktrees);
        Assert.Empty(_gitHub.Checkouts);
        Assert.Equal(existing, workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal(SidebarTab.Review, workbench.Sidebar.ActiveTab);
    }

    [Fact]
    public async Task A_checkout_that_fails_is_shown_in_the_picker_which_stays_open()
    {
        _gitHub.CheckoutError = "pr-132 already has local changes";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench) is { State: LoadState.Loaded },
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench)!.Alert.Message == "pr-132 already has local changes",
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(_fs.Path.GetFullPath("/work"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Fact]
    public async Task The_picker_opens_while_gh_lists_the_pull_requests_and_keeps_what_was_typed()
    {
        var gate = new TaskCompletionSource();
        _gitHub.HoldPullRequests = gate;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var stateWhileLoading = LoadState.Loaded;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench) is not null,
            () =>
            {
                stateWhileLoading = Picker(workbench)!.State;
                Type(host, "octo");
            },
            () => Picker(workbench)!.FilterField.Text == "octo",
            () => gate.SetResult(),
            () => Picker(workbench) is { State: LoadState.Loaded },
            () => Assert.Equal(["#129"], Picker(workbench)!.VisibleItems),
            () => host.App.InjectKey(Key.Esc),
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(LoadState.Loading, stateWhileLoading);
        Assert.Null(Picker(workbench));
        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Without_gh_the_picker_says_so_and_R_tries_again()
    {
        _gitHub.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var failed = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench)?.State == LoadState.Failed,
            () =>
            {
                failed = Picker(workbench)!.Says();
                _gitHub.Missing = false;
                host.App.InjectKey(Key.R);
            },
            () => Picker(workbench) is { State: LoadState.Loaded },
            () => Assert.Equal(["#132", "#129"], Picker(workbench)!.VisibleItems),
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Pull requests need the GitHub CLI: run gh auth login", failed);
        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task R_types_into_the_filter_once_the_list_is_in()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench) is { State: LoadState.Loaded },
            () => host.App.InjectKey(Key.R),
            () => Picker(workbench)!.FilterField.Text == "r",
            () => host.App.InjectKey(Key.Esc),
            () => host.App.InjectKey(Key.Esc));

        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_repo_with_no_open_pull_requests_says_so_in_the_picker()
    {
        _gitHub.OpenPullRequests = [];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var says = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench)?.State == LoadState.Empty,
            () => { says = Picker(workbench)!.Says(); },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("No open pull requests", says);
        Assert.Equal(workbench.StatusBar.DefaultMessage, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task Outside_a_git_repository_the_picker_says_so_and_offers_no_retry()
    {
        _git.Root = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var says = "";
        var state = LoadState.Loading;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench)?.State is LoadState.Empty,
            () =>
            {
                says = Picker(workbench)!.Says();
                host.App.InjectKey(Key.R);
            },
            () =>
            {
                state = Picker(workbench)!.State;
                host.App.InjectKey(Key.Esc);
                host.App.InjectKey(Key.Esc);
            });

        Assert.Equal($"{_fs.Path.GetFullPath("/work")} isn't in a git repository", says);
        Assert.Equal(LoadState.Empty, state);
    }

    private static PullRequestPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<PullRequestPickerView>().SingleOrDefault();

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart());
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
