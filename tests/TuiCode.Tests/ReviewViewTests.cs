using Terminal.Gui.Drawing;
using TuiCode.Abstractions;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

// Not hosted in a running app, so Refresh applies its result before the task completes.
public class ReviewViewTests
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    public ReviewViewTests() => _fs.AddDirectory("/work");

    [Fact]
    public async Task Refresh_lists_changed_files_under_their_folders_with_status_marks()
    {
        _git.Changes =
        [
            new GitChange(GitChangeKind.Modified, "src/Workbench/WorkbenchHost.cs"),
            new GitChange(GitChangeKind.Added, "src/Workbench/CommandScope.cs"),
            new GitChange(GitChangeKind.Deleted, "tests/Old.cs"),
            new GitChange(GitChangeKind.Renamed, "src/New.cs", "src/Old.cs"),
            new GitChange(GitChangeKind.Modified, "README.md"),
        ];
        using var view = Build();

        await view.Refresh();

        Assert.Equal("feature ← main  (no PR)", view.HeaderText);
        Assert.True(view.Files.Visible);
        Assert.Equal(
        [
            "src", "  R New.cs",
            "src/Workbench", "  A CommandScope.cs", "  M WorkbenchHost.cs",
            "tests", "  D Old.cs",
            "M README.md",
        ], Rows(view));
    }

    [Fact]
    public async Task No_changes_shows_one_label_naming_the_base()
    {
        using var view = Build();

        await view.Refresh();

        Assert.Equal("No changes against main", view.HeaderText);
        Assert.False(view.Files.Visible);
    }

    [Fact]
    public async Task Outside_a_git_repo_says_so()
    {
        _git.Root = null;
        using var view = Build();

        await view.Refresh();

        Assert.Equal("Not a git repository", view.HeaderText);
        Assert.False(view.Files.Visible);
    }

    [Fact]
    public async Task A_git_failure_shows_its_message()
    {
        _git.Missing = true;
        using var view = Build();

        await view.Refresh();

        Assert.Equal("git isn't installed or isn't on PATH", view.HeaderText);
        Assert.False(view.Files.Visible);
    }

    [Fact]
    public async Task A_detached_HEAD_is_named_HEAD()
    {
        _git.Branch = null;
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        using var view = Build();

        await view.Refresh();

        Assert.Equal("HEAD ← main  (no PR)", view.HeaderText);
    }

    [Fact]
    public async Task Refresh_keeps_the_selected_file_selected()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt"), new GitChange(GitChangeKind.Modified, "b.txt")];
        using var view = Build();
        await view.Refresh();
        view.Files.SelectedObject = view.Files.Objects!.Last();

        _git.Changes = [new GitChange(GitChangeKind.Added, "0.txt"), .. _git.Changes];
        await view.Refresh();

        Assert.Equal("M b.txt", view.Files.SelectedObject?.ToString());
    }

    [Fact]
    public async Task Enter_on_a_file_raises_FileActivated_and_on_a_folder_collapses_it()
    {
        _git.Changes = [new GitChange(GitChangeKind.Renamed, "src/New.cs", "lib/Old.cs")];
        using var view = Build();
        await view.Refresh();
        (BranchReview Review, GitChange Change)? activated = null;
        view.FileActivated += (_, e) => activated = e;
        var folder = view.Files.Objects!.Single();

        view.Files.SelectedObject = folder.Children[0];
        view.Files.NewKeyDownEvent(Key.Enter);
        view.Files.SelectedObject = folder;
        view.Files.NewKeyDownEvent(Key.Enter);

        Assert.Equal(new GitChange(GitChangeKind.Renamed, "src/New.cs", "lib/Old.cs"), activated?.Change);
        Assert.Equal("b45e", activated?.Review.MergeBase);
        Assert.False(view.Files.IsExpanded(folder));
    }

    [Fact]
    public async Task A_pull_request_puts_its_number_title_branches_and_checks_above_the_files()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _gitHub.PullRequest = new GitHubPullRequest(132, "Command scopes should be fixed", "main", "feature", new GitHubChecks(11, 1, 2));
        using var view = Build();

        await view.Refresh();

        Assert.Equal("#132 Command scopes should be fixed", view.TitleText);
        Assert.Equal("main ← feature", view.HeaderText);
        Assert.Equal("✓ 11  ✗ 1  ● 2 checks", view.ChecksText);
        Assert.Equal("", view.HintText);
        Assert.True(view.Files.Visible);
    }

    [Fact]
    public async Task Without_a_pull_request_only_the_branch_line_shows()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        using var view = Build();

        await view.Refresh();

        Assert.Equal("feature ← main  (no PR)", view.HeaderText);
        Assert.Equal("", view.TitleText);
        Assert.Equal("", view.ChecksText);
        Assert.Equal("", view.HintText);
    }

    [Fact]
    public async Task Without_gh_the_files_still_show_and_the_hint_says_how_to_get_pull_requests()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _gitHub.Missing = true;
        using var view = Build();

        await view.Refresh();

        Assert.Equal("Pull requests need the GitHub CLI: run gh auth login", view.HintText);
        Assert.Equal("feature ← main  (no PR)", view.HeaderText);
        Assert.True(view.Files.Visible);
    }

    [Fact]
    public async Task The_hint_is_drawn_faint()
    {
        _gitHub.Missing = true;
        using var view = Build();

        await view.Refresh();

        Assert.True(view.Hint.GetAttributeForRole(VisualRole.Normal).Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public async Task Outside_a_git_repo_gh_is_never_asked()
    {
        _git.Root = null;
        using var view = Build();

        await view.Refresh();

        Assert.Equal(0, _gitHub.Calls);
    }

    [Fact]
    public async Task A_pull_request_against_another_base_lists_the_files_against_that_base()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _gitHub.PullRequest = new GitHubPullRequest(9, "Fix", "release/1.0", "feature", default);
        _git.Resolvable.Add("origin/release/1.0");
        _git.MergeBases["origin/release/1.0"] = "cafe";
        _git.ChangesByRevision["cafe"] = [new GitChange(GitChangeKind.Added, "b.txt")];
        using var view = Build();

        await view.Refresh();

        Assert.Equal("release/1.0 ← feature", view.HeaderText);
        Assert.Equal("origin/release/1.0", view.Review?.Base);
        Assert.Equal("cafe", view.Review?.MergeBase);
        Assert.Equal(["A b.txt"], Rows(view));
    }

    [Fact]
    public async Task A_pull_request_that_gh_cant_answer_for_leaves_the_files_alone_and_says_why()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _gitHub.Error = "HTTP 502: Bad gateway";
        using var view = Build();

        await view.Refresh();

        Assert.Equal("HTTP 502: Bad gateway", view.HintText);
        Assert.Equal(["M a.txt"], Rows(view));
    }

    private ReviewView Build() => new(_git, _gitHub) { RootProvider = () => _fs.DirectoryInfo.New("/work") };

    private static List<string> Rows(ReviewView view) =>
        view.Files.Objects!.SelectMany(n => n is ReviewFolderNode
            ? [n.ToString()!, .. n.Children.Select(c => "  " + c)]
            : new[] { n.ToString()! }).ToList();
}
