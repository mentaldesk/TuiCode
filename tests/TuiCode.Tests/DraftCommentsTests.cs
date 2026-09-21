using TuiCode.Abstractions;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

// Draft line comments kept under ~/.tui/reviews (#188).
public class DraftCommentsTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Drafts_are_back_after_restarting()
    {
        var drafts = Open("/work", 188);
        drafts.Add("src/a.cs", 12, "Rename this?");
        drafts.Add("src/b.cs", 3, "Why the cast?");

        var reopened = Open("/work", 188);

        Assert.Equal(
        [
            new DraftComment("src/a.cs", 12, "Rename this?"),
            new DraftComment("src/b.cs", 3, "Why the cast?"),
        ], reopened.All);
    }

    [Fact]
    public void Each_pull_request_has_its_own_drafts()
    {
        Open("/work", 188).Add("src/a.cs", 12, "On 188");
        Open("/work", 189).Add("src/a.cs", 12, "On 189");

        Assert.Equal(["On 188"], Open("/work", 188).All.Select(d => d.Body));
        Assert.Equal(["On 189"], Open("/work", 189).All.Select(d => d.Body));
    }

    [Fact]
    public void Two_checkouts_of_the_same_pull_request_dont_share_drafts()
    {
        Open("/work/main", 188).Add("src/a.cs", 12, "In main");

        Assert.Empty(Open("/work/pr-188", 188).All);
    }

    [Fact]
    public void Switching_pull_request_reads_the_other_ones_drafts()
    {
        var drafts = Open("/work", 188);
        drafts.Add("src/a.cs", 12, "On 188");

        Switch(drafts, "/work", 189);

        Assert.Empty(drafts.All);
        Switch(drafts, "/work", 188);
        Assert.Equal(["On 188"], drafts.All.Select(d => d.Body));
    }

    [Fact]
    public void Editing_and_deleting_a_draft_are_kept_too()
    {
        var drafts = Open("/work", 188);
        drafts.Add("src/a.cs", 12, "Rename this?");
        drafts.Add("src/a.cs", 20, "And this?");

        drafts.Replace(drafts.All[0], "Rename it to what?");
        drafts.Remove(drafts.All[1]);

        Assert.Equal([new DraftComment("src/a.cs", 12, "Rename it to what?")], Open("/work", 188).All);
    }

    [Fact]
    public void Submitting_clears_them_from_disk_as_well()
    {
        var drafts = Open("/work", 188);
        drafts.Add("src/a.cs", 12, "Rename this?");

        drafts.Clear();

        Assert.Empty(Open("/work", 188).All);
        Assert.Empty(_fs.Directory.GetFiles("/drafts"));
    }

    [Fact]
    public void An_unreadable_file_reads_as_no_drafts_rather_than_throwing()
    {
        Open("/work", 188).Add("src/a.cs", 12, "Rename this?");
        var file = _fs.Directory.GetFiles("/drafts").Single();
        _fs.File.WriteAllText(file, "{ not json");

        Assert.Empty(Open("/work", 188).All);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "Draft review: 1 comment")]
    [InlineData(2, "Draft review: 2 comments")]
    public void The_review_tab_counts_them_at_its_foot(int count, string expected)
    {
        var drafts = Open("/work", 188);
        for (var i = 0; i < count; i++) drafts.Add("src/a.cs", i + 1, "Look here.");

        Assert.Equal(expected, drafts.Line);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 draft comment will be posted with it.")]
    [InlineData(3, "3 draft comments will be posted with it.")]
    public void The_submit_dialog_says_what_goes_with_the_review(int count, string expected) =>
        Assert.Equal(expected, DraftComments.Posting(count));

    private DraftComments Open(string repoRoot, int number)
    {
        var drafts = new DraftComments(_fs, "/drafts");
        Switch(drafts, repoRoot, number);
        return drafts;
    }

    private void Switch(DraftComments drafts, string repoRoot, int number) =>
        drafts.Open(_fs.Path.GetFullPath(repoRoot), number);
}
