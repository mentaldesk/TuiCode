using TuiCode.Editor;

namespace TuiCode.Tests;

public class RevealTests
{
    [Fact]
    public void A_jump_forwards_puts_the_ranges_first_line_two_rows_below_the_top()
    {
        Assert.Equal(48, Reveal.TopRow(top: 0, height: 20, lineCount: 100, first: 50, last: 53));
    }

    [Fact]
    public void A_jump_backwards_puts_the_ranges_first_line_two_rows_below_the_top()
    {
        Assert.Equal(8, Reveal.TopRow(top: 80, height: 20, lineCount: 100, first: 10, last: 11));
    }

    [Fact]
    public void A_range_already_in_view_with_room_at_both_ends_does_not_scroll()
    {
        Assert.Null(Reveal.TopRow(top: 40, height: 20, lineCount: 100, first: 42, last: 57));
    }

    [Theory]
    [InlineData(41, 57)] // One row above the range's first line.
    [InlineData(40, 58)] // One row below its last.
    public void A_range_without_the_full_margin_at_either_end_scrolls(int first, int last)
    {
        Assert.Equal(first - 2, Reveal.TopRow(top: 40, height: 20, lineCount: 100, first, last));
    }

    [Fact]
    public void A_range_taller_than_the_viewport_top_aligns_and_shows_what_it_can()
    {
        Assert.Equal(48, Reveal.TopRow(top: 0, height: 10, lineCount: 100, first: 50, last: 80));
    }

    [Fact]
    public void A_target_in_the_last_page_scrolls_no_further_than_the_end()
    {
        Assert.Equal(80, Reveal.TopRow(top: 0, height: 20, lineCount: 100, first: 99, last: 99));
    }

    [Fact]
    public void A_file_that_fits_the_viewport_whole_never_scrolls()
    {
        Assert.Null(Reveal.TopRow(top: 0, height: 20, lineCount: 5, first: 4, last: 4));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_viewport_too_short_for_the_margin_still_leaves_the_first_line_in_view(int height)
    {
        var top = Reveal.TopRow(top: 0, height, lineCount: 100, first: 50, last: 53);

        Assert.NotNull(top);
        Assert.InRange(50, top.Value, top.Value + height - 1);
    }

    [Fact]
    public void A_viewport_that_has_not_been_laid_out_skips_the_reveal()
    {
        Assert.Null(Reveal.TopRow(top: 0, height: 0, lineCount: 100, first: 50, last: 53));
    }

    [Fact]
    public void The_first_line_of_the_file_does_not_scroll_above_it()
    {
        Assert.Equal(0, Reveal.TopRow(top: 20, height: 10, lineCount: 100, first: 0, last: 0));
    }
}
