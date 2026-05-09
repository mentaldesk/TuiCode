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

    /// <summary>
    /// Current keybinding overrides — additions and removals layered on top of the workbench
    /// defaults. The list is ordered: later entries shadow earlier ones.
    /// </summary>
    IReadOnlyList<KeybindingOverride> KeybindingOverrides { get; }

    /// <summary>Replace the override list. Stages the change in memory; <see cref="Save"/> persists it.</summary>
    void SetKeybindingOverrides(IEnumerable<KeybindingOverride> overrides);

    /// <summary>Persist the current settings to disk, writing only values that differ from defaults.</summary>
    void Save();

    /// <summary>
    /// Load settings from disk and apply them to the underlying configuration system. Call once
    /// at startup, before any UI is constructed, so views render with the saved values on first paint.
    /// </summary>
    void Load();
}
