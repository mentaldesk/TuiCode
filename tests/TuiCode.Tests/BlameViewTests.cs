using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class BlameViewTests : StaticConfigurationTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(30, "now")]
    [InlineData(5 * 60, "5m ago")]
    [InlineData(3 * 60 * 60, "3h ago")]
    [InlineData(2 * 86400, "2d ago")]
    [InlineData(17 * 86400, "9 Nov")]
    [InlineData(382 * 86400, "9 Nov 2025")]
    public void The_commit_s_age_reads_the_short_way_with_no_date_after_it(long seconds, string age)
    {
        using var view = Blame(Now.AddSeconds(-seconds), Now);

        Assert.Equal($"4a91c0e  Ada  {age}", view.Commit);
    }

    [Fact]
    public void A_date_past_a_week_is_the_day_in_my_local_time()
    {
        var auckland = TimeZoneInfo.CreateCustomTimeZone("Auckland", TimeSpan.FromHours(13), "Auckland", "Auckland");

        using var view = Blame(new DateTimeOffset(2026, 11, 9, 20, 0, 0, TimeSpan.Zero), Now, auckland);

        Assert.Equal("4a91c0e  Ada  10 Nov", view.Commit);
    }

    [Fact]
    public void A_date_in_the_future_reads_as_now() =>
        Assert.Equal("4a91c0e  Ada  now", Blame(Now.AddMinutes(5), Now).Commit);

    private static BlameView Blame(DateTimeOffset authored, DateTimeOffset now, TimeZoneInfo? zone = null) =>
        new("a.cs", 1, new GitBlameLine("4a91c0e2b7d1f3e5a6c8b9d0e1f2a3b4c5d6e7f8", "Ada", authored, "Fix it", "x", "a.cs", null),
            new Times(new FakeClock(now, zone)));
}
