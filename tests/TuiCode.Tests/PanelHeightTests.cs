using TuiCode.Workbench.Parts;

namespace TuiCode.Tests;

public class PanelHeightTests
{
    [Theory]
    [InlineData(28, false, 12)]
    [InlineData(60, false, 23)]
    [InlineData(20, false, 11)]
    [InlineData(14, false, 11)]
    [InlineData(28, true, 14)]
    [InlineData(60, true, 30)]
    [InlineData(20, true, 11)]
    public void RowsFor_is_a_third_or_half_for_a_chat_plus_the_tab_strip_leaving_the_editor_ten_rows(int available, bool chat, int expected) =>
        Assert.Equal(expected, PanelPart.RowsFor(available, chat));
}
