using TuiCode.Abstractions;

namespace TuiCode.Workbench.Configuration;

/// <summary>
/// Picks the Terminal.Gui driver to boot. On Windows the auto-selected <c>ansi</c> driver
/// mis-decodes kitty key events (Backspace / Shift+letter / Ctrl+digit get dropped), so we
/// default to the native <c>windows</c> driver, which reads keys through the Win32 Console API
/// and decodes them correctly in Windows Terminal. See issue #82. The override lets the driver
/// be A/B-tested (or worked around) without recompiling.
/// </summary>
/// <remarks>
/// Precedence: the <c>--driver &lt;name&gt;</c> / <c>--driver=&lt;name&gt;</c> CLI flag wins over the
/// <c>TUICODE_DRIVER</c> environment variable, which wins over the per-OS default (<c>windows</c>
/// on Windows, otherwise <c>null</c> = TG auto-selects). A blank override falls through to the
/// default. The resolved name is passed straight to <c>Application.Init</c>, which validates it
/// against <c>DriverRegistry</c> (TG 2.1.0 registers <c>windows</c>, <c>dotnet</c>, <c>ansi</c>)
/// and throws on an unknown name.
/// </remarks>
public static class DriverSelection
{
    public const string EnvironmentVariable = "TUICODE_DRIVER";

    // DriverRegistry.Names.WINDOWS — the native Win32 Console driver. Kept as a literal so this
    // resolver stays free of Terminal.Gui and unit-testable without booting a driver.
    private const string WindowsDriver = "windows";

    private const string Flag = "--driver";

    public static string? Resolve(IReadOnlyList<string> args, IEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        return ResolveOverride(args, environment) ?? DefaultFor(environment);
    }

    private static string? ResolveOverride(IReadOnlyList<string> args, IEnvironment environment)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith(Flag + "=", StringComparison.Ordinal))
                return Normalize(arg[(Flag.Length + 1)..]);
            if (arg == Flag && i + 1 < args.Count)
                return Normalize(args[i + 1]);
        }

        return Normalize(environment.GetEnvironmentVariable(EnvironmentVariable));
    }

    private static string? DefaultFor(IEnvironment environment) =>
        environment.IsWindows ? WindowsDriver : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
