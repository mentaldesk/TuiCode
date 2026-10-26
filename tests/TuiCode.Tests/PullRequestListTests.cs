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
