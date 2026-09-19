using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives the Review tab through the host against a fake git. Boots a TG Application — serialised (#77).
public class ReviewHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public ReviewHostTests()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha\nbravo\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("new\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes =
        [
            new GitChange(GitChangeKind.Modified, "src/a.txt"),
            new GitChange(GitChangeKind.Deleted, "src/gone.txt"),
            new GitChange(GitChangeKind.Added, "b.txt"),
        ];
        _git.RepoFiles["b45e:src/a.txt"] = "alpha\n";
    }

    [Fact]
    public async Task Fr_shows_the_Review_tab_and_focuses_its_file_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        workbench.SetSidebarVisible(false);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.F),
            () => host.App.InjectKey(Key.R),
            () => workbench.Sidebar.Review.ListHasFocus);

        Assert.True(workbench.IsSidebarVisible);
        Assert.Equal(SidebarTab.Review, workbench.Sidebar.ActiveTab);
        Assert.Equal("feature ← main  (no PR)", workbench.Sidebar.Review.HeaderText);
    }

    [Fact]
    public async Task Ctrl_Shift_R_shows_the_Review_tab_and_focuses_its_file_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        workbench.SetSidebarVisible(false);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.R.WithCtrl.WithShift),
            () => workbench.Sidebar.Review.ListHasFocus);

        Assert.True(workbench.IsSidebarVisible);
        Assert.Equal(SidebarTab.Review, workbench.Sidebar.ActiveTab);
    }

    [Fact]
    public async Task Enter_on_a_modified_file_opens_it_and_a_diff_against_the_base()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is { IsFocused: true });

        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("a.txt ↔ main", diff.Title);
        Assert.Equal(_fs.Path.GetFullPath("/work/src/a.txt"), diff.Source.File.FullName);
        Assert.Equal([DiffRowKind.Both, DiffRowKind.RightOnly, DiffRowKind.Both], diff.Diff.Rows.Select(r => r.Kind));
        Assert.Equal([diff.Source.File.FullName], group.Tabs.Select(t => t.File.FullName));
    }

    [Fact]
    public async Task An_added_file_has_an_empty_left_side()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.End),
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is not null);

        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("b.txt ↔ main", diff.Title);
        Assert.All(diff.Diff.Rows, r => Assert.Equal(DiffRowKind.RightOnly, r.Kind));
        Assert.Equal(0, _git.ShowCount);
    }

    [Fact]
    public async Task Enter_on_a_deleted_file_says_so_and_opens_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.StatusBar.DisplayedText == "Deleted in this branch");

        Assert.Empty(workbench.Editor.Group.Tabs);
        Assert.Empty(workbench.Editor.Group.DiffTabs);
    }

    [Fact]
    public async Task Saving_a_file_refreshes_the_list()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.Review is not null,
            () =>
            {
                _git.Changes = [];
                workbench.OpenFile(_fs.FileInfo.New("/work/b.txt"));
                workbench.Editor.Save();
            },
            () => review.HeaderText == "No changes against main");
    }

    [Fact]
    public async Task The_PR_header_fills_in_after_the_file_list()
    {
        _gitHub.PullRequest = new GitHubPullRequest(183, "Shows the PR", "main", "feature", new GitHubChecks(3, 0, 1));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.TitleText.Length > 0);

        Assert.Equal("#183 Shows the PR", review.TitleText);
        Assert.Equal("main ← feature", review.HeaderText);
        Assert.Equal("✓ 3  ● 1 checks", review.ChecksText);
        Assert.True(review.Files.Visible);
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
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git);
    }
}
