using TextMateSharp.Internal.Grammars;
using TextMateSharp.Registry;

namespace TuiCode.Syntax;

[Flags]
public enum TokenStyle
{
    None = 0,
    Italic = 1,
    Bold = 2,
    Underline = 4,
    Strikethrough = 8,
}

/// <summary>Shared by every editor tab, so each grammar compiles once. Token colours come from Dark+ or Light+.</summary>
public sealed class SyntaxHighlighter
{
    /// <summary>The foreground id TextMate gives tokens the theme has no rule for.</summary>
    public const int DefaultForeground = 1;

    private readonly GrammarBundle _bundle;
    private readonly Registry _registry;

    public SyntaxHighlighter(GrammarBundle bundle)
    {
        _bundle = bundle;
        _registry = new Registry(bundle);
        Colors = ReadColors();
    }

    public bool IsDark { get; private set; } = true;

    /// <summary>Changes with every theme switch: token metadata from an older version encodes the old theme's colours.</summary>
    public int ThemeVersion { get; private set; }

    /// <summary><c>#RRGGBB</c> by the foreground id encoded in token metadata; index 0 is unused.</summary>
    public IReadOnlyList<string> Colors { get; private set; }

    public void UseTheme(bool dark)
    {
        if (dark == IsDark) return;
        _registry.SetTheme(_bundle.GetTheme(dark ? GrammarBundle.DarkTheme : GrammarBundle.LightTheme)!);
        IsDark = dark;
        Colors = ReadColors();
        ThemeVersion++;
    }

    public LineTokenCache? CreateCache(string path) =>
        _bundle.LanguageForFile(path) is { } language && _registry.LoadGrammar(language.ScopeName) is { } grammar
            ? new LineTokenCache(this, grammar)
            : null;

    public static int ForegroundOf(int metadata) => EncodedTokenAttributes.GetForeground(metadata);

    public static TokenStyle StyleOf(int metadata) => (TokenStyle)EncodedTokenAttributes.GetFontStyle(metadata);

    private string[] ReadColors()
    {
        var theme = _registry.GetTheme();
        var colors = new string[_registry.GetColorMap().Count + 1];
        for (var id = 1; id < colors.Length; id++)
            colors[id] = theme.GetColor(id);
        return colors;
    }
}
