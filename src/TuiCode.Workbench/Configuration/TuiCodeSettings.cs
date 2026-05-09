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
}
