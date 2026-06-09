namespace TuiCode.Abstractions;

/// <summary>
/// Wraps process environment + well-known folder lookups so terminal-integration code can be
/// unit-tested without poking at the real shell. Implementations delegate to
/// <see cref="System.Environment"/>; tests substitute a fake.
/// </summary>
public interface IEnvironment
{
    string? GetEnvironmentVariable(string name);

    string GetFolderPath(Environment.SpecialFolder folder);

    /// <summary>
    /// True when the current process is running on macOS. Cross-platform terminals (WezTerm,
    /// Alacritty, …) gate macOS-specific integrations on this so their Linux/Windows users
    /// don't get Cmd+letter bindings forced onto them.
    /// </summary>
    bool IsMacOS => OperatingSystem.IsMacOS();

    /// <summary>
    /// True when the current process is running on Windows. Drives the default Terminal.Gui driver
    /// choice: the auto-selected <c>ansi</c> driver mis-decodes kitty key events on Windows
    /// (issue #82), so we default to the native <c>windows</c> driver there.
    /// </summary>
    bool IsWindows => OperatingSystem.IsWindows();
}
