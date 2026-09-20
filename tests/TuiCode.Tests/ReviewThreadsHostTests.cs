using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives review threads (#186) through the host against a fake git and gh. Boots a TG Application — serialised (#77).
public class ReviewThreadsHostTests : StaticConfigurationTest
{
    private static readonly DateTimeOffset Posted = new(2026, 9, 20, 6, 54, 0, TimeSpan.Zero);

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public ReviewThreadsHostTests()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha\nbravo\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.txt")];
        _git.RepoFiles["b45e:src/a.txt"] = "alpha\nBRAVO\n";
        _gitHub.PullRequest = new GitHubPullRequest(186, "Threads", "main", "feature", default);
        _gitHub.ReviewThreads =
        [
            Thread(2, "Does this hold?", replies: 1),
            Thread(2, "Settled.", resolved: true),
        ];
    }

    [Fact]
    public async Task The_header_counts_the_threads_and_each_file_shows_its_own()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ThreadsText.Length > 0);

        Assert.Equal("2 threads, 1 unresolved", review.ThreadsText);
        Assert.Equal(2, review.Review?.ThreadsOn("src/a.txt").Count);
    }

    [Fact]
    public async Task Threads_reach_a_diff_that_was_open_before_they_arrived()
    {
        var gate = new TaskCompletionSource();
        _gitHub.ThreadsGate = gate.Task;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is not null,
            () => Assert.Empty(group.ActiveDiffTab!.Threads),
            () => gate.SetResult(),
            () => group.ActiveDiffTab!.Threads.Count == 2);

        Assert.Equal(["Does this hold?", "Settled."], group.ActiveDiffTab!.Threads.Select(t => t.First!.Body));
    }

    [Fact]
    public async Task Enter_expands_the_thread_on_the_current_row_and_still_goes_to_the_line_on_the_others()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab?.Threads.Count == 2,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => group.ActiveDiffTab!.CurrentThread is not null,
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab!.IsExpanded(group.ActiveDiffTab!.CurrentThread!),
            () => host.App.InjectKey(Key.Enter),
            () => !group.ActiveDiffTab!.IsExpanded(group.ActiveDiffTab!.CurrentThread!),
            () => group.ActiveDiffTab!.FirstChange(),
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveTab is not null);

        Assert.Equal(_fs.Path.GetFullPath("/work/src/a.txt"), group.ActiveTab!.File.FullName);
        Assert.Equal(1, group.ActiveTab!.CursorRow);
    }

    [Fact]
    public async Task Enter_on_a_files_outdated_threads_opens_them_in_a_read_only_tab()
    {
        _gitHub.ReviewThreads = [Thread(2, "Does this hold?"), Thread(null, "Gone now.", outdated: true)];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ThreadsText.Length > 0,
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveTab is { Title: "#186 Outdated: src/a.txt" });

        var tab = workbench.Editor.Group.ActiveTab!;
        Assert.True(tab.IsReadOnly);
        Assert.Equal("# #186 outdated threads on src/a.txt", tab.Lines[0]);
        Assert.Contains("Gone now.", tab.Lines);
        Assert.DoesNotContain("Does this hold?", tab.Lines);
    }

    private static GitHubReviewThread Thread(int? line, string body, int replies = 0, bool resolved = false, bool outdated = false) =>
        new("src/a.txt", line, resolved, outdated || line is null,
        [
            new GitHubComment("octocat", Posted, body),
            .. Enumerable.Range(1, replies).Select(i => new GitHubComment("hubot", Posted, $"Reply {i}")),
        ]);

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
