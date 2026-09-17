using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class OccurrencesTests
{
    [Theory]
    [InlineData(1, 0, 3)]
    [InlineData(0, 0, 3)]
    [InlineData(3, 0, 3)]
    [InlineData(5, 4, 11)]
    public void RunAt_finds_the_word_the_caret_is_in_or_touches(int column, int start, int end)
    {
        Assert.Equal((new Point(start, 0), new Point(end, 0)), Occurrences.RunAt(Lines("foo my_var2."), new Point(column, 0)));
    }

    [Fact]
    public void RunAt_finds_the_whitespace_run_the_caret_is_in()
    {
        Assert.Equal((new Point(1, 0), new Point(4, 0)), Occurrences.RunAt(Lines("a   .b"), new Point(2, 0)));
    }

    [Fact]
    public void RunAt_is_null_between_punctuation()
    {
        Assert.Null(Occurrences.RunAt(Lines("a.,b"), new Point(2, 0)));
    }

    [Fact]
    public void Find_returns_non_overlapping_matches_in_buffer_order()
    {
        Assert.Equal(
            [(new Point(0, 0), new Point(2, 0)), (new Point(2, 0), new Point(4, 0)), (new Point(1, 1), new Point(3, 1))],
            Occurrences.Find(Lines("aaaaa", "baa"), Lines("aa"), whole: false));
    }

    [Fact]
    public void Find_is_case_sensitive()
    {
        Assert.Equal([(new Point(4, 0), new Point(7, 0))], Occurrences.Find(Lines("Foo foo"), Lines("foo"), whole: false));
    }

    [Fact]
    public void Find_whole_skips_matches_inside_longer_words()
    {
        Assert.Equal(
            [(new Point(0, 0), new Point(3, 0)), (new Point(12, 0), new Point(15, 0))],
            Occurrences.Find(Lines("foo foobar (foo)"), Lines("foo"), whole: true));
    }

    [Fact]
    public void Find_whole_matches_only_identical_whitespace_runs()
    {
        Assert.Equal(
            [(new Point(1, 0), new Point(3, 0)), (new Point(0, 1), new Point(2, 1))],
            Occurrences.Find(Lines("a  b    c", "  d"), Lines("  "), whole: true));
    }

    [Fact]
    public void Find_matches_across_lines()
    {
        Assert.Equal(
            [(new Point(1, 0), new Point(1, 1)), (new Point(1, 1), new Point(1, 2))],
            Occurrences.Find(Lines("xab", "cab", "cd"), Lines("ab", "c"), whole: false));
    }

    private static string[][] Lines(params string[] lines) => [.. lines.Select(line => line.Select(c => c.ToString()).ToArray())];
}
