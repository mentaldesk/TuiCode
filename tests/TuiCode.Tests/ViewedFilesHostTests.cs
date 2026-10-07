using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives toggle viewed (#396) through the host against a fake git and gh. Boots a TG Application — serialised (#77).
public class ViewedFilesHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();

    public ViewedFilesHostTests()
    {
        _fs.AddFile("/work/src/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/work/src/b.txt", new MockFileData("bravo\n"));
        _git.Root = _fs.Path.GetFullPath("/work");
        _git.Changes = [new GitChange(GitChangeKind.Modified, "src/a.txt"), new GitChange(GitChangeKind.Modified, "src/b.txt")];
        _gitHub.PullRequest = new GitHubPullRequest(396, "Viewed", "main", "feature", default, Id: "PR_kw396");
        _gitHub.ViewedFiles = new() { ["src/b.txt"] = GitHubViewedState.Viewed };
    }

    [Fact]
    public async Task The_foot_counts_the_files_GitHub_says_are_viewed()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await InReviewList(host, workbench, commands);

        Assert.Equal("Viewed 1 of 2", workbench.Sidebar.Review.ViewedText);
        Assert.Equal(["a.txt", "b.txt  ✓"], Rows(workbench));
    }

    [Fact]
    public async Task Space_marks_the_selected_file_viewed_on_GitHub_and_again_unmarks_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await InReviewList(host, workbench, commands);
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 2 of 2");

        Assert.Equal(("PR_kw396", "src/a.txt", true), _gitHub.ViewedChanges.Single());
        Assert.Equal(["a.txt  ✓", "b.txt  ✓"], Rows(workbench));
        Assert.Equal("src/a.txt", review.SelectedFile?.Path);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 1 of 2");

        Assert.Equal(("PR_kw396", "src/a.txt", false), _gitHub.ViewedChanges[^1]);
        Assert.True(review.ListHasFocus);
    }

    [Fact]
    public async Task Space_on_a_file_changed_since_it_was_viewed_marks_it_viewed_again()
    {
        _gitHub.ViewedFiles = new() { ["src/a.txt"] = GitHubViewedState.Dismissed, ["src/b.txt"] = GitHubViewedState.Viewed };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await InReviewList(host, workbench, commands);
        Assert.Equal("Viewed 1 of 2", workbench.Sidebar.Review.ViewedText);
        Assert.Equal(["a.txt  changed", "b.txt  ✓"], Rows(workbench));

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => workbench.Sidebar.Review.ViewedText == "Viewed 2 of 2");

        Assert.Equal(("PR_kw396", "src/a.txt", true), _gitHub.ViewedChanges.Single());
        Assert.Equal(["a.txt  ✓", "b.txt  ✓"], Rows(workbench));
    }

    [Fact]
    public async Task Tv_does_the_same_as_space()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await InReviewList(host, workbench, commands);
        await HostSteps.Run(host,
            () => Assert.True(commands.TryExecute(CommandIds.ToggleViewed)),
            () => workbench.Sidebar.Review.ViewedText == "Viewed 2 of 2");

        Assert.Equal("tv", CommandMnemonics.For(CommandIds.ToggleViewed));
        Assert.Contains(WorkbenchHost.MnemonicsInScope(commands, CommandScope.Review), e => e.CommandId == CommandIds.ToggleViewed);
    }

    [Fact]
    public async Task A_mark_GitHub_refuses_leaves_the_file_as_it_was_and_says_why()
    {
        _gitHub.ViewedError = "error connecting to api.github.com";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await InReviewList(host, workbench, commands);
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => workbench.StatusBar.DisplayedText.StartsWith(_gitHub.ViewedError, StringComparison.Ordinal));

        Assert.Equal("Viewed 1 of 2", workbench.Sidebar.Review.ViewedText);
        Assert.Equal(["a.txt", "b.txt  ✓"], Rows(workbench));
    }

    [Fact]
    public async Task On_a_branch_with_no_PR_space_marks_the_file_locally_and_it_stays_marked_after_restarting()
    {
        _gitHub.PullRequest = null;
        using (var workbench = BuildWorkbench())
        using (var host = BuildHost(workbench, out var commands))
        {
            await InReviewList(host, workbench, commands);
            Assert.Equal("Viewed 0 of 2", workbench.Sidebar.Review.ViewedText);

            await HostSteps.Run(host,
                () => host.App.InjectKey(Key.Space),
                () => workbench.Sidebar.Review.ViewedText == "Viewed 1 of 2");
            Assert.Equal(["a.txt  ✓", "b.txt"], Rows(workbench));
        }

        Assert.Empty(_gitHub.ViewedChanges);
        using var reopened = BuildWorkbench();
        using var again = BuildHost(reopened, out var reopenedCommands);
        await InReviewList(again, reopened, reopenedCommands);

        Assert.Equal("Viewed 1 of 2", reopened.Sidebar.Review.ViewedText);
        Assert.Equal(["a.txt  ✓", "b.txt"], Rows(reopened));
    }

    [Fact]
    public async Task On_a_branch_with_no_PR_editing_a_viewed_file_shows_it_changed_until_its_marked_again()
    {
        _gitHub.PullRequest = null;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await InReviewList(host, workbench, commands);
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 1 of 2",
            () => _fs.File.WriteAllText("/work/src/a.txt", "alpha\nedited\n"),
            () => commands.TryExecute(CommandIds.FocusReview),
            () => review.ViewedText == "Viewed 0 of 2");

        Assert.Equal(["a.txt  changed", "b.txt"], Rows(workbench));

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 1 of 2");

        Assert.Equal(["a.txt  ✓", "b.txt"], Rows(workbench));
    }

    [Fact]
    public async Task Space_on_a_folder_marks_the_files_under_it_not_yet_viewed()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await InReviewList(host, workbench, commands);
        await OnFolder(host, review, "src");
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 2 of 2");

        Assert.Equal(("PR_kw396", "src/a.txt", true), _gitHub.ViewedChanges.Single());
        Assert.Equal(["a.txt  ✓", "b.txt  ✓"], Rows(workbench));
        Assert.True(Assert.IsType<ReviewFolderNode>(review.Files.SelectedObject).Viewed);
        Assert.True(review.Files.IsExpanded(review.Files.SelectedObject!));
    }

    [Fact]
    public async Task Space_on_a_folder_with_none_viewed_marks_them_all_and_again_unmarks_them_all()
    {
        _gitHub.ViewedFiles = [];
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await InReviewList(host, workbench, commands);
        await OnFolder(host, review, "src");
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 2 of 2");

        Assert.Equal(["src/a.txt", "src/b.txt"], _gitHub.ViewedChanges.Where(c => c.Viewed).Select(c => c.Path).Order());

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 0 of 2");

        Assert.Equal(["src/a.txt", "src/b.txt"], _gitHub.ViewedChanges.Where(c => !c.Viewed).Select(c => c.Path).Order());
        Assert.Equal(["a.txt", "b.txt"], Rows(workbench));
        Assert.False(Assert.IsType<ReviewFolderNode>(review.Files.SelectedObject).Viewed);
    }

    [Fact]
    public async Task Space_on_a_folder_marks_the_files_in_its_subfolders_too()
    {
        _fs.AddFile("/work/src/deep/c.txt", new MockFileData("charlie\n"));
        _git.Changes = [.. _git.Changes, new GitChange(GitChangeKind.Modified, "src/deep/c.txt")];
        _gitHub.ViewedFiles = new() { ["src/a.txt"] = GitHubViewedState.Viewed, ["src/b.txt"] = GitHubViewedState.Viewed };
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await InReviewList(host, workbench, commands);
        await OnFolder(host, review, "src");
        Assert.False(Assert.IsType<ReviewFolderNode>(review.Files.SelectedObject).Viewed);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => review.ViewedText == "Viewed 3 of 3");

        Assert.Equal(("PR_kw396", "src/deep/c.txt", true), _gitHub.ViewedChanges.Single());
        Assert.All(review.Files.Objects.OfType<ReviewFolderNode>(), f => Assert.True(f.Viewed, f.Path));
    }

    [Fact]
    public async Task On_a_branch_with_no_PR_space_on_a_folder_marks_its_files_locally()
    {
        _gitHub.PullRequest = null;
        using (var workbench = BuildWorkbench())
        using (var host = BuildHost(workbench, out var commands))
        {
            var review = workbench.Sidebar.Review;
            await InReviewList(host, workbench, commands);
            await OnFolder(host, review, "src");
            await HostSteps.Run(host,
                () => host.App.InjectKey(Key.Space),
                () => review.ViewedText == "Viewed 2 of 2");
        }

        Assert.Empty(_gitHub.ViewedChanges);
        using var reopened = BuildWorkbench();
        using var again = BuildHost(reopened, out var reopenedCommands);
        await InReviewList(again, reopened, reopenedCommands);

        Assert.Equal("Viewed 2 of 2", reopened.Sidebar.Review.ViewedText);
    }

    [Fact]
    public async Task A_folder_GitHub_partly_refuses_says_why_and_shows_what_GitHub_holds()
    {
        _gitHub.ViewedFiles = [];
        _gitHub.ViewedError = "error connecting to api.github.com";
        _gitHub.ViewedRefused.Add("src/b.txt");
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var review = workbench.Sidebar.Review;

        await InReviewList(host, workbench, commands);
        await OnFolder(host, review, "src");
        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space),
            () => workbench.StatusBar.DisplayedText.StartsWith(_gitHub.ViewedError, StringComparison.Ordinal)
                && review.ViewedText == "Viewed 1 of 2");

        Assert.Equal(2, _gitHub.ViewedChanges.Count);
        Assert.Equal(["a.txt  ✓", "b.txt"], Rows(workbench));
        Assert.False(review.Files.Objects.OfType<ReviewFolderNode>().Single().Viewed);
    }

    private static Task InReviewList(WorkbenchHost host, Workbench.Workbench workbench, CommandService commands) =>
        HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.ListHasFocus && workbench.Sidebar.Review.ViewedText.Length > 0);

    private static Task OnFolder(WorkbenchHost host, ReviewView review, string path) =>
        HostSteps.Run(host,
            () => { review.Files.SelectedObject = review.Files.Objects.OfType<ReviewFolderNode>().Single(f => f.Path == path); },
            () => review.SelectedFolder == path);

    private static string[] Rows(Workbench.Workbench workbench) =>
        [.. workbench.Sidebar.Review.Files.Objects.SelectMany(n => n.Children).OfType<ReviewFileNode>().Select(f => ReviewRow.Display(f))];

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
