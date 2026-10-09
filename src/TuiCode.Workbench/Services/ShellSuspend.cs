using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Workbench.Services;

/// <summary>Stops TuiCode and hands the terminal back to the shell it was started from until <c>fg</c>, as Ctrl+Z does in vim (#463).</summary>
public sealed class ShellSuspend(IEnvironment environment, Func<bool> hasJobControl, Action<string> write, Action stop)
{
    public const string NotOnWindows = "⚠ Suspend isn't available on Windows. Use a new terminal tab.";
    public const string NoJobControl = "⚠ Can't suspend: TuiCode wasn't started from a shell with job control.";

    /// <summary>Returns once the shell has resumed TuiCode, or with the reason it can't suspend.</summary>
    public string? Run(TerminalModes modes)
    {
        if (environment.IsWindows) return NotOnWindows;
        if (!hasJobControl()) return NoJobControl;
        write(modes.Off);
        stop();
        write(modes.On);
        return null;
    }
}

/// <summary>What TuiCode turns on in the terminal that Terminal.Gui's own suspend leaves on.</summary>
public sealed record TerminalModes(
    KittyKeyboardFlags KittyKeyboard,
    bool MultipleCursors,
    CursorStyle CursorStyle,
    string? CursorColour)
{
    internal const string Active = "\e]1337;SetUserVar=TUICODE_ACTIVE=MQ==\a";
    internal const string Inactive = "\e]1337;SetUserVar=TUICODE_ACTIVE=MA==\a";
    internal const string DefaultCursorColour = "\e]112\a";

    public string Off =>
        (KittyKeyboard == KittyKeyboardFlags.None ? "" : EscSeqUtils.CSI_DisableKittyKeyboardFlags)
        + (MultipleCursors ? TerminalCursors.ClearAll : "")
        + EscSeqUtils.CSI_SetCursorStyle(CursorStyle.Default)
        + Inactive
        + DefaultCursorColour;

    public string On =>
        (KittyKeyboard == KittyKeyboardFlags.None ? "" : EscSeqUtils.CSI_EnableKittyKeyboardFlags(KittyKeyboard))
        + (CursorStyle is CursorStyle.Default or CursorStyle.Hidden ? "" : EscSeqUtils.CSI_SetCursorStyle(CursorStyle))
        + Active
        + (CursorColour is null ? "" : $"\e]12;{CursorColour}\a");
}
