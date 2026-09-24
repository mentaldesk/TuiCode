using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// `rc` in the diffs you review in: ctr, cto and the Review tab's branch and PR diffs (#246).
// Boots a TG Application — serialised (#77).
public class RevertSourcesHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public RevertSourcesHostTests()
    {
        _fs.AddDirectory("/work");
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.DefaultBranch = "origin/main";
    }

    [Fact]
    public async Task Rc_in_a_revision_diff_takes_back_the_lines_the_branch_added()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\nSTRAY\nbravo"));
        _git.Refs = [new GitRef("v1.2.0", GitRefKind.Tag)];
        _git.Resolvable.Add("v1.2.0");
        _git.Files["v1.2.0"] = "alpha\nbravo";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => RevisionPicker(workbench) is { Loaded: true },
            () => Type(host, "v1.2"),
            () => RevisionPicker(workbench)!.VisibleItems.Count == 1,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is { IsFocused: true },
            () => host.App.InjectKey(Key.R.WithCtrl));

        Assert.StartsWith("Reverted 1 line from v1.2.0", workbench.StatusBar.DisplayedText);
        Assert.Equal(["alpha", "bravo"], group.Tabs[0].Lines);
        Assert.True(group.Tabs[0].IsDirty);
        Assert.Equal("alpha\nSTRAY\nbravo", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Rc_in_an_other_file_diff_puts_back_the_lines_that_file_still_has()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha\ndelta"));
        _fs.AddFile("/work/src/b.txt", new MockFileData("alpha\nbravo\ncharlie\ndelta"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/src/a.txt")),
            () => commands.TryExecute(CommandIds.CompareToOtherFile),
            () => OpenDialog(workbench) is not null,
            () => Type(host, "b.t"),
            () => OpenDialog(workbench)!.VisibleItems.Count == 1,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is { IsFocused: true },
            () => host.App.InjectKey(Key.R.WithCtrl));

        Assert.StartsWith("Reverted 2 lines from b.txt", workbench.StatusBar.DisplayedText);
        Assert.Equal(["alpha", "bravo", "charlie", "delta"], group.Tabs[0].Lines);
        Assert.Equal("alpha\ndelta", _fs.File.ReadAllText("/work/src/a.txt"));
        Assert.Equal("alpha\nbravo\ncharlie\ndelta", _fs.File.ReadAllText("/work/src/b.txt"));
    }

    [Fact]
    public async Task Rc_in_a_review_branch_diff_replaces_the_modified_lines_with_the_base_s()
    {
        Review("a.txt", branch: "a1\nCHANGED\na3", @base: "a1\na2\na3");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            [.. OpenReviewDiff(host, commands, workbench), () => host.App.InjectKey(Key.R.WithCtrl)]);

        Assert.StartsWith("Reverted 1 line from origin/main", workbench.StatusBar.DisplayedText);
        Assert.Equal(["a1", "a2", "a3"], group.Tabs[0].Lines);
        Assert.True(group.Tabs[0].IsDirty);
        Assert.Equal("a1\nCHANGED\na3", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Rc_in_a_pull_request_diff_reverts_the_branch_checked_out_in_this_worktree()
    {
        Review("b.txt", branch: "b1\nCHANGED\nb3", @base: "b1\nb2\nb3");
        _gitHub.OpenPullRequests = [new GitHubPullRequestSummary(132, "Command scopes", "octocat", "fix-scopes")];
        _gitHub.PullRequest = new GitHubPullRequest(132, "Command scopes", "main", "fix-scopes", default);
        _git.BranchWorktrees["fix-scopes"] = _fs.Path.GetFullPath("/work");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            () => commands.TryExecute(CommandIds.OpenPullRequest),
            () => PullRequestPicker(workbench) is not null,
            () => host.App.InjectKey(Key.Enter),
            () => PullRequestPicker(workbench) is null,
            .. OpenReviewDiff(host, commands, workbench),
            () => host.App.InjectKey(Key.R.WithCtrl),
        ]);

        Assert.Equal(132, workbench.Sidebar.Review.Review?.PullRequest?.Number);
        Assert.StartsWith("Reverted 1 line from origin/main", workbench.StatusBar.DisplayedText);
        Assert.Equal(["b1", "b2", "b3"], group.Tabs[0].Lines);
        Assert.Equal("b1\nCHANGED\nb3", _fs.File.ReadAllText("/work/b.txt"));
    }

    [Fact]
    public async Task After_a_revert_next_change_still_steps_on_and_then_into_the_next_file()
    {
        Review("a.txt", branch: "a1\nCHANGED\na3\nCHANGED\na5", @base: "a1\na2\na3\na4\na5");
        Review("c.txt", branch: "c1\nCHANGED\nc3", @base: "c1\nc2\nc3");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenReviewDiff(host, commands, workbench),
            () => host.App.InjectKey(Key.R.WithCtrl),
            () => commands.TryExecute(CommandIds.NextChange),
            () => group.ActiveDiffTab?.ChangeStatus == "Change 1 of 1",
            () => commands.TryExecute(CommandIds.NextChange),
            () => group.ActiveDiffTab?.File.Name == "c.txt",
        ]);

        Assert.Equal(["a1", "a2", "a3", "CHANGED", "a5"], group.Tabs.Single(t => t.File.Name == "a.txt").Lines);
        Assert.Equal("Change 1 of 1", group.ActiveDiffTab!.ChangeStatus);
    }

    [Fact]
    public async Task Rc_in_a_deleted_file_s_diff_says_there_is_no_buffer_to_revert_into()
    {
        _git.RepoFiles[$"{_git.MergeBase}:gone.txt"] = "g1\ng2";
        _git.Changes = [new GitChange(GitChangeKind.Deleted, "gone.txt")];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            [.. OpenReviewDiff(host, commands, workbench), () => host.App.InjectKey(Key.R.WithCtrl)]);

        Assert.True(workbench.Editor.Group.ActiveDiffTab!.IsDeleted);
        Assert.StartsWith("gone.txt is deleted in this branch", workbench.StatusBar.DisplayedText);
        Assert.Empty(workbench.Editor.Group.Tabs);
    }

    // Adds a file the branch changes: its working-tree content on disk, its base content in git.
    private void Review(string path, string branch, string @base)
    {
        _fs.AddFile($"/work/{path}", new MockFileData(branch));
        _git.RepoFiles[$"{_git.MergeBase}:{path}"] = @base;
        _git.Changes = [.. _git.Changes, new GitChange(GitChangeKind.Modified, path)];
    }

    // Focuses the Review tab and opens the first file's diff.
    private static Delegate[] OpenReviewDiff(WorkbenchHost host, CommandService commands, Workbench.Workbench workbench) =>
    [
        () => commands.TryExecute(CommandIds.FocusReview),
        () => workbench.Sidebar.Review.ListHasFocus,
        () => host.App.InjectKey(Key.Enter),
        () => workbench.Editor.Group.ActiveDiffTab is { IsFocused: true },
    ];

    private static RevisionPickerView? RevisionPicker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<RevisionPickerView>().SingleOrDefault();

    private static PullRequestPickerView? PullRequestPicker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<PullRequestPickerView>().SingleOrDefault();

    private static OpenView? OpenDialog(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<OpenView>().SingleOrDefault();

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
