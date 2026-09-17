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

/// <summary>Shared by every editor tab, so each grammar compiles once. Token colours come from <see cref="Theme"/>.</summary>
public sealed class SyntaxHighlighter
{
    /// <summary>The foreground id TextMate gives tokens the theme has no rule for.</summary>
    public const int DefaultForeground = 1;

    /// <summary>The association value that turns highlighting off for matching files.</summary>
    public const string PlainText = "plaintext";

    private readonly GrammarBundle _bundle;
    private readonly Registry _registry;
    private Dictionary<string, string> _associations = new(StringComparer.OrdinalIgnoreCase);

    public SyntaxHighlighter(GrammarBundle bundle)
    {
        _bundle = bundle;
        _registry = new Registry(bundle);
        Colors = ReadColors();
    }

    public IReadOnlyCollection<SyntaxLanguage> Languages => _bundle.Languages;

    public IReadOnlyDictionary<string, SyntaxLanguage> DefaultAssociations => _bundle.Associations;

    public IReadOnlyList<string> Problems => _bundle.Problems;

    /// <summary>The user's associations (pattern → language id or <see cref="PlainText"/>), which win over the defaults.</summary>
    public IReadOnlyDictionary<string, string> Associations
    {
        get => _associations;
        set => _associations = new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The file's language by the user's associations, then the defaults; null means plain text.</summary>
    public SyntaxLanguage? LanguageForFile(string path) =>
        GrammarBundle.Match(_associations, path) is { } id ? LanguageById(id) : _bundle.LanguageForFile(path);

    public SyntaxLanguage? LanguageById(string id) => _bundle.LanguageById(id);

    /// <summary>Changes with every theme switch: token metadata from an older version encodes the old theme's colours.</summary>
    public int ThemeVersion { get; private set; }

    /// <summary><c>#RRGGBB</c> by the foreground id encoded in token metadata; index 0 is unused.</summary>
    public IReadOnlyList<string> Colors { get; private set; }

    /// <summary>The token theme file in use, e.g. <see cref="GrammarBundle.DarkTheme"/>.</summary>
    public string Theme { get; private set; } = GrammarBundle.DarkTheme;

    /// <summary>The theme's VS Code <c>colors</c>, e.g. <c>editorCursor.foreground</c> → <c>#RRGGBB</c>.</summary>
    public IReadOnlyDictionary<string, string> EditorColors => _registry.GetTheme().GetGuiColorDictionary();

    public void UseTheme(string theme)
    {
        if (theme == Theme) return;
        _registry.SetTheme(_bundle.GetTheme(theme) ?? throw new ArgumentException($"No token theme named {theme}.", nameof(theme)));
        Theme = theme;
        Colors = ReadColors();
        ThemeVersion++;
    }

    public LineTokenCache? CreateCache(SyntaxLanguage? language)
    {
        if (language is null) return null;
        try
        {
            return _registry.LoadGrammar(language.ScopeName) is { } grammar ? new LineTokenCache(this, grammar, language) : null;
        }
        // User grammars are untrusted input to TextMateSharp, which throws various exceptions for malformed ones.
        catch (Exception)
        {
            return null;
        }
    }

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
