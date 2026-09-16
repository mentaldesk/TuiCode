namespace TuiCode.Workbench.Themes;

/// <summary>The themes we offer (#11): TG theme JSON merged in as runtime config, each paired with a token theme.</summary>
public static class BundledThemes
{
    public const string Midnight = "Midnight";
    public const string Daylight = "Daylight";
    public const string TurboPascal = "Turbo Pascal";
    public const string ModernBorland = "Modern Borland";

    public const string Default = Midnight;

    public static IReadOnlyList<string> Names { get; } = [Midnight, Daylight, TurboPascal, ModernBorland];

    public static string Config { get; } = ReadConfig();

    /// <summary>The token theme file, which also carries the editor's gutter and cursor colours.</summary>
    public static string TokenThemeFor(string theme) => theme switch
    {
        Daylight => "daylight.json",
        TurboPascal => "turbo-pascal.json",
        ModernBorland => "modern-borland.json",
        _ => "midnight.json",
    };

    /// <summary>Ours for a theme saved before we shipped our own: TG's Light becomes Daylight, anything else Midnight.</summary>
    public static string Migrate(string theme) =>
        Names.Contains(theme) ? theme
        : string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? Daylight
        : Default;

    private static string ReadConfig()
    {
        using var stream = typeof(BundledThemes).Assembly.GetManifestResourceStream("themes.json")
            ?? throw new InvalidOperationException("The embedded themes are missing.");
        return new StreamReader(stream).ReadToEnd();
    }
}
