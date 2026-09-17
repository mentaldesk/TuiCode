using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace TuiCode.Syntax;

public sealed record SyntaxLanguage(string Id, string Name, string ScopeName);

/// <summary>User grammar packages, then TextMateSharp.Grammars' re-packed as a compressed zip (its own loader embeds 6.7 MB uncompressed).</summary>
public sealed class GrammarBundle : IRegistryOptions
{
    public const string DarkTheme = "dark_plus.json";
    public const string LightTheme = "light_plus.json";

    private readonly ZipArchive _archive;
    private readonly Lock _archiveLock = new();
    private readonly Dictionary<string, Func<StreamReader?>> _grammars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SyntaxLanguage> _associations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SyntaxLanguage> _languages = new(StringComparer.Ordinal);
    private readonly List<string> _problems = [];

    public static GrammarBundle Load() => new(null, null);

    /// <summary>Also loads each VS Code-style grammar package under <paramref name="userGrammars"/>; unusable ones go to <see cref="Problems"/>.</summary>
    public static GrammarBundle Load(IFileSystem fileSystem, string userGrammars) => new(fileSystem, userGrammars);

    private GrammarBundle(IFileSystem? fileSystem, string? userGrammars)
    {
        _archive = new ZipArchive(
            typeof(GrammarBundle).Assembly.GetManifestResourceStream("Grammars.zip")
            ?? throw new InvalidOperationException("The embedded grammar bundle is missing."),
            ZipArchiveMode.Read);

        // First registration wins, so user packages go first.
        if (fileSystem is not null && userGrammars is not null && fileSystem.Directory.Exists(userGrammars))
        {
            foreach (var directory in fileSystem.Directory.GetDirectories(userGrammars).Order(StringComparer.Ordinal))
                AddUserPackage(fileSystem, directory);
        }

        foreach (var manifest in _archive.Entries.Where(e => e.Name == "package.json").ToArray())
        {
            var directory = manifest.FullName[..^manifest.Name.Length];
            AddPackage(ReadJson(manifest), path => _archive.GetEntry(directory + path) is null ? null : () => OpenText(directory + path));
        }
    }

    /// <summary>Why user grammar packages, or grammars in them, weren't loaded.</summary>
    public IReadOnlyList<string> Problems => _problems;

    public IReadOnlyCollection<SyntaxLanguage> Languages => _languages.Values;

    public IReadOnlyCollection<string> ScopeNames => _grammars.Keys;

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
        _grammars.TryGetValue(scopeName, out var open) && open() is { } reader
            ? GrammarReader.ReadGrammarSync(reader)
            : null;

    /// <summary>Every token theme: ours, then the zip's.</summary>
    public IEnumerable<string> ThemeNames =>
        typeof(GrammarBundle).Assembly.GetManifestResourceNames()
            .Concat(_archive.Entries.Select(e => e.FullName))
            .Where(name => name.StartsWith("themes/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(Path.GetFileName)!;

    public IRawTheme? GetTheme(string scopeName)
    {
        if (ReadTheme(scopeName) is not { } theme) return null;
        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(theme.ToJsonString())));
        return ThemeReader.ReadThemeSync(reader);
    }

    // TextMateSharp follows only one level of include, which would drop dark_vs.json from a theme that includes dark_plus.json.
    private JsonObject? ReadTheme(string name)
    {
        var entryName = "themes/" + Path.GetFileName(name);
        JsonObject theme;
        using (var reader = typeof(GrammarBundle).Assembly.GetManifestResourceStream(entryName) is { } own ? new StreamReader(own) : OpenText(entryName))
        {
            if (reader is null) return null;
            theme = JsonNode.Parse(reader.ReadToEnd(), documentOptions: ThemeJsonOptions)!.AsObject();
        }

        if (!theme.Remove("include", out var include) || ReadTheme(include!.GetValue<string>()) is not { } baseTheme)
            return theme;

        var tokenColors = Take(baseTheme, "tokenColors") as JsonArray ?? [];
        foreach (var rule in Take(theme, "tokenColors") as JsonArray ?? [])
            tokenColors.Add((JsonNode)rule!.DeepClone());
        theme["tokenColors"] = tokenColors;

        var colors = Take(baseTheme, "colors") as JsonObject ?? [];
        foreach (var (key, value) in Take(theme, "colors") as JsonObject ?? [])
            colors[key] = value?.DeepClone();
        theme["colors"] = colors;
        return theme;
    }

    private static readonly JsonDocumentOptions ThemeJsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonNode? Take(JsonObject theme, string property) =>
        theme.Remove(property, out var node) ? node : null;

    public IRawTheme GetDefaultTheme() => GetTheme(DarkTheme)!;

    public ICollection<string>? GetInjections(string scopeName) => null;

    private void AddUserPackage(IFileSystem fs, string directory)
    {
        var package = fs.Path.GetFileName(directory);
        var manifestPath = fs.Path.Combine(directory, "package.json");
        if (!fs.File.Exists(manifestPath))
        {
            _problems.Add($"{package}: no package.json");
            return;
        }

        try
        {
            var manifest = JsonNode.Parse(fs.File.ReadAllText(manifestPath)) ?? throw new JsonException("empty");
            var added = AddPackage(manifest, path =>
            {
                var file = fs.Path.GetFullPath(fs.Path.Combine(directory, path));
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    _problems.Add($"{package}: {path} isn't a JSON grammar (convert .tmLanguage or YAML grammars to JSON)");
                else if (!fs.File.Exists(file))
                    _problems.Add($"{package}: {path} not found");
                else
                    return () => new StreamReader(new MemoryStream(fs.File.ReadAllBytes(file)));
                return null;
            });
            if (added == 0)
                _problems.Add($"{package}: no usable grammars");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException)
        {
            _problems.Add($"{package}: package.json couldn't be read ({e.Message})");
        }
    }

    /// <summary>Registers a package's grammars and languages; <paramref name="grammarAt"/> opens a grammar by its manifest path, or returns null.</summary>
    private int AddPackage(JsonNode manifest, Func<string, Func<StreamReader?>?> grammarAt)
    {
        var contributes = manifest["contributes"];
        var scopeByLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
        var added = 0;
        foreach (var grammar in contributes?["grammars"]?.AsArray() ?? [])
        {
            if (grammar?["scopeName"]?.GetValue<string>() is not { } scope
                || grammar["path"]?.GetValue<string>() is not { } path
                || grammarAt(path.TrimStart('.', '/')) is not { } open)
                continue;
            _grammars.TryAdd(scope, open);
            added++;
            if (grammar["language"]?.GetValue<string>() is { } languageId)
                scopeByLanguage.TryAdd(languageId, scope);
        }

        foreach (var node in contributes?["languages"]?.AsArray() ?? [])
        {
            if (node?["id"]?.GetValue<string>() is not { } id || !scopeByLanguage.TryGetValue(id, out var scope))
                continue;
            var name = node["aliases"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? id;
            if (!_languages.TryGetValue(id, out var language))
                _languages[id] = language = new SyntaxLanguage(id, name, scope);
            foreach (var fileName in node["filenames"]?.AsArray() ?? [])
                _associations.TryAdd(fileName!.GetValue<string>(), language);
            foreach (var extension in node["extensions"]?.AsArray() ?? [])
                _associations.TryAdd(extension!.GetValue<string>(), language);
        }
        return added;
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
