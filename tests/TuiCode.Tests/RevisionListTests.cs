using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class RevisionListTests
{
    private static readonly IReadOnlyList<RevisionEntry> Entries = RevisionList.Build(
        [
            new GitRef("main", GitRefKind.Branch),
            new GitRef("feature/DiffTab", GitRefKind.Branch),
            new GitRef("origin/main", GitRefKind.RemoteBranch),
            new GitRef("v1.2", GitRefKind.Tag),
        ],
        [
            new GitCommit("3f2a9c1", "Register IGitCli\tin Program.cs", DateTimeOffset.UnixEpoch),
            new GitCommit("a1b2c3d", "Fix the main menu", DateTimeOffset.UnixEpoch),
        ]);

    [Fact]
    public void Build_lists_HEAD_branches_remotes_tags_then_commits_with_their_kind()
    {
        Assert.Equal(
        [
            ("HEAD", "HEAD", "checked out"),
            ("main", "main", "branch"),
            ("feature/DiffTab", "feature/DiffTab", "branch"),
            ("origin/main", "origin/main", "branch"),
            ("v1.2", "v1.2", "tag"),
            ("3f2a9c1", "3f2a9c1  Register IGitCli in Program.cs", "commit"),
            ("a1b2c3d", "a1b2c3d  Fix the main menu", "commit"),
        ], Entries.Select(e => (e.Revision, e.Text, e.Kind)));
    }

    [Theory]
    [InlineData("ma", new[] { "main", "origin/main", "a1b2c3d" })]
    [InlineData("fdt", new[] { "feature/DiffTab" })]
    [InlineData("3F2", new[] { "3f2a9c1" })]
    [InlineData("igitcli", new[] { "3f2a9c1" })]
    [InlineData("HEAD~3", new string[0])]
    [InlineData("", new[] { "HEAD", "main", "feature/DiffTab", "origin/main", "v1.2", "3f2a9c1", "a1b2c3d" })]
    public void Filter_matches_names_by_camel_humps_and_commits_by_hash_prefix_or_subject(string filter, string[] expected)
    {
        Assert.Equal(expected, RevisionList.Filter(Entries, filter).Select(e => e.Revision));
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("head")]
    [InlineData(" Head ")]
    public void Selected_is_the_HEAD_row_for_HEAD_in_any_case_above_branches_it_matches(string filter)
    {
        var entries = RevisionList.Build([new GitRef("feature/header", GitRefKind.Branch)], []);
        var visible = RevisionList.Filter(entries, filter);

        Assert.Equal(["HEAD", "feature/header"], visible.Select(e => e.Revision));
        Assert.Equal(0, RevisionList.Selected(visible, filter));
    }

    [Theory]
    [InlineData("v1.2-", 0)]
    [InlineData("v1.2", 1)]
    [InlineData("v1", 2)]
    public void Selected_is_the_ref_named_exactly_even_below_partial_matches(string filter, int expected)
    {
        var entries = RevisionList.Build(
        [
            new GitRef("v1.2-rc", GitRefKind.Tag),
            new GitRef("v1.2", GitRefKind.Tag),
            new GitRef("v1", GitRefKind.Tag),
        ], []);
        var visible = RevisionList.Filter(entries, filter);

        Assert.Equal(expected, RevisionList.Selected(visible, filter));
    }

    [Fact]
    public void Selected_is_the_first_row_with_no_filter_and_none_with_no_rows()
    {
        Assert.Equal(0, RevisionList.Selected(Entries, ""));
        Assert.Null(RevisionList.Selected([], "zzz"));
    }

    [Fact]
    public void Filter_does_not_match_a_hash_in_the_middle()
    {
        Assert.Empty(RevisionList.Filter(Entries, "9c1"));
    }

    [Fact]
    public void Display_right_aligns_the_kind_and_cuts_long_text()
    {
        var entry = new RevisionEntry("abc", "a very long branch name", "branch");

        Assert.Equal("main        branch", new RevisionEntry("main", "main", "branch").Display(18));
        Assert.Equal("a very l…  branch", entry.Display(17));
    }
}
