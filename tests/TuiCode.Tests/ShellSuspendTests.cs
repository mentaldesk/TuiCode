using Terminal.Gui.Drivers;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class ShellSuspendTests
{
    private readonly List<string> _events = [];

    private ShellSuspend Build(bool windows = false, bool jobControl = true) =>
        new(new FakeEnvironment().SetIsWindows(windows), () => jobControl, _events.Add, () => _events.Add("stop"));

    private static readonly TerminalModes Everything =
        new(KittyKeyboardFlags.DisambiguateEscapeCodes | KittyKeyboardFlags.ReportAllKeysAsEscapeCodes, true, CursorStyle.SteadyBar, "#FFCC00");

    [Fact]
    public void Run_puts_the_terminal_back_before_stopping_and_sets_it_up_again_after_fg()
    {
        Assert.Null(Build().Run(Everything));

        Assert.Equal(
        [
            "\e[<u" + "\e[>0;4 q" + "\e[0 q" + "\e]1337;SetUserVar=TUICODE_ACTIVE=MA==\a" + "\e]112\a",
            "stop",
            "\e[>9u" + "\e[6 q" + "\e]1337;SetUserVar=TUICODE_ACTIVE=MQ==\a" + "\e]12;#FFCC00\a",
        ], _events);
    }

    [Fact]
    public void Run_leaves_alone_the_modes_this_terminal_never_turned_on()
    {
        Build().Run(new TerminalModes(KittyKeyboardFlags.None, false, CursorStyle.Default, null));

        Assert.DoesNotContain("\e[<u", _events[0]);
        Assert.DoesNotContain("\e[>0;4 q", _events[0]);
        Assert.Equal("\e]1337;SetUserVar=TUICODE_ACTIVE=MQ==\a", _events[2]);
    }

    [Fact]
    public void Run_refuses_on_Windows_without_touching_the_terminal()
    {
        Assert.Equal("⚠ Suspend isn't available on Windows. Use a new terminal tab.", Build(windows: true).Run(Everything));
        Assert.Empty(_events);
    }

    [Fact]
    public void Run_refuses_without_a_job_control_shell_to_return_to()
    {
        Assert.Equal("⚠ Can't suspend: TuiCode wasn't started from a shell with job control.",
            Build(jobControl: false).Run(Everything));
        Assert.Empty(_events);
    }
}
