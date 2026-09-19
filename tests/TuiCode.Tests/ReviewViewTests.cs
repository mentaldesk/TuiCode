using TuiCode.Abstractions;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

// Not hosted in a running app, so Refresh applies its result before the task completes.
public class ReviewViewTests
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };

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

    private ReviewView Build() => new(_git) { RootProvider = () => _fs.DirectoryInfo.New("/work") };

    private static List<string> Rows(ReviewView view) =>
        view.Files.Objects!.SelectMany(n => n is ReviewFolderNode
            ? [n.ToString()!, .. n.Children.Select(c => "  " + c)]
            : new[] { n.ToString()! }).ToList();
}
