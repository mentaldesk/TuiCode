using TuiCode.Abstractions;
using TuiCode.Search;

namespace TuiCode.Tests;

public class TextSearchTests
{
    [Fact]
    public void FindAll_is_case_insensitive_and_reports_row_and_column()
    {
        var matches = TextSearch.FindAll(["Hello there", "say HELLO"], "hello");

        Assert.Equal([new TextMatch(0, 0, 5), new TextMatch(1, 4, 5)], matches);
    }

    [Fact]
    public void FindAll_does_not_report_overlapping_matches()
    {
        var matches = TextSearch.FindAll(["aaaa"], "aa");

        Assert.Equal([new TextMatch(0, 0, 2), new TextMatch(0, 2, 2)], matches);
    }

    [Fact]
    public void FindAll_with_empty_query_finds_nothing()
    {
        Assert.Empty(TextSearch.FindAll(["anything"], ""));
    }

    [Fact]
    public void SplitLines_handles_every_line_terminator()
    {
        Assert.Equal(["a", "b", "c", "d"], TextSearch.SplitLines("a\r\nb\nc\rd"));
    }

    [Fact]
    public void IndexAtOrAfter_includes_a_match_at_the_position_and_wraps_past_the_last()
    {
        TextMatch[] matches = [new(0, 2, 1), new(2, 0, 1)];

        Assert.Equal(0, TextSearch.IndexAtOrAfter(matches, 0, 2));
        Assert.Equal(1, TextSearch.IndexAtOrAfter(matches, 0, 3));
        Assert.Equal(0, TextSearch.IndexAtOrAfter(matches, 5, 0));
    }

    [Fact]
    public void IndexAfter_skips_a_match_at_the_position_and_wraps()
    {
        TextMatch[] matches = [new(0, 2, 1), new(2, 0, 1)];

        Assert.Equal(1, TextSearch.IndexAfter(matches, 0, 2));
        Assert.Equal(0, TextSearch.IndexAfter(matches, 2, 0));
    }

    [Fact]
    public void IndexBefore_finds_the_previous_match_and_wraps_to_the_last()
    {
        TextMatch[] matches = [new(0, 2, 1), new(2, 0, 1)];

        Assert.Equal(0, TextSearch.IndexBefore(matches, 2, 0));
        Assert.Equal(1, TextSearch.IndexBefore(matches, 0, 2));
    }

    [Fact]
    public void Index_helpers_return_minus_one_without_matches()
    {
        Assert.Equal(-1, TextSearch.IndexAtOrAfter([], 0, 0));
        Assert.Equal(-1, TextSearch.IndexAfter([], 0, 0));
        Assert.Equal(-1, TextSearch.IndexBefore([], 0, 0));
    }

    [Fact]
    public void ReplaceAll_replaces_every_match_and_preserves_line_endings()
    {
        var result = TextSearch.ReplaceAll("Foo\r\nfoo\nbar", "foo", "baz", out var count);

        Assert.Equal("baz\r\nbaz\nbar", result);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ReplaceAll_with_a_replacement_containing_the_query_does_not_loop()
    {
        var result = TextSearch.ReplaceAll("a-a", "a", "aa", out var count);

        Assert.Equal("aa-aa", result);
        Assert.Equal(2, count);
    }
}
