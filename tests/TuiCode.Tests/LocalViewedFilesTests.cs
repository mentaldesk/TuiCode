using TuiCode.Abstractions;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

// Viewed marks on a branch with no PR, kept under ~/.tui/viewed (#400).
public class LocalViewedFilesTests
{
    private readonly MockFileSystem _fs = new();

    public LocalViewedFilesTests()
    {
        foreach (var root in new[] { "/work/main", "/work/other" })
        {
            _fs.AddFile($"{root}/a.cs", new MockFileData("alpha\n"));
            _fs.AddFile($"{root}/b.cs", new MockFileData("bravo\n"));
        }
    }

    [Fact]
    public void Marks_are_back_after_restarting()
    {
        Marks().Set(Review(), "a.cs", true);

        Assert.Equal(new Dictionary<string, GitHubViewedState> { ["a.cs"] = GitHubViewedState.Viewed }, Marks().Read(Review()));
    }

    [Fact]
    public void Unmarking_forgets_the_file()
    {
        Marks().Set(Review(), "a.cs", true);
        Marks().Set(Review(), "b.cs", true);

        Marks().Set(Review(), "a.cs", false);

        Assert.Equal(["b.cs"], Marks().Read(Review()).Keys);
    }

    [Fact]
    public void Another_checkout_or_branch_has_its_own_marks()
    {
        Marks().Set(Review(), "a.cs", true);

        Assert.Empty(Marks().Read(Review("/work/other")));
        Assert.Empty(Marks().Read(Review(branch: "other")));
    }

    [Fact]
    public void A_file_whose_content_changed_since_it_was_marked_reads_as_changed_and_the_others_dont()
    {
        Marks().Set(Review(), "a.cs", true);
        Marks().Set(Review(), "b.cs", true);

        _fs.File.WriteAllText("/work/main/a.cs", "alpha\nmore\n");

        Assert.Equal(new Dictionary<string, GitHubViewedState>
        {
            ["a.cs"] = GitHubViewedState.Dismissed,
            ["b.cs"] = GitHubViewedState.Viewed,
        }, Marks().Read(Review()));
    }

    [Fact]
    public void Marking_a_changed_file_again_makes_it_viewed()
    {
        Marks().Set(Review(), "a.cs", true);
        _fs.File.WriteAllText("/work/main/a.cs", "alpha\nmore\n");

        Marks().Set(Review(), "a.cs", true);

        Assert.Equal(GitHubViewedState.Viewed, Marks().Read(Review())["a.cs"]);
    }

    [Fact]
    public void A_deleted_file_stays_viewed_until_it_comes_back()
    {
        _fs.File.Delete("/work/main/a.cs");
        Marks().Set(Review(), "a.cs", true);

        Assert.Equal(GitHubViewedState.Viewed, Marks().Read(Review())["a.cs"]);

        _fs.AddFile("/work/main/a.cs", new MockFileData("alpha\n"));
        Assert.Equal(GitHubViewedState.Dismissed, Marks().Read(Review())["a.cs"]);
    }

    [Fact]
    public void A_mark_on_a_file_the_branch_no_longer_changes_isnt_listed()
    {
        Marks().Set(Review(), "a.cs", true);

        Assert.Empty(Marks().Read(Review() with { Changes = [new GitChange(GitChangeKind.Modified, "b.cs")] }));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "a.cs": 3 }""")]
    public void Marks_that_cant_be_read_are_none(string content)
    {
        Marks().Set(Review(), "b.cs", true);
        _fs.File.WriteAllText(Assert.Single(_fs.Directory.GetFiles("/viewed")), content);

        Assert.Empty(Marks().Read(Review()));
    }

    private LocalViewedFiles Marks() => new(_fs, "/viewed");

    private static BranchReview Review(string root = "/work/main", string branch = "feature") =>
        new(root, branch, "origin/main", "b45e", [new GitChange(GitChangeKind.Modified, "a.cs"), new GitChange(GitChangeKind.Modified, "b.cs")]);
}
