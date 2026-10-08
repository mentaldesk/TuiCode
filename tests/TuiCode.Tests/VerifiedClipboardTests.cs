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

    [Fact]
    public void Write_leaves_the_platform_program_alone_when_the_clipboard_took_the_text()
    {
        var program = new ClipboardProgram();

        VerifiedClipboard.Write(new TestClipboard(), "a", ClipboardTools.For(true, false, false, program));

        Assert.Empty(program.Ran);
    }

    [Fact]
    public void Write_copies_through_the_platform_program_when_the_clipboard_did_not_take_the_text()
    {
        var program = new ClipboardProgram();

        var outcome = VerifiedClipboard.Write(new TestClipboard { Transform = _ => null }, "one\ntwo",
            ClipboardTools.For(true, false, false, program));

        Assert.Equal(new CopyOutcome.Copied(2, 6), outcome);
        Assert.Equal("one\ntwo", program.Stored);
    }

    [Fact]
    public void Write_says_why_the_platform_program_failed_too()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.Exited(1, "", "no pasteboard") };

        var outcome = VerifiedClipboard.Write(new TestClipboard { Transform = _ => null }, "a",
            ClipboardTools.For(true, false, false, program));

        Assert.Equal(new CopyOutcome.Failed("no pasteboard (pbcopy)"), outcome);
    }

    [Fact]
    public void Write_sends_the_text_through_the_terminal_as_well_and_says_nothing_of_it_when_the_clipboard_took_it()
    {
        var sent = new List<string>();

        var outcome = VerifiedClipboard.Write(new TerminalClipboard(new TestClipboard(), sent.Add), "one\ntwo");

        Assert.Equal(new CopyOutcome.Copied(2, 6), outcome);
        Assert.Equal([TerminalClipboard.Sequence("one\ntwo")], sent);
    }

    [Fact]
    public void Write_says_the_text_went_through_the_terminal_when_the_clipboard_did_not_take_it()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.NotFound() };
        var terminal = new TerminalClipboard(new TestClipboard { Transform = _ => null }, _ => { });

        var outcome = VerifiedClipboard.Write(terminal, "a", ClipboardTools.For(false, false, false, program));

        Assert.Equal(new CopyOutcome.Copied(1, 1, ThroughTerminal: true), outcome);
    }

    [Fact]
    public void Write_fails_when_neither_the_clipboard_nor_a_terminal_took_the_text()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.NotFound() };

        var outcome = VerifiedClipboard.Write(new TestClipboard { Transform = _ => null }, "a",
            ClipboardTools.For(false, false, false, program));

        Assert.Equal(new CopyOutcome.Failed("no clipboard tool found (install wl-copy or xclip)"), outcome);
    }

    [Fact]
    public void Write_treats_Terminal_Guis_in_process_clipboard_as_no_clipboard_when_a_terminal_is_there()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.NotFound() };
        var inProcess = new FakeClipboard();

        var outcome = VerifiedClipboard.Write(new TerminalClipboard(inProcess, _ => { }), "a",
            ClipboardTools.For(false, false, false, program));

        Assert.Equal(new CopyOutcome.Copied(1, 1, ThroughTerminal: true), outcome);
        Assert.Equal("a", inProcess.GetClipboardData());
    }

    [Fact]
    public void Write_copies_through_the_platform_program_when_Terminal_Guis_clipboard_is_in_process()
    {
        var program = new ClipboardProgram();

        var outcome = VerifiedClipboard.Write(new TerminalClipboard(new FakeClipboard(), _ => { }), "a",
            ClipboardTools.For(false, false, false, program));

        Assert.Equal(new CopyOutcome.Copied(1, 1), outcome);
        Assert.Equal("a", program.Stored);
    }

    [Fact]
    public void Write_says_a_copy_was_too_large_for_the_terminal_when_nothing_else_took_it()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.NotFound() };
        var sent = new List<string>();
        var terminal = new TerminalClipboard(new TestClipboard { Transform = _ => null }, sent.Add);

        var outcome = VerifiedClipboard.Write(terminal, new string('x', TerminalClipboard.MaxBytes + 1),
            ClipboardTools.For(false, false, false, program));

        Assert.Equal(new CopyOutcome.Failed(TerminalClipboard.TooLarge), outcome);
        Assert.Empty(sent);
    }

    [Fact]
    public void Write_says_nothing_of_the_terminal_limit_when_the_clipboard_took_a_large_copy()
    {
        var text = new string('x', TerminalClipboard.MaxBytes + 1);

        var outcome = VerifiedClipboard.Write(new TerminalClipboard(new TestClipboard(), _ => { }), text);

        Assert.Equal(new CopyOutcome.Copied(1, text.Length), outcome);
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
