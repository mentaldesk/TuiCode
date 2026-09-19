using TuiCode.Editor;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

public class DocumentStatsTests
{
    [Fact]
    public void Of_an_empty_buffer_counts_its_one_line()
    {
        Assert.Equal(new DocumentStats(1, 0, 0, 0), DocumentStats.Of([""]));
    }

    [Fact]
    public void Of_counts_the_empty_line_after_a_trailing_newline()
    {
        Assert.Equal(new DocumentStats(3, 2, 10, 10), DocumentStats.Of(["alpha", "bravo", ""]));
    }

    [Fact]
    public void Of_counts_words_as_runs_of_non_whitespace()
    {
        Assert.Equal(new DocumentStats(2, 5, 27, 20), DocumentStats.Of(["  int x=1;  // one", "\tfoo(bar)"]));
    }

    [Fact]
    public void Of_treats_tabs_and_other_whitespace_as_spaces()
    {
        Assert.Equal(new DocumentStats(1, 3, 7, 3), DocumentStats.Of(["a\tb\u00A0\u2003c\v"]));
    }

    [Fact]
    public void Of_counts_emoji_and_combining_marks_as_one_character_each()
    {
        Assert.Equal(new DocumentStats(1, 2, 5, 4), DocumentStats.Of(["e\u0301\U0001F468\u200D\U0001F469\u200D\U0001F467 \U0001F44D\U0001F3FD!"]));
    }

    [Fact]
    public void Of_ranges_counts_a_range_inside_one_line()
    {
        Assert.Equal(new DocumentStats(1, 2, 7, 6), DocumentStats.Of(["alpha bravo charlie"], [Range(0, 3, 0, 10)]));
    }

    [Fact]
    public void Of_ranges_counts_a_range_across_lines_without_the_line_breaks()
    {
        Assert.Equal(new DocumentStats(3, 3, 11, 11), DocumentStats.Of(["alpha", "bravo", "charlie"], [Range(0, 2, 2, 3)]));
    }

    [Fact]
    public void Of_ranges_ends_a_word_at_a_line_break()
    {
        Assert.Equal(2, DocumentStats.Of(["ab", "cd"], [Range(0, 0, 1, 2)]).Words);
    }

    [Fact]
    public void Of_ranges_leaves_out_the_line_a_range_ends_at_column_0_of()
    {
        Assert.Equal(new DocumentStats(2, 2, 10, 10), DocumentStats.Of(["alpha", "bravo", "charlie"], [Range(0, 0, 2, 0)]));
    }

    [Fact]
    public void Of_ranges_counts_an_empty_range_as_nothing()
    {
        Assert.Equal(new DocumentStats(0, 0, 0, 0), DocumentStats.Of(["alpha"], [Range(0, 2, 0, 2)]));
    }

    [Fact]
    public void Of_ranges_counts_overlapping_ranges_once()
    {
        Assert.Equal(DocumentStats.Of(["alpha bravo", "charlie"], [Range(0, 2, 1, 4)]),
            DocumentStats.Of(["alpha bravo", "charlie"], [Range(0, 6, 1, 4), Range(0, 2, 0, 8)]));
    }

    [Fact]
    public void Of_ranges_counts_adjacent_ranges_as_one_run_of_text()
    {
        Assert.Equal(new DocumentStats(1, 1, 6, 6), DocumentStats.Of(["foobar"], [Range(0, 3, 0, 6), Range(0, 0, 0, 3)]));
    }

    [Fact]
    public void Of_ranges_counts_a_line_shared_by_two_ranges_once()
    {
        Assert.Equal(new DocumentStats(2, 3, 6, 6), DocumentStats.Of(["ab cd ef", "gh"], [Range(0, 0, 0, 2), Range(0, 6, 1, 2)]));
    }

    [Fact]
    public void Of_ranges_takes_columns_as_graphemes()
    {
        Assert.Equal(new DocumentStats(1, 1, 2, 2), DocumentStats.Of(["e\u0301\U0001F44D\U0001F3FDx"], [Range(0, 1, 0, 3)]));
    }

    private static (Point Start, Point End) Range(int startRow, int startColumn, int endRow, int endColumn) =>
        (new Point(startColumn, startRow), new Point(endColumn, endRow));
}
