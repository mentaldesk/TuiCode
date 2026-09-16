using TuiCode.Syntax;

namespace TuiCode.Workbench.Themes;

/// <summary>Our own TG themes (#11), merged into TG's built-ins as runtime config.</summary>
public static class BundledThemes
{
    public const string TurboPascal = "Turbo Pascal";
    public const string ModernBorland = "Modern Borland";
    public const string Midnight = "Midnight";

    public static IReadOnlyList<string> Names { get; } = [TurboPascal, ModernBorland, Midnight];

    public static string Config { get; } = ReadConfig();

    /// <summary>The token theme that suits <paramref name="theme"/>, or null to pick Dark+ or Light+ by background.</summary>
    public static string? TokenThemeFor(string theme) =>
        theme is TurboPascal or ModernBorland ? GrammarBundle.BorlandTheme : null;

    private static string ReadConfig()
    {
        using var stream = typeof(BundledThemes).Assembly.GetManifestResourceStream("themes.json")
            ?? throw new InvalidOperationException("The embedded themes are missing.");
        return new StreamReader(stream).ReadToEnd();
    }
}
