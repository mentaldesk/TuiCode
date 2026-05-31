namespace TuiCode.Abstractions;

/// <summary>
/// Whether an integration is currently installed in the user's terminal config, and whether
/// it matches the version TuiCode would write today.
/// </summary>
public enum TerminalIntegrationStatus
{
    /// <summary>No integration file/config present.</summary>
    NotInstalled,

    /// <summary>Integration is present and matches what TuiCode would write now.</summary>
    Installed,

    /// <summary>Integration is present but written by an older TuiCode — reinstall to refresh.</summary>
    Stale,
}

/// <summary>
/// One terminal emulator's integration (iTerm2, WezTerm, Ghostty, …). Reports whether the
/// host's current terminal matches, what state the integration is in, and installs/uninstalls
/// the per-terminal config (dynamic profile JSON, keybind block, etc.).
/// </summary>
/// <remarks>
/// Implementations write through <see cref="System.IO.Abstractions.IFileSystem"/> and read
/// environment via <see cref="IEnvironment"/> so they're testable with <c>MockFileSystem</c>
/// and a fake env, with no real I/O.
/// </remarks>
public interface ITerminalIntegration
{
    /// <summary>Stable identifier used by CLI flags and persisted state (e.g. <c>"iterm2"</c>).</summary>
    string Id { get; }

    /// <summary>Human-readable name for the Settings UI (e.g. <c>"iTerm2"</c>).</summary>
    string DisplayName { get; }

    /// <summary>True when the current process is running inside this terminal emulator.</summary>
    bool IsAvailable();

    /// <summary>Inspect the user's config to determine install state.</summary>
    TerminalIntegrationStatus GetStatus();

    /// <summary>
    /// Write or overwrite the integration in place. Idempotent — re-running on an already-installed
    /// integration refreshes it without duplicating.
    /// </summary>
    void Install();

    /// <summary>Remove the integration from the user's terminal config. No-op if not installed.</summary>
    void Uninstall();
}
