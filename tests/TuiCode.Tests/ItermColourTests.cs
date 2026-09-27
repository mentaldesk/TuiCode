using TuiCode.Workbench.TerminalIntegration;

namespace TuiCode.Tests;

public class ItermColourTests
{
    [Theory]
    [InlineData("#1F2328", 0x1F, 0x23, 0x28)]
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#1f2328", 0x1F, 0x23, 0x28)]
    [InlineData("1F2328", 0x1F, 0x23, 0x28)]
    public void Parse_scales_each_channel_to_0_to_1(string hex, int r, int g, int b) =>
        Assert.Equal(new ItermColour(r / 255.0, g / 255.0, b / 255.0), ItermColour.Parse(hex));

    [Fact]
    public void Parse_gives_Daylight_cursor_as_the_pitch_expects()
    {
        var c = ItermColour.Parse("#1F2328")!.Value;

        Assert.Equal(0.121, c.Red, 0.001);
        Assert.Equal(0.137, c.Green, 0.001);
        Assert.Equal(0.156, c.Blue, 0.001);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#FFF")]
    [InlineData("#1F23289")]
    [InlineData("#GGGGGG")]
    [InlineData("#-12345")]
    [InlineData(" 1F2328")]
    [InlineData("##1F232")]
    public void Parse_rejects_malformed_input(string? hex) => Assert.Null(ItermColour.Parse(hex));
}
