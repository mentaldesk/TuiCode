using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Steps a review file by file from the diff tab (#181). Boots a TG Application — serialised (#77).
public class ReviewStepHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();

    public ReviewStepHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a1\nCHANGED\na3\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("b1\nb2\n"));
        _fs.AddFile("/work/c.txt", new MockFileData("c1\nCHANGED\nc3\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes =
        [
            new GitChange(GitChangeKind.Modified, "a.txt"),
            new GitChange(GitChangeKind.Modified, "b.txt"),
            new GitChange(GitChangeKind.Modified, "c.txt"),
            new GitChange(GitChangeKind.Deleted, "d.txt"),
        ];
        _git.RepoFiles["b45e:a.txt"] = "a1\na2\na3\n";
        _git.RepoFiles["b45e:b.txt"] = "b1\nb2\n";
        _git.RepoFiles["b45e:c.txt"] = "c1\nc2\nc3\n";
    }

    [Fact]
    public async Task Next_change_past_the_last_opens_the_next_file_with_changes_and_closes_the_diff_it_leaves()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 0),
            () => group.ActiveDiffTab?.Source.File.Name == "a.txt",
            () => commands.TryExecute(CommandIds.NextChange),
            () => commands.TryExecute(CommandIds.NextChange),
            () => group.ActiveDiffTab?.Source.File.Name == "c.txt",
        ]);

        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("Change 1 of 1", diff.ChangeStatus);
        Assert.Equal(1, diff.CurrentRow);
        // b.txt was stepped over because its buffer matches the base, but like every file visited it stays open.
        Assert.Equal(["a.txt", "b.txt", "c.txt"], group.Tabs.Select(t => t.File.Name).Order());
    }

    [Fact]
    public async Task Previous_change_before_the_first_opens_the_previous_file_at_its_last_change()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 2),
            () => group.ActiveDiffTab?.Source.File.Name == "c.txt",
            () => commands.TryExecute(CommandIds.PreviousChange),
            () => group.ActiveDiffTab?.Source.File.Name == "a.txt",
        ]);

        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("Change 1 of 1", diff.ChangeStatus);
        Assert.Equal(1, diff.CurrentRow);
    }

    [Fact]
    public async Task The_last_change_of_the_last_file_says_so_and_stays_put()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 2),
            () => group.ActiveDiffTab?.Source.File.Name == "c.txt",
            () => commands.TryExecute(CommandIds.NextChange),
            () => commands.TryExecute(CommandIds.NextChange),
            () => workbench.StatusBar.DisplayedText.StartsWith("Last change in the review", StringComparison.Ordinal),
        ]);

        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("c.txt", diff.Source.File.Name);
        Assert.Equal("Change 1 of 1", diff.ChangeStatus);
    }

    [Fact]
    public async Task The_first_change_of_the_first_file_says_so_and_stays_put()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 0),
            () => group.ActiveDiffTab?.Source.File.Name == "a.txt",
            () => commands.TryExecute(CommandIds.PreviousChange),
            () => workbench.StatusBar.DisplayedText.StartsWith("First change in the review", StringComparison.Ordinal),
        ]);

        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("a.txt", diff.Source.File.Name);
    }

    [Fact]
    public async Task The_status_bar_and_the_Review_tab_follow_the_file_being_shown()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 0),
            () => group.ActiveDiffTab?.Source.File.Name == "a.txt",
            () => commands.TryExecute(CommandIds.NextChange),
            () => commands.TryExecute(CommandIds.NextChange),
            () => group.ActiveDiffTab?.Source.File.Name == "c.txt",
            () => workbench.StatusBar.DisplayedText.Contains("File 3 of 4  •  Change 1 of 1", StringComparison.Ordinal),
        ]);

        var selected = Assert.IsType<ReviewFileNode>(workbench.Sidebar.Review.Files.SelectedObject);
        Assert.Equal("c.txt", selected.Change.Path);
    }

    [Fact]
    public async Task A_compare_to_saved_diff_still_stops_at_its_last_change()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { group.ActiveTab!.Content = "a1\nEDITED\na3\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => group.ActiveDiffTab is { IsFocused: true },
            () => commands.TryExecute(CommandIds.NextChange),
            () => commands.TryExecute(CommandIds.NextChange));

        var diff = Assert.Single(group.DiffTabs);
        Assert.Null(diff.Review);
        Assert.Equal("a.txt ↔ saved", diff.Title);
        Assert.Equal("Change 1 of 1", diff.ChangeStatus);
        Assert.Equal([diff.Source], group.Tabs);
    }

    // Focuses the Review tab, walks the list down to a file and opens its diff.
    private static Delegate[] OpenDiff(WorkbenchHost host, Workbench.Workbench workbench, CommandService commands, int rowsDown) =>
    [
        () => commands.TryExecute(CommandIds.FocusReview),
        () => workbench.Sidebar.Review.ListHasFocus,
        .. Enumerable.Repeat<Delegate>(() => host.App.InjectKey(Key.CursorDown), rowsDown),
        () => host.App.InjectKey(Key.Enter),
    ];

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
