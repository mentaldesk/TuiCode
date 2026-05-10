using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class GoToLineParseTests
{
    [Theory]
    [InlineData("12", 12, 1)]
    [InlineData("3:7", 3, 7)]
    [InlineData("1:1", 1, 1)]
    public void TryParse_accepts_well_formed_input(string raw, int expectedLine, int expectedCol)
    {
        Assert.True(GoToLineView.TryParse(raw, out var line, out var col));
        Assert.Equal(expectedLine, line);
        Assert.Equal(expectedCol, col);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("5:")]
    [InlineData("5:abc")]
    [InlineData("5:0")]
    public void TryParse_rejects_invalid_input(string raw)
    {
        Assert.False(GoToLineView.TryParse(raw, out _, out _));
    }
}
