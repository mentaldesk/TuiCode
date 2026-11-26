using TuiCode.Abstractions;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

public class PullRequestOverviewTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 20, 6, 54, 0, TimeSpan.Zero);

    // 13:30 on 26 Nov in a zone 13 hours ahead of UTC, so a time read in UTC would show.
    private static readonly Times Times = new(new FakeClock(
        new DateTimeOffset(2026, 11, 26, 0, 30, 0, TimeSpan.Zero),
        TimeZoneInfo.CreateCustomTimeZone("Plus13", TimeSpan.FromHours(13), "Plus13", "Plus13")));

    [Fact]
    public void A_pull_request_with_no_comments_is_its_title_author_and_description()
    {
        var text = PullRequestOverview.Build(Conversation("Shows the PR\n", []), Times);

        Assert.Equal("""
        # #183 Review tab shows the PR

        jamescrosswell · 20 Sep 19:54

        Shows the PR

        """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void Each_comment_follows_in_order_under_a_rule_headed_by_its_author_and_local_time()
    {
        var text = PullRequestOverview.Build(Conversation("Shows the PR", [
            new GitHubComment("octocat", new DateTimeOffset(2025, 11, 14, 9, 30, 0, TimeSpan.Zero), "Looks good."),
            new GitHubComment("hubot", new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero), "Merging.\r\nThanks!"),
        ]), Times);

        Assert.Equal("""
        # #183 Review tab shows the PR

        jamescrosswell · 20 Sep 19:54

        Shows the PR

        ---

        octocat · 14 Nov 2025 22:30

        Looks good.

        ---

        hubot · 10:00

        Merging.
        Thanks!

        """.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void An_empty_description_says_so_rather_than_leaving_a_gap()
    {
        var text = PullRequestOverview.Build(Conversation("   \n", []), Times);

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
