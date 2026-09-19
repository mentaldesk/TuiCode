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
    public void Build_lists_branches_remotes_tags_then_commits_with_their_kind()
    {
        Assert.Equal(
        [
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
    [InlineData("", new[] { "main", "feature/DiffTab", "origin/main", "v1.2", "3f2a9c1", "a1b2c3d" })]
    public void Filter_matches_names_by_camel_humps_and_commits_by_hash_prefix_or_subject(string filter, string[] expected)
    {
        Assert.Equal(expected, RevisionList.Filter(Entries, filter).Select(e => e.Revision));
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
