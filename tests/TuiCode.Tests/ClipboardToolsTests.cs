using System.Text;
using TuiCode.Editor;

namespace TuiCode.Tests;

// #319
public class ClipboardToolsTests
{
    [Theory]
    [InlineData(true, false, false, "pbcopy")]
    [InlineData(false, true, false, "clip.exe")]
    [InlineData(false, false, true, "wl-copy xclip")]
    [InlineData(false, false, false, "xclip")]
    public void For_picks_the_platform_s_clipboard_program(bool macOS, bool windows, bool wayland, string expected)
    {
        var tools = ClipboardTools.For(macOS, windows, wayland, new ClipboardProgram());

        Assert.Equal(expected, string.Join(' ', tools.Candidates.Select(tool => tool.Name)));
    }

    [Fact]
    public void ThisMachine_has_its_clipboard_programs()
    {
        Assert.NotEmpty(ClipboardTools.ThisMachine.Candidates);
        Assert.All(ClipboardTools.ThisMachine.Candidates, Assert.NotNull);
    }

    [Fact]
    public void Write_copies_through_the_program_and_reads_the_text_back()
    {
        var program = new ClipboardProgram();

        var failed = ClipboardTools.For(true, false, false, program).Write("one\ntwo");

        Assert.Null(failed);
        Assert.Equal("one\ntwo", program.Stored);
        Assert.Equal(["pbcopy", "pbpaste"], program.Ran);
    }

    [Fact]
    public void Write_fails_when_the_program_reads_back_something_else()
    {
        var program = new ClipboardProgram { Answer = command => command[0] == "pbpaste" ? new ToolRun.Exited(0, "before", "") : null };

        var failed = ClipboardTools.For(true, false, false, program).Write("a");

        Assert.Equal(new CopyOutcome.Failed("the clipboard didn't take the text (pbcopy)"), failed);
    }

    [Fact]
    public void Write_under_Wayland_uses_xclip_when_wl_copy_is_missing()
    {
        var program = new ClipboardProgram { Answer = command => command[0] == "wl-copy" ? new ToolRun.NotFound() : null };

        var failed = ClipboardTools.For(false, false, true, program).Write("a");

        Assert.Null(failed);
        Assert.Equal(["wl-copy", "xclip -selection clipboard", "xclip -selection clipboard -o"], program.Ran);
    }

    [Fact]
    public void Write_says_which_program_to_install_when_none_is_there()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.NotFound() };

        var failed = ClipboardTools.For(false, false, true, program).Write("a");

        Assert.Equal(new CopyOutcome.Failed("no clipboard tool found (install wl-copy or xclip)"), failed);
    }

    [Fact]
    public void Write_fails_with_the_first_line_of_what_the_program_said()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.Exited(1, "", "\nError: Can't open display: (null)\nmore\n") };

        var failed = ClipboardTools.For(false, false, false, program).Write("a");

        Assert.Equal(new CopyOutcome.Failed("Error: Can't open display: (null) (xclip)"), failed);
    }

    [Fact]
    public void Write_gives_the_exit_code_when_the_program_says_nothing()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.Exited(3, "", "") };

        var failed = ClipboardTools.For(false, false, false, program).Write("a");

        Assert.Equal(new CopyOutcome.Failed("exited with code 3 (xclip)"), failed);
    }

    [Fact]
    public void Write_gives_up_on_a_program_that_does_not_answer()
    {
        var program = new ClipboardProgram { Answer = _ => new ToolRun.TimedOut() };

        var failed = ClipboardTools.For(true, false, false, program).Write("a");

        Assert.Equal(new CopyOutcome.Failed("no answer after 5 seconds (pbcopy)"), failed);
        Assert.Equal(ClipboardTools.Timeout, program.Timeout);
    }

    [Fact]
    public void Write_hands_clip_exe_UTF_16_with_a_byte_order_mark()
    {
        var program = new ClipboardProgram();

        ClipboardTools.For(false, true, false, program).Write("é");

        Assert.Equal([0xFF, 0xFE, 0xE9, 0x00], program.Input);
    }

    // #347
    [Fact]
    public void Write_runs_pbcopy_and_pbpaste_in_a_UTF_8_locale()
    {
        var program = new ClipboardProgram();

        ClipboardTools.For(true, false, false, program).Write("a — b");

        Assert.Equal(["LC_ALL=en_US.UTF-8", "LC_ALL=en_US.UTF-8"], program.Environments);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Write_leaves_the_locale_of_byte_transparent_programs_alone(bool wayland, bool windows)
    {
        var program = new ClipboardProgram();

        ClipboardTools.For(false, windows, wayland, program).Write("a — b");

        Assert.All(program.Environments, environment => Assert.Equal("", environment));
    }

    [Fact]
    public void The_programs_keep_the_rest_of_TuiCode_s_environment()
    {
        var inherited = new Dictionary<string, string?> { ["LC_ALL"] = "C", ["DISPLAY"] = ":0", ["WAYLAND_DISPLAY"] = "wayland-0" };

        ProcessRunner.Override(inherited, new Dictionary<string, string> { ["LC_ALL"] = "en_US.UTF-8" });

        Assert.Equal(new Dictionary<string, string?> { ["LC_ALL"] = "en_US.UTF-8", ["DISPLAY"] = ":0", ["WAYLAND_DISPLAY"] = "wayland-0" }, inherited);
    }
}

/// <summary>Stands in for every clipboard program: a copy stores its input as UTF-8, a paste returns it; <see cref="Answer"/> overrides either.</summary>
internal sealed class ClipboardProgram : IProcessRunner
{
    public string Stored { get; private set; } = string.Empty;
    public byte[] Input { get; private set; } = [];
    public TimeSpan Timeout { get; private set; }
    public List<string> Ran { get; } = [];
    public List<string> Environments { get; } = [];
    public Func<string[], ToolRun?> Answer { get; init; } = _ => null;

    public ToolRun Run(string[] command, IReadOnlyDictionary<string, string> environment, byte[] input, bool readOutput, TimeSpan timeout)
    {
        Ran.Add(string.Join(' ', command));
        Environments.Add(string.Join(' ', environment.Select(variable => $"{variable.Key}={variable.Value}")));
        Timeout = timeout;
        if (Answer(command) is { } answer) return answer;
        if (readOutput) return new ToolRun.Exited(0, Stored, "");
        Input = input;
        Stored = Encoding.UTF8.GetString(input);
        return new ToolRun.Exited(0, "", "");
    }
}
