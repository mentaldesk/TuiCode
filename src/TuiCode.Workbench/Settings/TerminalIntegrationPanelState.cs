using TuiCode.Abstractions;

namespace TuiCode.Workbench.Settings;

/// <summary>
/// Pure-data snapshot of what the Terminal Integration settings panel should render. Split out
/// from the TG view so tests can assert on the text + button set without driving a real terminal.
/// </summary>
/// <param name="Detected">First integration whose <see cref="ITerminalIntegration.IsAvailable"/> returned true, or null.</param>
/// <param name="Status">Status of <paramref name="Detected"/>, or null when nothing matched.</param>
/// <param name="Lines">Body text to render in the panel, in top-to-bottom order.</param>
/// <param name="Actions">Buttons to show below the body. Empty when no integration was detected.</param>
internal sealed record TerminalIntegrationPanelState(
    ITerminalIntegration? Detected,
    TerminalIntegrationStatus? Status,
    IReadOnlyList<string> Lines,
    IReadOnlyList<TerminalIntegrationAction> Actions)
{
    public static TerminalIntegrationPanelState Build(
        IEnumerable<ITerminalIntegration> integrations,
        IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(integrations);
        ArgumentNullException.ThrowIfNull(environment);

        var all = integrations.ToArray();
        var detected = all.FirstOrDefault(i => i.IsAvailable());

        var state = detected is not null ? BuildSupported(detected)
            : UnsupportedTerminal.Detect(environment) is { } unsupported ? BuildUnsupported(unsupported)
            : BuildUnknown(all, environment);
        return string.IsNullOrEmpty(environment.GetEnvironmentVariable("TMUX"))
            ? state
            : state with { Lines = [.. state.Lines, "", .. TmuxClipboardLines] };
    }

    private static readonly string[] TmuxClipboardLines =
    [
        "Inside tmux, copying over SSH needs this in ~/.tmux.conf:",
        "  set -g set-clipboard on",
    ];

    private static TerminalIntegrationPanelState BuildSupported(ITerminalIntegration detected)
    {
        var status = detected.GetStatus();
        return new TerminalIntegrationPanelState(
            detected, status, BuildSupportedLines(detected, status), BuildActions(status));
    }

    private static TerminalIntegrationPanelState BuildUnsupported(UnsupportedTerminal terminal)
    {
        var lines = new List<string>
        {
            $"Detected terminal: {terminal.Name}",
            "",
            "Cmd+C and Cmd+X can't reach TuiCode here:",
        };
        lines.AddRange(terminal.Reason);
        lines.AddRange(
        [
            "",
            "Inside TuiCode, use Ctrl+C and Ctrl+X instead.",
            "Cmd+V pastes as usual.",
            "",
            "For Cmd shortcuts, use iTerm2, WezTerm or kitty.",
        ]);
        return new TerminalIntegrationPanelState(null, null, lines, Array.Empty<TerminalIntegrationAction>());
    }

    private static TerminalIntegrationPanelState BuildUnknown(
        IReadOnlyList<ITerminalIntegration> all, IEnvironment env)
    {
        var termProgram = env.GetEnvironmentVariable("TERM_PROGRAM");
        var term = env.GetEnvironmentVariable("TERM");
        var supported = all.Count == 0
            ? "(none built into this version of TuiCode)"
            : string.Join(", ", all.Select(i => i.DisplayName)) + ".";

        var lines = new List<string>
        {
            "No integration available for this terminal yet.",
            "",
            "Detected:",
            $"  TERM_PROGRAM = {Display(termProgram)}",
            $"  TERM         = {Display(term)}",
            "",
            $"Supported so far: {supported}",
            "Track other terminals at:",
            "  https://github.com/mentaldesk/TuiCode/issues/40",
        };
        return new TerminalIntegrationPanelState(null, null, lines, Array.Empty<TerminalIntegrationAction>());
    }

    private static IReadOnlyList<string> BuildSupportedLines(
        ITerminalIntegration detected, TerminalIntegrationStatus status)
    {
        var lines = new List<string>
        {
            $"Detected terminal: {detected.DisplayName}",
            "",
            "Installs terminal config that maps macOS shortcuts",
            "(Cmd+C/V/X/Z/A, Cmd+arrows, …) onto the key sequences",
            "TuiCode understands.",
            "",
        };
        lines.Add(status switch
        {
            TerminalIntegrationStatus.NotInstalled => "Status: Not installed",
            TerminalIntegrationStatus.Installed => "Status: Installed",
            TerminalIntegrationStatus.Stale =>
                "Status: Installed (older version) — update to pick up the latest shortcuts.",
            _ => $"Status: {status}",
        });

        if (status != TerminalIntegrationStatus.NotInstalled &&
            detected.PostInstallInstructions is { } notes)
            AddNotes(lines, notes);
        if (detected.ClipboardInstructions is { } clipboard)
            AddNotes(lines, clipboard);

        return lines;
    }

    private static void AddNotes(List<string> lines, string notes)
    {
        lines.Add("");
        foreach (var line in notes.Split('\n'))
            lines.Add(line.TrimEnd('\r'));
    }

    private static IReadOnlyList<TerminalIntegrationAction> BuildActions(TerminalIntegrationStatus status) =>
        status switch
        {
            TerminalIntegrationStatus.NotInstalled => new[] { TerminalIntegrationAction.Install },
            TerminalIntegrationStatus.Installed => new[]
            {
                TerminalIntegrationAction.Reinstall,
                TerminalIntegrationAction.Remove,
            },
            TerminalIntegrationStatus.Stale => new[]
            {
                TerminalIntegrationAction.Update,
                TerminalIntegrationAction.Remove,
            },
            _ => Array.Empty<TerminalIntegrationAction>(),
        };

    private static string Display(string? value) =>
        string.IsNullOrEmpty(value) ? "(unset)" : value;
}

internal enum TerminalIntegrationAction
{
    Install,
    Reinstall,
    Update,
    Remove,
}

internal static class TerminalIntegrationActionExtensions
{
    public static string Label(this TerminalIntegrationAction action) => action switch
    {
        TerminalIntegrationAction.Install => "Install",
        TerminalIntegrationAction.Reinstall => "Reinstall",
        TerminalIntegrationAction.Update => "Update",
        TerminalIntegrationAction.Remove => "Remove",
        _ => action.ToString(),
    };
}
