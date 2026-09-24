using TuiCode.Abstractions;

namespace TuiCode.Tests;

// The sidebar width's clamp (#209).
public class SidebarSizingTests
{
    [Theory]
    [InlineData(120, 80)]
    [InlineData(80, 40)]
    [InlineData(55, 15)]
    public void MaxFor_leaves_the_editor_its_floor(int terminalWidth, int expected)
    {
        Assert.Equal(expected, SidebarSizing.MaxFor(terminalWidth));
    }

    // Below Min + EditorFloor columns there's no width that satisfies both, and the sidebar's floor wins.
    [Theory]
    [InlineData(50, 15)]
    [InlineData(20, 15)]
    [InlineData(0, 15)]
    public void MaxFor_falls_back_to_the_sidebar_floor_on_a_terminal_too_narrow_for_both(int terminalWidth, int expected)
    {
        Assert.Equal(expected, SidebarSizing.MaxFor(terminalWidth));
    }

    [Theory]
    [InlineData(10, 120, 15)]
    [InlineData(30, 120, 30)]
    [InlineData(200, 120, 80)]
    [InlineData(60, 80, 40)]
    [InlineData(30, 50, 15)]
    [InlineData(10, 50, 15)]
    public void Clamp_holds_both_floors(int width, int terminalWidth, int expected)
    {
        Assert.Equal(expected, SidebarSizing.Clamp(width, terminalWidth));
    }

    [Fact]
    public void The_default_sits_between_the_limits()
    {
        Assert.InRange(SidebarSizing.Default, SidebarSizing.Min, SidebarSizing.SettingsMax);
    }
}
