using System.Reflection;
using System.Text.Json;
using Terminal.Gui.Configuration;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Services;

public sealed class ThemeService : IThemeService
{
    private readonly Func<string, Stream?> _themeStreamLoader;
    private readonly IReadOnlyCollection<string> _availableThemes;
    private Dictionary<string, Color> _tokens = new(StringComparer.Ordinal);
    private string _currentTheme = "";

    public string CurrentTheme => _currentTheme;
    public IReadOnlyCollection<string> AvailableThemes => _availableThemes;
    public event EventHandler? ThemeChanged;

    public ThemeService() : this(LoadEmbeddedTheme, DiscoverEmbeddedThemes()) { }

    // Test seam: caller supplies the theme-loading function and the available-themes list.
    internal ThemeService(Func<string, Stream?> themeStreamLoader, IReadOnlyCollection<string> availableThemes)
    {
        _themeStreamLoader = themeStreamLoader;
        _availableThemes = availableThemes;
    }

    public Color GetColor(string token)
    {
        if (_tokens.TryGetValue(token, out var color))
            return color;
        throw new KeyNotFoundException(
            $"Theme token '{token}' is not defined by theme '{_currentTheme}'. " +
            "Add it to the theme JSON or remove the lookup.");
    }

    public void LoadTheme(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using var stream = _themeStreamLoader(name)
            ?? throw new ArgumentException($"Unknown theme '{name}'", nameof(name));

        _tokens = ParseTokens(stream);
        _currentTheme = name;

        RegisterSchemes();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Dictionary<string, Color> ParseTokens(Stream stream)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Theme JSON did not deserialize to a token map");

        var result = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var (token, value) in dict)
        {
            if (!Color.TryParse(value, out var color))
                throw new InvalidOperationException($"Theme token '{token}' has invalid colour '{value}'");
            result[token] = color!.Value;
        }
        return result;
    }

    private void RegisterSchemes()
    {
        Register(SchemeNames.Editor, ThemeTokens.EditorForeground, ThemeTokens.EditorBackground);
        Register(SchemeNames.Sidebar, ThemeTokens.SideBarForeground, ThemeTokens.SideBarBackground,
            focusFg: ThemeTokens.ListFocusForeground, focusBg: ThemeTokens.ListFocusBackground);
        Register(SchemeNames.StatusBar, ThemeTokens.StatusBarForeground, ThemeTokens.StatusBarBackground);
        Register(SchemeNames.Tabs, ThemeTokens.TabInactiveForeground, ThemeTokens.TabInactiveBackground,
            focusFg: ThemeTokens.TabActiveForeground, focusBg: ThemeTokens.TabActiveBackground);
    }

    private void Register(string schemeName, string fgToken, string bgToken,
        string? focusFg = null, string? focusBg = null)
    {
        var fg = GetColor(fgToken);
        var bg = GetColor(bgToken);
        var focus = (focusFg, focusBg) switch
        {
            (not null, not null) => new Terminal.Gui.Drawing.Attribute(GetColor(focusFg), GetColor(focusBg)),
            _ => new Terminal.Gui.Drawing.Attribute(fg, bg)
        };
        var scheme = new Scheme
        {
            Normal = new Terminal.Gui.Drawing.Attribute(fg, bg),
            Focus = focus,
            HotNormal = new Terminal.Gui.Drawing.Attribute(fg, bg),
            HotFocus = focus,
            Disabled = new Terminal.Gui.Drawing.Attribute(fg, bg)
        };
        SchemeManager.AddScheme(schemeName, scheme);
    }

    private static Stream? LoadEmbeddedTheme(string name)
    {
        var asm = typeof(ThemeService).Assembly;
        return asm.GetManifestResourceStream(ThemeResourceName(asm, name));
    }

    private static string ThemeResourceName(Assembly asm, string name) =>
        $"{asm.GetName().Name}.Themes.{name}.json";

    private static IReadOnlyCollection<string> DiscoverEmbeddedThemes()
    {
        var asm = typeof(ThemeService).Assembly;
        var prefix = $"{asm.GetName().Name}.Themes.";
        return asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n.Substring(prefix.Length, n.Length - prefix.Length - ".json".Length))
            .ToArray();
    }
}
