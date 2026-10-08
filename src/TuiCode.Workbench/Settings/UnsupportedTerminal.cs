using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>A macOS terminal TuiCode recognises but can't integrate with, and why.</summary>
internal sealed record UnsupportedTerminal(string Name, IReadOnlyList<string> Reason)
{
    public static readonly UnsupportedTerminal Alacritty = new("Alacritty",
    [
        "Alacritty's key bindings apply to every program,",
        "so passing them on would break copy in the shell.",
    ]);

    public static readonly UnsupportedTerminal Ghostty = new("Ghostty",
    [
        "Ghostty has no key bindings for one program only,",
        "so passing them on would break copy in the shell.",
    ]);

    public static readonly UnsupportedTerminal TerminalApp = new("Terminal.app",
    [
        "Terminal.app doesn't pass on the modifiers",
        "TuiCode needs.",
    ]);

    public static UnsupportedTerminal? Detect(IEnvironment env)
    {
        if (!env.IsMacOS)
            return null;

        var termProgram = env.GetEnvironmentVariable("TERM_PROGRAM");
        bool Is(string program, string marker) =>
            string.Equals(termProgram, program, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(env.GetEnvironmentVariable(marker));

        if (env.GetEnvironmentVariable("TERM") == "alacritty" || Is("alacritty", "ALACRITTY_WINDOW_ID"))
            return Alacritty;
        if (env.GetEnvironmentVariable("TERM") == "xterm-ghostty" || Is("ghostty", "GHOSTTY_RESOURCES_DIR"))
            return Ghostty;
        if (termProgram == "Apple_Terminal")
            return TerminalApp;
        return null;
    }
}
