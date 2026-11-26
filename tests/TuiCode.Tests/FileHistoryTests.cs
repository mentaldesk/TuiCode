using TuiCode.Abstractions;
using TuiCode.Workbench.History;

namespace TuiCode.Tests;

public class FileHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Times Times = new(new FakeClock(Now));

    private static readonly GitFileCommit[] Commits =
    [
        new("4a91c0e2b7d1f3e5a6c8b9d0e1f2a3b4c5d6e7f8", "James Crosswell", Now.AddDays(-2), "Suspend to shell", "a.cs", "a.cs"),
        new("9b14c2e2b7d1f3e5a6c8b9d0e1f2a3b4c5d6e7f8", "a-team-app[bot]", Now.AddHours(-3), "Open a file from the command line", "a.cs", "a.cs"),
    ];

    [Theory]
    [InlineData("4A9", "Suspend to shell")]
    [InlineData("crosswell", "Suspend to shell")]
    [InlineData("COMMAND LINE", "Open a file from the command line")]
    [InlineData("  ", "Suspend to shell", "Open a file from the command line")]
    [InlineData("c0e")]
    public void Filter_matches_a_hash_prefix_or_the_author_or_subject_in_any_case(string filter, params string[] expected) =>
        Assert.Equal(expected, FileHistory.Filter(Commits, filter).Select(c => c.Subject));

    [Fact]
    public void Row_lines_up_hash_age_author_and_subject_and_cuts_only_at_the_edge()
    {
        var columns = FileHistory.Columns(Commits, Times);

        Assert.Equal("4a91c0e  2d  James Crosswell  Suspend to shell", FileHistory.Row(Commits[0], columns, Times, 80));
        Assert.Equal("9b14c2e  3h  a-team-app[bot]  Open a file from the command line", FileHistory.Row(Commits[1], columns, Times, 80));
        Assert.Equal("9b14c2e  3h  a-team-app[bot]  Open a file from th…", FileHistory.Row(Commits[1], columns, Times, 50));
    }

    [Theory]
    [InlineData(30, "now")]
    [InlineData(5 * 60, "5m")]
    [InlineData(3 * 60 * 60, "3h")]
    [InlineData(2 * 86400, "2d")]
    [InlineData(4 * 86400, "4d")]
    [InlineData(40 * 86400, "3 Sep")]
    [InlineData(400 * 86400, "8 Sep 2025")]
    public void Age_reads_the_short_way(long seconds, string age) =>
        Assert.Equal(age, FileHistory.Age(Commits[0] with { Date = Now.AddSeconds(-seconds) }, Times));

    [Fact]
    public void The_age_column_is_as_wide_as_the_widest_age_and_right_aligned()
    {
        GitFileCommit[] commits = [Commits[1], Commits[0] with { Date = Now.AddDays(-400) }];

        var columns = FileHistory.Columns(commits, Times);

        Assert.Equal("8 Sep 2025".Length, columns.Age);
        Assert.StartsWith("9b14c2e          3h  ", FileHistory.Row(commits[0], columns, Times, 80));
        Assert.StartsWith("4a91c0e  8 Sep 2025  ", FileHistory.Row(commits[1], columns, Times, 80));
    }
}
