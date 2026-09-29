using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class BlameViewTests
{
    [Theory]
    [InlineData(0, "0 seconds ago")]
    [InlineData(1, "1 second ago")]
    [InlineData(89, "89 seconds ago")]
    [InlineData(90, "2 minutes ago")]
    [InlineData(60 * 60, "60 minutes ago")]
    [InlineData(2 * 60 * 60, "2 hours ago")]
    [InlineData(36 * 60 * 60, "2 days ago")]
    [InlineData(13 * 86400, "13 days ago")]
    [InlineData(25 * 86400, "4 weeks ago")]
    [InlineData(100 * 86400, "3 months ago")]
    [InlineData(365 * 86400, "1 year ago")]
    [InlineData(3 * 365 * 86400, "3 years ago")]
    public void Ago_rounds_like_git_s_relative_dates(long seconds, string expected) =>
        Assert.Equal(expected, BlameView.Ago(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void When_gives_the_relative_date_then_the_author_s_own_calendar_date()
    {
        var authored = new DateTimeOffset(2026, 9, 4, 23, 30, 0, TimeSpan.FromHours(-5));
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(13));

        Assert.Equal("3 weeks ago (2026-09-04)", BlameView.When(authored, now));
    }

    [Fact]
    public void A_date_in_the_future_reads_as_now_rather_than_negative() =>
        Assert.Equal("0 seconds ago", BlameView.Ago(TimeSpan.FromMinutes(-5)));
}
