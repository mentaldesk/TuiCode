using System.Text;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class TerminalClipboardTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("one\ntwo\r\nthree\n")]
    [InlineData("café — 日本語 🙂")]
    public void Sequence_is_OSC_52_with_the_base64_of_the_UTF8_text(string text)
    {
        var sequence = TerminalClipboard.Sequence(text)!;

        Assert.StartsWith("\e]52;c;", sequence);
        Assert.EndsWith("\a", sequence);
        Assert.Equal(text, Encoding.UTF8.GetString(Convert.FromBase64String(sequence[7..^1])));
    }

    [Fact]
    public void Sequence_for_hello_is_the_known_one() =>
        Assert.Equal("\e]52;c;aGVsbG8=\a", TerminalClipboard.Sequence("hello"));

    [Fact]
    public void Send_takes_text_up_to_the_limit_in_bytes()
    {
        var sent = new List<string>();
        var clipboard = new TerminalClipboard(null, sent.Add);

        Assert.True(clipboard.Send(new string('x', TerminalClipboard.MaxBytes)));
        Assert.False(clipboard.Send(new string('é', TerminalClipboard.MaxBytes / 2 + 1)));
        Assert.Single(sent);
    }

    [Fact]
    public void Setting_the_clipboard_sends_the_text_and_stores_it_on_the_platform_clipboard()
    {
        var sent = new List<string>();
        var platform = new TestClipboard();
        var clipboard = new TerminalClipboard(platform, sent.Add);

        clipboard.SetClipboardData("a");

        Assert.Equal("a", platform.Text);
        Assert.Equal("a", clipboard.GetClipboardData());
        Assert.Equal([TerminalClipboard.Sequence("a")], sent);
    }

    [Fact]
    public void Setting_the_clipboard_sends_the_text_even_when_the_platform_clipboard_throws()
    {
        var sent = new List<string>();
        var clipboard = new TerminalClipboard(new TestClipboard { Throw = new NotSupportedException() }, sent.Add);

        Assert.Throws<NotSupportedException>(() => clipboard.SetClipboardData("a"));
        Assert.Single(sent);
    }
}
