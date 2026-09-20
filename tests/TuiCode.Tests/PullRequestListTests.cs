using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class PullRequestListTests
{
    private static readonly IReadOnlyList<GitHubPullRequestSummary> Entries =
    [
        new(132, "Command scopes should be fixed", "jamescrosswell", ReviewRequested: true),
        new(129, "Delete, rename and move files", "octocat"),
        new(32, "Scope the status bar", "octocat"),
    ];

    [Fact]
    public void Filter_with_nothing_typed_keeps_every_pull_request()
    {
        Assert.Same(Entries, PullRequestList.Filter(Entries, "  "));
    }

    [Fact]
    public void Filter_matches_the_number_with_or_without_a_hash()
    {
        Assert.Equal([132], PullRequestList.Filter(Entries, "132").Select(pr => pr.Number));
        Assert.Equal([132], PullRequestList.Filter(Entries, "#132").Select(pr => pr.Number));
    }

    [Fact]
    public void Filter_matches_part_of_the_title_or_the_author_whatever_the_case()
    {
        Assert.Equal([132, 32], PullRequestList.Filter(Entries, "SCOPE").Select(pr => pr.Number));
        Assert.Equal([129, 32], PullRequestList.Filter(Entries, "Octocat").Select(pr => pr.Number));
    }

    [Fact]
    public void Display_ends_a_requested_review_with_the_marker_and_leaves_the_others_without_one()
    {
        Assert.Equal("#132  Command scopes should be fixed    jamescrosswell ●", PullRequestList.Display(Entries[0], 56));
        Assert.Equal("#129  Delete, rename and move files  octocat  ", PullRequestList.Display(Entries[1], 46));
    }

    [Fact]
    public void Display_truncates_a_title_too_long_for_the_row()
    {
        var row = PullRequestList.Display(Entries[0], 30);

        Assert.Equal(30, row.Length);
        Assert.EndsWith("jamescrosswell ●", row, StringComparison.Ordinal);
        Assert.Contains('…', row);
    }
}
