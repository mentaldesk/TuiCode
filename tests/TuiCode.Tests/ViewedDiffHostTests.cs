using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Toggle viewed from a PR's review diff, moving on to the next unviewed file (#398). Boots a TG Application — serialised (#77).
public class ViewedDiffHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public ViewedDiffHostTests()
    {
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            _fs.AddFile($"/work/{name}.txt", new MockFileData($"{name}1\nCHANGED\n"));
            _git.RepoFiles[$"b45e:{name}.txt"] = $"{name}1\n{name}2\n";
        }
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [.. new[] { "a", "b", "c", "d" }.Select(name => new GitChange(GitChangeKind.Modified, $"{name}.txt"))];
        _gitHub.PullRequest = new GitHubPullRequest(398, "Viewed", "main", "feature", default, Id: "PR_kw398");
        _gitHub.ViewedFiles = new() { ["c.txt"] = GitHubViewedState.Viewed };
    }

    [Fact]
    public async Task Tv_marks_the_file_viewed_and_opens_the_next_unviewed_one_closing_the_diff_it_leaves()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 1),
            () => group.ActiveDiffTab?.File.Name == "b.txt" && group.ActiveDiffTab.IsFocused,
            () => Assert.True(commands.TryExecute(CommandIds.ToggleViewed)),
            () => group.ActiveDiffTab?.File.Name == "d.txt",
        ]);

        Assert.Equal(("PR_kw398", "b.txt", true), _gitHub.ViewedChanges.Single());
        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("Change 1 of 1", diff.ChangeStatus);
        Assert.Equal("d.txt", review.SelectedFile?.Path);
        Assert.Equal("Viewed 2 of 4", review.ViewedText);
        Assert.Equal(["a.txt", "b.txt  ✓", "c.txt  ✓", "d.txt"], Rows(workbench));
    }

    [Fact]
    public async Task Tv_on_the_last_file_wraps_round_to_an_unviewed_one_before_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 3),
            () => group.ActiveDiffTab?.File.Name == "d.txt" && group.ActiveDiffTab.IsFocused,
            () => commands.TryExecute(CommandIds.ToggleViewed),
            () => group.ActiveDiffTab?.File.Name == "a.txt",
        ]);

        Assert.Single(group.DiffTabs);
    }

    [Fact]
    public async Task Tv_on_the_last_unviewed_file_closes_the_diff_and_says_every_file_is_viewed()
    {
        _gitHub.ViewedFiles = new()
        {
            ["a.txt"] = GitHubViewedState.Viewed, ["c.txt"] = GitHubViewedState.Viewed, ["d.txt"] = GitHubViewedState.Viewed,
        };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 1),
            () => group.ActiveDiffTab?.File.Name == "b.txt" && group.ActiveDiffTab.IsFocused,
            () => commands.TryExecute(CommandIds.ToggleViewed),
            () => workbench.StatusBar.DisplayedText.StartsWith("All files viewed", StringComparison.Ordinal),
        ]);

        Assert.Empty(group.DiffTabs);
        Assert.Equal("Viewed 4 of 4", workbench.Sidebar.Review.ViewedText);
    }

    [Fact]
    public async Task Tv_on_a_viewed_file_unmarks_it_and_stays_and_the_status_bar_follows()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 2),
            () => group.ActiveDiffTab?.File.Name == "c.txt" && group.ActiveDiffTab.IsFocused,
            () => workbench.StatusBar.DisplayedText.Contains("File 3 of 4  •  Viewed  •  +1 −1", StringComparison.Ordinal),
            () => commands.TryExecute(CommandIds.ToggleViewed),
            () => workbench.Sidebar.Review.ViewedText == "Viewed 0 of 4",
            () => workbench.StatusBar.DisplayedText.Contains("File 3 of 4  •  +1 −1", StringComparison.Ordinal),
        ]);

        Assert.Equal(("PR_kw398", "c.txt", false), _gitHub.ViewedChanges.Single());
        Assert.Equal("c.txt", Assert.Single(group.DiffTabs).File.Name);
    }

    [Fact]
    public async Task Tv_is_unavailable_in_a_compare_with_saved_diff()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ViewedText.Length > 0,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => { group.ActiveTab!.Content = "a1\nEDITED\n"; },
            () => commands.TryExecute(CommandIds.CompareToSaved),
            () => group.ActiveDiffTab is { IsFocused: true },
            () => Assert.False(commands.IsEnabled(CommandIds.ToggleViewed)));

        Assert.DoesNotContain(WorkbenchHost.MnemonicsInScope(commands, CommandScope.Diff), e => e.CommandId == CommandIds.ToggleViewed);
        Assert.Empty(_gitHub.ViewedChanges);
    }

    [Fact]
    public async Task Next_change_still_steps_into_a_viewed_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
        [
            .. OpenDiff(host, workbench, commands, rowsDown: 1),
            () => group.ActiveDiffTab?.File.Name == "b.txt",
            () => commands.TryExecute(CommandIds.NextChange),
            () => commands.TryExecute(CommandIds.NextChange),
            () => group.ActiveDiffTab?.File.Name == "c.txt",
        ]);
    }

    private static Delegate[] OpenDiff(WorkbenchHost host, Workbench.Workbench workbench, CommandService commands, int rowsDown) =>
    [
        () => commands.TryExecute(CommandIds.FocusReview),
        () => workbench.Sidebar.Review.ListHasFocus && workbench.Sidebar.Review.ViewedText.Length > 0,
        .. Enumerable.Repeat<Delegate>(() => host.App.InjectKey(Key.CursorDown), rowsDown),
        () => host.App.InjectKey(Key.Enter),
    ];

    private static string[] Rows(Workbench.Workbench workbench) =>
        [.. workbench.Sidebar.Review.Files.Objects.OfType<ReviewFileNode>().Select(f => ReviewRow.Display(f))];

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
