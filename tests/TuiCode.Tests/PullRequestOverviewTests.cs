using TuiCode.Abstractions;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

public class PullRequestOverviewTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 20, 6, 54, 0, TimeSpan.Zero);

    [Fact]
    public void A_pull_request_with_no_comments_is_its_title_author_and_description()
    {
        var text = PullRequestOverview.Build(Conversation("Shows the PR\n", []));

        Assert.Equal("""
        # #183 Review tab shows the PR

        jamescrosswell · 2026-09-20 06:54

        Shows the PR

        """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void Each_comment_follows_in_order_under_a_rule_headed_by_its_author_and_date()
    {
        var text = PullRequestOverview.Build(Conversation("Shows the PR", [
            new GitHubComment("octocat", Opened.AddHours(1), "Looks good."),
            new GitHubComment("hubot", Opened.AddDays(1), "Merging.\r\nThanks!"),
        ]));

        Assert.Equal("""
        # #183 Review tab shows the PR

        jamescrosswell · 2026-09-20 06:54

        Shows the PR

        ---

        octocat · 2026-09-20 07:54

        Looks good.

        ---

        hubot · 2026-09-21 06:54

        Merging.
        Thanks!

        """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void An_empty_description_says_so_rather_than_leaving_a_gap()
    {
        var text = PullRequestOverview.Build(Conversation("   \n", []));

        Assert.Contains("*No description*", text);
    }

    [Fact]
    public void The_title_of_the_tab_is_the_pull_request_number_and_Overview()
    {
        Assert.Equal("#183 Overview", PullRequestOverview.TitleOf(183));
    }

    private static GitHubConversation Conversation(string body, IReadOnlyList<GitHubComment> comments) =>
        new(183, "Review tab shows the PR", "jamescrosswell", Opened, body, comments);
}
