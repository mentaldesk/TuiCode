namespace TuiCode.Workbench.Configuration;

/// <summary>
/// <c>tuicode --help</c> / <c>-h</c>: prints the usage block and exits without booting the TUI
/// (#264). Recognised in <c>Program.cs</c> ahead of every other parser.
/// </summary>
public static class UsageCli
{
    public static IReadOnlyList<string> Flags { get; } = ["--help", "-h"];

    /// <summary>The usage block, wrapped to fit an 80-column terminal.</summary>
    public const string Text = """
        tuicode — a minimalist terminal code editor

        Usage:
          tuicode [<path>...]        Open a file, or a folder as the workspace.
                                     With no path, the current folder.
                                     Several files open as tabs, the first
                                     active; the first path is the workspace.
                                     A file may carry a position:
                                     path:line[:column]

        Options:
          --help, -h                 Show this help and exit
          --driver <name>            Terminal driver: windows | dotnet | ansi

        Terminal integration (<id> defaults to the terminal you're in):
          --list-terminal-integrations
          --install-terminal-integration[=<id>]
          --uninstall-terminal-integration[=<id>]
          --check-terminal-integration[=<id>]

        Docs: https://github.com/mentaldesk/TuiCode
        """;

    public static int? TryHandle(IReadOnlyList<string> args, TextWriter @out)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(@out);

        if (!args.Any(Flags.Contains)) return null;

        @out.WriteLine(Text);
        return 0;
    }
}
