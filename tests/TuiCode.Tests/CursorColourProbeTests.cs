using TuiCode.Workbench.Diagnostics;

namespace TuiCode.Tests;

public class CursorColourProbeTests
{
    [Theory]
    [InlineData("\x1b]12;rgb:1f1f/2323/2828\x07")]
    [InlineData("\x1b]12;rgb:1F1F/2323/2828\x1b\\")]
    [InlineData("\x1b]12;rgb:1F/23/28\x07")]
    [InlineData("\x1b]12;#1f2328\x07")]
    public void ParseReply_reads_the_colour_as_8_bit_hex(string reply) =>
        Assert.Equal("#1F2328", CursorColourProbe.ParseReply(reply));

    [Fact]
    public void ParseReply_rounds_16_bit_channels_to_the_nearest_8_bit_value() =>
        Assert.Equal("#FF0080", CursorColourProbe.ParseReply("\x1b]12;rgb:ffff/0000/8080\x07"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\x1b]11;rgb:1f1f/2323/2828\x07")]
    [InlineData("\x1b]12;rgb:zz/23/28\x07")]
    [InlineData("garbage")]
    public void ParseReply_returns_null_for_anything_but_a_cursor_colour(string? reply) =>
        Assert.Null(CursorColourProbe.ParseReply(reply));

    [Fact]
    public void Matches_accepts_iTerm2s_colour_space_rounding() =>
        Assert.True(CursorColourProbe.Matches("#1F2328", CursorColourProbe.ParseReply("\x1b]12;rgb:1fde/2302/27ad\x07")!));

    [Theory]
    [InlineData("#1F2328")]
    [InlineData("#1f2328")]
    [InlineData("#212328")]
    [InlineData("#1D2126")]
    public void Matches_accepts_colours_within_two_steps_on_every_channel(string reported) =>
        Assert.True(CursorColourProbe.Matches("#1F2328", reported));

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#222328")]
    [InlineData("#1F2025")]
    [InlineData("#1F232B")]
    public void Matches_rejects_colours_more_than_two_steps_off_on_any_channel(string reported) =>
        Assert.False(CursorColourProbe.Matches("#1F2328", reported));
}
