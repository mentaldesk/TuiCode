using Terminal.Gui.Configuration;

namespace TuiCode.Workbench.Configuration;

/// <summary>
/// Static persistence surface for TG's <see cref="ConfigurationManager"/>. Properties decorated
/// with <see cref="ConfigurationPropertyAttribute"/> are loaded from the JSON config hierarchy on
/// <see cref="ConfigurationManager.Apply"/>. Keys in the file are auto-prefixed with the class
/// name, so <c>Theme</c> appears as <c>TuiCodeSettings.Theme</c>.
///
/// Don't read or write this directly — go through <see cref="TuiCode.Abstractions.ISettingsService"/>.
/// </summary>
public static class TuiCodeSettings
{
    public const string DefaultTheme = "Default";

    [ConfigurationProperty(Scope = typeof(AppSettingsScope))]
    public static string Theme { get; set; } = DefaultTheme;

    // Keybindings used to live here as another [ConfigurationProperty], but TG's
    // ConfigurationManager uses source-generated JsonTypeInfo and silently fails to
    // deserialize types it doesn't know — including KeybindingOverride[] AND string[].
    // Keybindings now persist to ~/.tui/TuiCode.keybindings.json via DefaultSettingsService,
    // outside TG's config system, so we can use a clean human-readable JSON shape.
}
