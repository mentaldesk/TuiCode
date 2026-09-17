using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class CamelHumpsTests
{
    [Theory]
    [InlineData("fdl", "FileDirectoryListing.cs")]
    [InlineData("FiDiLi", "FileDirectoryListing.cs")]
    [InlineData("filedir", "FileDirectoryListing.cs")]
    [InlineData("ov.cs", "OpenView.cs")]
    [InlineData("ovcs", "OpenView.cs")]
    [InlineData("OPENVIEW", "OpenView.cs")]
    [InlineData("hp", "HTMLParser.cs")]
    [InlineData("rm", "readme.md")]
    [InlineData("s2t", "string2text")]
    [InlineData("tpn", "third-party-notices")]
    [InlineData("open view", "OpenView.cs")]
    [InlineData("", "anything")]
    public void Score_matches_camel_humps(string query, string candidate) =>
        Assert.NotNull(CamelHumps.Score(query, candidate));

    [Theory]
    [InlineData("iew", "OpenView.cs")]
    [InlineData("pen", "OpenView.cs")]
    [InlineData("fdx", "FileDirectoryListing.cs")]
    [InlineData("o pen", "OpenView.cs")]
    [InlineData("viewopen", "OpenView.cs")]
    public void Score_rejects_characters_that_neither_continue_a_run_nor_start_a_hump(string query, string candidate) =>
        Assert.Null(CamelHumps.Score(query, candidate));

    [Fact]
    public void Score_backtracks_when_the_first_hump_found_leads_nowhere() =>
        Assert.NotNull(CamelHumps.Score("ab", "AxAb"));

    [Fact]
    public void Score_prefers_a_contiguous_prefix_over_jumping_between_humps()
    {
        Assert.Equal(0, CamelHumps.Score("open", "OpenView.cs"));
        Assert.True(CamelHumps.Score("open", "OpenView.cs") < CamelHumps.Score("view", "OpenView.cs"));
    }

    [Theory]
    [InlineData("nav/ov")]
    [InlineData("s/n/ov")]
    [InlineData("/ov")]
    public void ScorePath_matches_query_parts_against_segments_in_order(string query) =>
        Assert.NotNull(CamelHumps.ScorePath(query, ["src", "Navigation", "OpenView.cs"]));

    [Theory]
    [InlineData("ov/nav")]
    [InlineData("nav/src")]
    [InlineData("src/nav")]
    public void ScorePath_requires_the_last_part_to_match_the_last_segment_and_order_to_hold(string query) =>
        Assert.Null(CamelHumps.ScorePath(query, ["src", "Navigation", "OpenView.cs"]));
}
