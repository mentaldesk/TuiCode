using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class PanelHeightTests
{
    [Theory]
    [InlineData(28, 12)]
    [InlineData(60, 23)]
    [InlineData(20, 11)]
    [InlineData(14, 11)]
    public void RowsFor_is_a_third_plus_the_tab_strip_leaving_the_editor_ten_rows(int available, int expected) =>
        Assert.Equal(expected, PanelPart.RowsFor(available));
}
