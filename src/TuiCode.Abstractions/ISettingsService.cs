namespace TuiCode.Abstractions;

/// <summary>
/// User-facing settings: read current values, mutate them (with live preview side-effects),
/// enumerate available themes, persist to disk. Wraps Terminal.Gui's static
/// <c>ConfigurationManager</c> / <c>ThemeManager</c> surface so the rest of the app stays DI-uniform.
/// </summary>
public interface ISettingsService
{
    /// <summary>Current theme name. Setter applies the theme live.</summary>
    string Theme { get; set; }

    /// <summary>Names of all themes the user can pick from (TG built-ins for v1).</summary>
    IReadOnlyCollection<string> AvailableThemes { get; }

    /// <summary>Persist the current settings to disk, writing only values that differ from defaults.</summary>
    void Save();
}
