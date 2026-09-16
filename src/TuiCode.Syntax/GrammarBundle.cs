using System.IO.Compression;
using System.Text.Json.Nodes;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace TuiCode.Syntax;

public sealed record SyntaxLanguage(string Id, string Name, string ScopeName);

/// <summary>TextMateSharp.Grammars' grammars and themes, re-packed as a compressed embedded zip; the package's own loader embeds 6.7 MB uncompressed.</summary>
public sealed class GrammarBundle : IRegistryOptions
{
    public const string DarkTheme = "dark_plus.json";
    public const string LightTheme = "light_plus.json";

    private readonly ZipArchive _archive;
    private readonly Lock _archiveLock = new();
    private readonly Dictionary<string, string> _grammarEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SyntaxLanguage> _associations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SyntaxLanguage> _languages = new(StringComparer.Ordinal);

    public static GrammarBundle Load() =>
        new(typeof(GrammarBundle).Assembly.GetManifestResourceStream("Grammars.zip")
            ?? throw new InvalidOperationException("The embedded grammar bundle is missing."));

    private GrammarBundle(Stream archive)
    {
        _archive = new ZipArchive(archive, ZipArchiveMode.Read);
        foreach (var manifest in _archive.Entries.Where(e => e.Name == "package.json").ToArray())
            AddPackage(manifest.FullName[..^manifest.Name.Length], ReadJson(manifest));
    }

    public IReadOnlyCollection<SyntaxLanguage> Languages => _languages.Values;

    public IReadOnlyCollection<string> ScopeNames => _grammarEntries.Keys;

    /// <summary>File-name patterns — an exact name like <c>Dockerfile</c> or an extension like <c>.cs</c> — to their language.</summary>
    public IReadOnlyDictionary<string, SyntaxLanguage> Associations => _associations;

    public SyntaxLanguage? LanguageForFile(string path) => Match(_associations, path);

    public SyntaxLanguage? LanguageById(string id) => _languages.GetValueOrDefault(id);

    /// <summary>Looks up the exact file name first, then its extensions, longest first (<c>.js.map</c> before <c>.map</c>).</summary>
    public static TValue? Match<TValue>(IReadOnlyDictionary<string, TValue> associations, string path) where TValue : class
    {
        var name = Path.GetFileName(path);
        if (associations.TryGetValue(name, out var value))
            return value;

        for (var dot = name.IndexOf('.'); dot >= 0; dot = name.IndexOf('.', dot + 1))
        {
            if (associations.TryGetValue(name[dot..], out value))
                return value;
        }
        return null;
    }

    public IRawGrammar? GetGrammar(string scopeName) =>
        _grammarEntries.TryGetValue(scopeName, out var entry) && OpenText(entry) is { } reader
            ? GrammarReader.ReadGrammarSync(reader)
            : null;

    // Also called with a theme's include of its base theme, e.g. "./dark_vs.json".
    public IRawTheme? GetTheme(string scopeName) =>
        OpenText("themes/" + Path.GetFileName(scopeName)) is { } reader ? ThemeReader.ReadThemeSync(reader) : null;

    public IRawTheme GetDefaultTheme() => GetTheme(DarkTheme)!;

    public ICollection<string>? GetInjections(string scopeName) => null;

    private void AddPackage(string directory, JsonNode manifest)
    {
        var contributes = manifest["contributes"];
        var scopeByLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var grammar in contributes?["grammars"]?.AsArray() ?? [])
        {
            var scope = grammar!["scopeName"]!.GetValue<string>();
            var entry = directory + grammar["path"]!.GetValue<string>().TrimStart('.', '/');
            if (_archive.GetEntry(entry) is null)
                continue;
            _grammarEntries.TryAdd(scope, entry);
            if (grammar["language"]?.GetValue<string>() is { } languageId)
                scopeByLanguage.TryAdd(languageId, scope);
        }

        foreach (var node in contributes?["languages"]?.AsArray() ?? [])
        {
            var id = node!["id"]!.GetValue<string>();
            if (!scopeByLanguage.TryGetValue(id, out var scope))
                continue;
            var name = node["aliases"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? id;
            if (!_languages.TryGetValue(id, out var language))
                _languages[id] = language = new SyntaxLanguage(id, name, scope);
            foreach (var fileName in node["filenames"]?.AsArray() ?? [])
                _associations.TryAdd(fileName!.GetValue<string>(), language);
            foreach (var extension in node["extensions"]?.AsArray() ?? [])
                _associations.TryAdd(extension!.GetValue<string>(), language);
        }
    }

    private static JsonNode ReadJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonNode.Parse(stream)!;
    }

    // ZipArchive isn't thread-safe.
    private StreamReader? OpenText(string entryName)
    {
        var buffer = new MemoryStream();
        lock (_archiveLock)
        {
            if (_archive.GetEntry(entryName) is not { } entry)
                return null;
            using var stream = entry.Open();
            stream.CopyTo(buffer);
        }
        buffer.Position = 0;
        return new StreamReader(buffer);
    }
}
