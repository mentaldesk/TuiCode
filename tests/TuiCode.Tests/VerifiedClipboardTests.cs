using Terminal.Gui.Drivers;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class VerifiedClipboardTests
{
    [Theory]
    [InlineData("one", 1, 3)]
    [InlineData("one\ntwo", 2, 6)]
    [InlineData("one\r\ntwo", 2, 6)]
    [InlineData("one\ntwo\n", 2, 6)]
    [InlineData("", 1, 0)]
    public void Write_counts_the_lines_and_characters_that_reached_the_clipboard(string text, int lines, int characters)
    {
        var clipboard = new TestClipboard();

        var outcome = VerifiedClipboard.Write(clipboard, text);

        Assert.Equal(new CopyOutcome.Copied(lines, characters), outcome);
        Assert.Equal(text, clipboard.Text);
    }

    [Fact]
    public void Write_accepts_a_clipboard_that_changes_the_line_endings()
    {
        var clipboard = new TestClipboard { Transform = text => text.ReplaceLineEndings("\r\n") };

        Assert.Equal(new CopyOutcome.Copied(2, 2), VerifiedClipboard.Write(clipboard, "a\nb"));
    }

    [Fact]
    public void Write_fails_when_there_is_no_clipboard() =>
        Assert.Equal(new CopyOutcome.Failed("there is no clipboard"), VerifiedClipboard.Write(null, "a"));

    [Fact]
    public void Write_fails_with_the_first_line_of_the_reason_when_the_clipboard_throws()
    {
        var clipboard = new TestClipboard { Throw = new InvalidOperationException("pasteboard refused\n   at Somewhere()") };

        var outcome = VerifiedClipboard.Write(clipboard, "a");

        Assert.Equal(new CopyOutcome.Failed("pasteboard refused (TestClipboard)"), outcome);
    }

    [Fact]
    public void Write_fails_when_the_clipboard_silently_keeps_what_it_had()
    {
        var clipboard = new TestClipboard { Text = "before", Transform = _ => null };

        var outcome = VerifiedClipboard.Write(clipboard, "a");

        Assert.Equal(new CopyOutcome.Failed("the clipboard didn't take the text (TestClipboard)"), outcome);
    }

    [Fact]
    public void Write_fails_when_the_clipboard_reads_back_something_else()
    {
        var clipboard = new TestClipboard { Transform = text => text[..^1] };

        Assert.IsType<CopyOutcome.Failed>(VerifiedClipboard.Write(clipboard, "abc"));
    }
}

/// <summary>Stores what it's given, after <see cref="Transform"/>; null from it drops the write.</summary>
internal sealed class TestClipboard : IClipboard
{
    public string Text { get; set; } = string.Empty;
    public Func<string, string?> Transform { get; init; } = text => text;
    public Exception? Throw { get; init; }

    public bool IsSupported => false;

    public string GetClipboardData() => Text;

    public void SetClipboardData(string text)
    {
        if (Throw is not null) throw Throw;
        if (Transform(text) is { } stored) Text = stored;
    }

    public bool TryGetClipboardData(out string result)
    {
        result = Text;
        return true;
    }

    public bool TrySetClipboardData(string text)
    {
        SetClipboardData(text);
        return true;
    }
}
