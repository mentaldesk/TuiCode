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
    public async Task Enter_on_a_deleted_file_opens_its_base_version_with_nothing_on_the_right()
    {
        _git.RepoFiles["b45e:src/gone.txt"] = "gone1\ngone2\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is { IsFocused: true });

        var diff = Assert.Single(group.DiffTabs);
        Assert.True(diff.IsDeleted);
        Assert.Equal("gone.txt \u2194 main (deleted)", diff.Title);
        Assert.All(diff.Diff.Rows, r => Assert.Equal(DiffRowKind.LeftOnly, r.Kind));
        Assert.Empty(group.Tabs);
    }

    [Fact]
    public async Task Go_to_line_in_a_deleted_file_s_diff_says_so_and_opens_nothing()
    {
        _git.RepoFiles["b45e:src/gone.txt"] = "gone1\ngone2\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is { IsFocused: true },
            () => host.App.InjectKey(Key.Enter),
            () => workbench.StatusBar.DisplayedText.StartsWith("Deleted in this branch", StringComparison.Ordinal));

        Assert.Empty(group.Tabs);
        Assert.Same(group.DiffTabs[0], group.ActiveDiffTab);
    }

    [Fact]
    public async Task Enter_on_a_deleted_file_again_focuses_the_diff_it_already_opened()
    {
        _git.RepoFiles["b45e:src/gone.txt"] = "gone1\ngone2\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is { IsFocused: true },
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is { IsFocused: true });

        Assert.Single(group.DiffTabs);
        Assert.Equal(1, _git.ShowCount);
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

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git));
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
