using TuiCode.Abstractions;
using TuiCode.Workbench.History;

namespace TuiCode.Tests;

public class FileHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

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
        var columns = FileHistory.Columns(Commits, Now);

        Assert.Equal("4a91c0e  2 days ago   James Crosswell  Suspend to shell", FileHistory.Row(Commits[0], columns, Now, 80));
        Assert.Equal("9b14c2e  3 hours ago  a-team-app[bot]  Open a file from the command line", FileHistory.Row(Commits[1], columns, Now, 80));
        Assert.Equal("9b14c2e  3 hours ago  a-team-app[bot]  Open a fil…", FileHistory.Row(Commits[1], columns, Now, 50));
    }

    [Fact]
    public void Age_reads_how_long_ago_the_commit_was()
    {
        Assert.Equal("2 days ago", FileHistory.Age(Commits[0], Now));
        Assert.Equal("3 weeks ago", FileHistory.Age(Commits[0] with { Date = Now.AddDays(-21) }, Now));
        Assert.Equal("3 months ago", FileHistory.Age(Commits[0] with { Date = Now.AddDays(-90) }, Now));
    }

    [Theory]
    [InlineData(28, 9)]
    [InlineData(60, 20)]
    [InlineData(20, 8)]
    [InlineData(14, 8)]
    public void PanelHeight_is_a_third_leaving_the_editor_ten_rows_and_the_list_five(int available, int expected)
    {
        Assert.Equal(expected, FileHistory.PanelHeight(available));
        Assert.True(expected >= FileHistory.MinPanelHeight);
    }
}
