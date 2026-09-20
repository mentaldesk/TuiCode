using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
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
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/pr-132/b.txt", new MockFileData("bravo\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "b.txt")];
        _gitHub.OpenPullRequests =
        [
            new GitHubPullRequestSummary(132, "Command scopes should be fixed", "jamescrosswell", ReviewRequested: true),
            new GitHubPullRequestSummary(129, "Delete, rename and move files", "octocat"),
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
            () => Picker(workbench) is not null,
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
            () => Picker(workbench) is not null,
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
    public async Task A_checkout_that_fails_is_shown_in_the_picker_which_stays_open()
    {
        _gitHub.CheckoutError = "pr-132 already has local changes";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => Picker(workbench) is not null,
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench)!.Status == "pr-132 already has local changes",
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(_fs.Path.GetFullPath("/work"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Fact]
    public async Task Without_gh_opr_says_so_in_the_status_bar_and_opens_nothing()
    {
        _gitHub.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => workbench.StatusBar.DisplayedText == "Pull requests need the GitHub CLI: run gh auth login");

        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_repo_with_no_open_pull_requests_says_so_rather_than_opening_an_empty_picker()
    {
        _gitHub.OpenPullRequests = [];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => workbench.StatusBar.DisplayedText == "No open pull requests.");

        Assert.Null(Picker(workbench));
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
