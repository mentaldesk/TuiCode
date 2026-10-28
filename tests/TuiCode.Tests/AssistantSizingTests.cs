using TuiCode.Abstractions;

namespace TuiCode.Tests;

// The AI assistance sidebar's width beside the left sidebar and the editor (#619).
public class AssistantSizingTests
{
    [Theory]
    [InlineData(200, 50)]
    [InlineData(120, 48)]
    [InlineData(100, 40)]
    [InlineData(60, 30)]
    public void It_first_opens_at_fifty_columns_or_forty_percent_of_the_terminal_if_less(int terminalWidth, int expected) =>
        Assert.Equal(expected, AssistantSizing.DefaultFor(terminalWidth));

    [Theory]
    [InlineData(10, 30)]
    [InlineData(60, 60)]
    [InlineData(200, 90)]
    public void Clamp_holds_its_floor_and_leaves_the_editor_forty_columns(int width, int expected) =>
        Assert.Equal(expected, AssistantSizing.Clamp(width, 160, 30));

    [Theory]
    [InlineData(160, 30, 50, 30, 50)] // room for both
    [InlineData(110, 30, 50, 30, 40)] // the AI sidebar narrows first
    [InlineData(100, 30, 50, 30, 30)] // down to its floor
    [InlineData(90, 30, 50, 20, 30)]  // then the left one
    [InlineData(60, 30, 50, 15, 30)]  // both at their floors on a terminal too narrow for all three
    public void Fit_keeps_the_editor_forty_columns_narrowing_the_AI_sidebar_before_the_left_one(
        int terminalWidth, int left, int right, int expectedLeft, int expectedRight) =>
        Assert.Equal((expectedLeft, expectedRight), SidebarSizing.Fit(terminalWidth, left, right));

    [Fact]
    public void Fit_gives_a_hidden_sidebar_no_columns_and_the_other_its_room()
    {
        Assert.Equal((0, 80), SidebarSizing.Fit(120, null, 100));
        Assert.Equal((80, 0), SidebarSizing.Fit(120, 100, null));
    }
}
