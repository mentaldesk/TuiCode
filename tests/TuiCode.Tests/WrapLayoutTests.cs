using System.Globalization;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class WrapLayoutTests
{
    [Fact]
    public void RowStarts_breaks_after_the_last_space_that_fits() =>
        Assert.Equal([0, 10], WrapLayout.RowStarts(G("the quick brown"), 10, 4));

    [Fact]
    public void RowStarts_breaks_mid_word_when_a_word_is_wider_than_the_width() =>
        Assert.Equal([0, 4, 8], WrapLayout.RowStarts(G("abcdefghij"), 4, 4));

    [Fact]
    public void RowStarts_keeps_a_line_exactly_the_width_on_one_row() =>
        Assert.Equal([0], WrapLayout.RowStarts(G("abcd"), 4, 4));

    [Fact]
    public void RowStarts_gives_an_empty_line_one_row() =>
        Assert.Equal([0], WrapLayout.RowStarts([], 4, 4));

    [Fact]
    public void RowStarts_lets_the_space_at_a_break_run_past_the_edge() =>
        Assert.Equal([0, 5], WrapLayout.RowStarts(G("abcd efg"), 4, 4));

    [Fact]
    public void RowStarts_counts_wide_characters_in_cells() =>
        Assert.Equal([0, 2], WrapLayout.RowStarts(G("a中文b"), 3, 4));

    [Theory]
    [InlineData("\tab", 5, new[] { 0, 1 })]
    [InlineData("abcd\tx", 6, new[] { 0, 4 })]
    public void RowStarts_measures_a_tab_to_the_next_stop_from_the_start_of_its_row(string line, int width, int[] starts) =>
        Assert.Equal(starts, WrapLayout.RowStarts(G(line), width, 4));

    [Fact]
    public void RowStarts_carries_a_word_to_the_next_row_with_the_tab_stops_measured_there()
    {
        // "x\tyy" carried to a new row: the tab now reaches column 4 rather than 8.
        Assert.Equal([0, 6], WrapLayout.RowStarts(G("abcde x\tyy"), 7, 4));
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(15, 1)]
    public void RowOf_puts_a_row_start_on_its_own_row(int column, int row) =>
        Assert.Equal(row, WrapLayout.RowOf([0, 10], column));

    [Theory]
    [InlineData(9, 9)]
    [InlineData(10, 0)]
    [InlineData(15, 5)]
    public void X_counts_cells_from_the_start_of_the_row(int column, int x) =>
        Assert.Equal(x, WrapLayout.X(G("the quick brown"), [0, 10], column, 4));

    [Theory]
    [InlineData(0, 3, 3)]
    [InlineData(1, 2, 12)]
    [InlineData(0, 50, 9)]
    [InlineData(1, 50, 15)]
    public void ColumnAt_finds_the_column_under_a_cell_and_stops_at_the_end_of_the_row(int row, int x, int column) =>
        Assert.Equal(column, WrapLayout.ColumnAt(G("the quick brown"), [0, 10], row, x, 4));

    [Fact]
    public void ColumnAt_lands_on_a_wide_character_from_either_of_its_cells()
    {
        Assert.Equal(1, WrapLayout.ColumnAt(G("a中文b"), [0, 2], 0, 1, 4));
        Assert.Equal(1, WrapLayout.ColumnAt(G("a中文b"), [0, 2], 0, 2, 4));
    }

    [Fact]
    public void Map_finds_each_screen_row_and_where_each_line_starts()
    {
        var map = new WrapMap();
        string[] lines = ["the quick brown", "", "abcdefghijk"];

        Assert.True(map.Update(lines, i => G(lines[i]), 10, 4));

        Assert.Equal(5, map.Rows);
        Assert.Equal([0, 2, 3, 5], Enumerable.Range(0, 4).Select(map.FirstRow));
        Assert.Equal([(0, 0), (0, 1), (1, 0), (2, 0), (2, 1)], Enumerable.Range(0, 5).Select(map.At));
    }

    [Fact]
    public void Map_measures_again_only_the_lines_whose_text_changed()
    {
        var map = new WrapMap();
        string[] lines = ["the quick brown", "abcdefghij"];
        map.Update(lines, i => G(lines[i]), 10, 4);
        var measured = new List<int>();

        string[] edited = [lines[0], "abc"];
        Assert.True(map.Update(edited, i => { measured.Add(i); return G(edited[i]); }, 10, 4));
        Assert.False(map.Update(edited, i => { measured.Add(i); return G(edited[i]); }, 10, 4));

        Assert.Equal([1], measured);
        Assert.Equal(3, map.Rows);
    }

    private static string[] G(string text)
    {
        var graphemes = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) graphemes.Add(e.GetTextElement());
        return [.. graphemes];
    }
}
