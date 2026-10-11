using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Workbench.Themes;

namespace TuiCode.Workbench.Configuration;

/// <summary>
/// DI-friendly thin wrapper around TG's static <see cref="TuiConfigurationBuilder"/> /
/// <see cref="ThemeManager"/>. Tests substitute an in-memory implementation; production
/// code never touches the static surface directly.
///
/// <para>Theme persists via TG's native <c>Theme</c> setting, written as
/// <c>{"Theme": "Daylight"}</c> at the JSON root of <c>~/.tui/TuiCode.config.json</c>.
/// <see cref="Load"/> calls <c>TuiConfigurationBuilder.ApplyToStaticFacades</c>, which reads the file and
/// applies the theme — no custom load logic needed. Saving still goes through us because
/// TG exposes no Save API.</para>
///
/// <para>Keybindings persist to a sibling file <c>~/.tui/TuiCode.keybindings.json</c>
/// that we read and write directly — TG's source-generated <c>JsonTypeInfo</c> only
/// knows the types its built-in scopes use, so complex types silently fail to deserialize.
/// A dedicated file dodges that and gives us a clean, hand-readable JSON shape.</para>
/// </summary>
public sealed class DefaultSettingsService : ISettingsService
{
    private readonly IFileSystem _fs;
    private readonly ILogger _logger;
    private readonly string _themeConfigPath;
    private readonly string _keybindingsPath;
    private readonly string _grammarsPath;
    private readonly string _settingsPath;
    private List<KeybindingOverride> _keybindings;
    private Dictionary<string, string> _grammarAssociations;

    public DefaultSettingsService(IFileSystem fs, ILogger<DefaultSettingsService>? logger = null)
    {
        _fs = fs;
        _logger = logger ?? NullLogger<DefaultSettingsService>.Instance;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = _fs.Path.Combine(home, ".tui");
        _themeConfigPath = _fs.Path.Combine(dir, "TuiCode.config.json");
        _keybindingsPath = _fs.Path.Combine(dir, "TuiCode.keybindings.json");
        _grammarsPath = _fs.Path.Combine(dir, "TuiCode.grammars.json");
        _settingsPath = _fs.Path.Combine(dir, "TuiCode.settings.json");
        _keybindings = LoadKeybindings();
        _grammarAssociations = LoadGrammarAssociations();
        LoadSettings();
    }

    public string Theme
    {
        get => ThemeManager.Theme;
        set
        {
            if (string.Equals(ThemeManager.Theme, value, StringComparison.Ordinal)) return;
            ThemeManager.Theme = value;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ThemeChanged;

    // TG's built-ins don't describe the editor's gutter or cursor, so we only offer our own.
    public IReadOnlyCollection<string> AvailableThemes =>
        BundledThemes.Names.Where(ThemeManager.GetThemeNames().Contains).ToArray();

    public IReadOnlyList<KeybindingOverride> KeybindingOverrides => _keybindings;

    public void SetKeybindingOverrides(IEnumerable<KeybindingOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        _keybindings = overrides.ToList();
    }

    public IReadOnlyDictionary<string, string> GrammarAssociations => _grammarAssociations;

    public void SetGrammarAssociations(IReadOnlyDictionary<string, string> associations)
    {
        ArgumentNullException.ThrowIfNull(associations);
        _grammarAssociations = new Dictionary<string, string>(associations, StringComparer.OrdinalIgnoreCase);
    }

    public FileIconStyle FileIcons { get; set; }

    public EditorSettings Editor { get; set; } = EditorSettings.Default;

    public int SidebarWidth { get; set; } = SidebarSizing.Default;

    public IReadOnlyDictionary<string, LanguageServerSetting> LanguageServers { get; set; } =
        new Dictionary<string, LanguageServerSetting>(StringComparer.OrdinalIgnoreCase);

    public bool SettingsFileInvalid { get; private set; }

    public void Load()
    {
        TuiConfigurationBuilder.Shared.RuntimeConfig = BundledThemes.Config;
        TuiConfigurationBuilder.Shared.ApplyToStaticFacades();
        Theme = BundledThemes.Migrate(ThemeManager.Theme);
    }

    public void Save()
    {
        SaveTheme();
        SaveKeybindings();
        SaveGrammarAssociations();
        SaveSettings();
    }

    // Settings TG doesn't know. They can't share TuiCode.config.json: TG drops that whole file when it meets an unknown key.
    private void SaveSettings()
    {
        var root = new JsonObject();
        if (FileIcons != FileIconStyle.Auto)
            root["FileIcons"] = FileIcons.ToString();
        var defaults = EditorSettings.Default;
        if (Editor.IndentSize != defaults.IndentSize)
            root["IndentSize"] = Editor.IndentSize;
        if (Editor.InsertSpaces != defaults.InsertSpaces)
            root["InsertSpaces"] = Editor.InsertSpaces;
        if (Editor.LineEnding != defaults.LineEnding)
            root["LineEnding"] = Editor.LineEnding.ToString();
        if (Editor.InsertFinalNewline != defaults.InsertFinalNewline)
            root["InsertFinalNewline"] = Editor.InsertFinalNewline;
        if (Editor.WordWrap != defaults.WordWrap)
            root["WordWrap"] = Editor.WordWrap;
        if (Editor.StickyLines != defaults.StickyLines)
            root["StickyLines"] = Editor.StickyLines;
        if (Editor.WrapByLanguage.Count > 0)
        {
            var wrapByLanguage = new JsonObject();
            foreach (var (language, wrap) in Editor.WrapByLanguage.OrderBy(w => w.Key, StringComparer.OrdinalIgnoreCase))
                wrapByLanguage[language] = wrap;
            root["WrapByLanguage"] = wrapByLanguage;
        }
        if (SidebarWidth != SidebarSizing.Default)
            root["SidebarWidth"] = SidebarWidth;
        if (LanguageServers.Count > 0)
        {
            var servers = new JsonObject();
            foreach (var (language, server) in LanguageServers.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
                servers[language] = new JsonObject
                {
                    ["Command"] = server.Command,
                    ["Arguments"] = new JsonArray([.. server.Arguments.Select(a => (JsonNode?)a)]),
                };
            root["LanguageServers"] = servers;
        }

        if (root.Count == 0)
        {
            if (_fs.File.Exists(_settingsPath))
                _fs.File.Delete(_settingsPath);
            return;
        }
        EnsureDirExists(_settingsPath);
        _fs.File.WriteAllText(_settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // A flat object, e.g. { ".h": "cpp", "Jenkinsfile": "groovy" }, sorted so the file diffs cleanly.
    private void SaveGrammarAssociations()
    {
        if (_grammarAssociations.Count == 0)
        {
            if (_fs.File.Exists(_grammarsPath))
                _fs.File.Delete(_grammarsPath);
            return;
        }

        var root = new JsonObject();
        foreach (var (pattern, grammar) in _grammarAssociations.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
            root[pattern] = grammar;
        EnsureDirExists(_grammarsPath);
        _fs.File.WriteAllText(_grammarsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private Dictionary<string, string> LoadGrammarAssociations()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!_fs.File.Exists(_grammarsPath)) return result;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(_fs.File.ReadAllText(_grammarsPath)) as JsonObject;
        }
        catch (JsonException e)
        {
            LogUnreadable(_grammarsPath, e);
            return result;
        }

        // Like the keybindings file (#90): skip a hand-edited entry of the wrong type rather than lose the rest.
        foreach (var (pattern, value) in root ?? [])
        {
            if (value is JsonValue v && v.TryGetValue<string>(out var grammar) && pattern.Length > 0 && grammar.Length > 0)
                result[pattern] = grammar;
        }
        return result;
    }

    private void SaveTheme()
    {
        var root = new JsonObject();
        if (!string.Equals(ThemeManager.Theme, BundledThemes.Default, StringComparison.Ordinal))
            root["Theme"] = ThemeManager.Theme;

        EnsureDirExists(_themeConfigPath);
        _fs.File.WriteAllText(_themeConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // Each value falls back to its default on its own, so one bad hand edit doesn't lose the rest.
    private void LoadSettings()
    {
        JsonObject? root = null;
        if (_fs.File.Exists(_settingsPath))
        {
            try
            {
                root = JsonNode.Parse(_fs.File.ReadAllText(_settingsPath)) as JsonObject;
            }
            catch (JsonException e)
            {
                SettingsFileInvalid = true;
                LogUnreadable(_settingsPath, e);
            }
        }

        var defaults = EditorSettings.Default;
        FileIcons = ReadEnum(root, "FileIcons", FileIconStyle.Auto);
        Editor = new EditorSettings
        {
            IndentSize = Read(root, "IndentSize", defaults.IndentSize) is var size
                && size is >= EditorSettings.MinIndentSize and <= EditorSettings.MaxIndentSize
                    ? size
                    : defaults.IndentSize,
            InsertSpaces = Read(root, "InsertSpaces", defaults.InsertSpaces),
            LineEnding = ReadEnum(root, "LineEnding", defaults.LineEnding),
            InsertFinalNewline = Read(root, "InsertFinalNewline", defaults.InsertFinalNewline),
            WordWrap = Read(root, "WordWrap", defaults.WordWrap),
            StickyLines = Read(root, "StickyLines", defaults.StickyLines),
            WrapByLanguage = ReadWrapByLanguage(root),
        };
        // A width above the spinner's maximum is legitimate on a wide terminal, so only the floor is validated.
        SidebarWidth = Read(root, "SidebarWidth", SidebarSizing.Default) is var width && width >= SidebarSizing.Min
            ? width
            : SidebarSizing.Default;
        LanguageServers = ReadLanguageServers(root);
    }

    private static Dictionary<string, LanguageServerSetting> ReadLanguageServers(JsonObject? root)
    {
        var result = new Dictionary<string, LanguageServerSetting>(StringComparer.OrdinalIgnoreCase);
        if (root?["LanguageServers"] is not JsonObject servers) return result;
        foreach (var (language, value) in servers)
        {
            if (language.Length == 0 || value is not JsonObject server) continue;
            if (server["Command"] is not JsonValue c || !c.TryGetValue<string>(out var command)) continue;
            var arguments = new List<string>();
            if (server["Arguments"] is JsonArray array)
            {
                foreach (var argument in array)
                    if (argument is JsonValue a && a.TryGetValue<string>(out var text)) arguments.Add(text);
                if (arguments.Count != array.Count) continue;
            }
            result[language] = new LanguageServerSetting(command.Trim(), arguments);
        }
        return result;
    }

    private static Dictionary<string, bool> ReadWrapByLanguage(JsonObject? root)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (root?["WrapByLanguage"] is not JsonObject languages) return result;
        foreach (var (language, value) in languages)
        {
            if (value is JsonValue v && v.TryGetValue<bool>(out var wrap) && language.Length > 0)
                result[language] = wrap;
        }
        return result;
    }

    private static T Read<T>(JsonObject? root, string key, T fallback) =>
        root?[key] is JsonValue v && v.TryGetValue<T>(out var value) ? value : fallback;

    private static T ReadEnum<T>(JsonObject? root, string key, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(Read(root, key, ""), ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : fallback;

    private void SaveKeybindings()
    {
        if (_keybindings.Count == 0)
        {
            if (_fs.File.Exists(_keybindingsPath))
                _fs.File.Delete(_keybindingsPath);
            return;
        }

        // Persist the chord by raw keycode (#89): identity must not depend on a display string whose
        // ToString()/TryParse can be lossy. "Label" is decorative — written for a human reading the
        // file, ignored on load.
        var arr = new JsonArray();
        foreach (var o in _keybindings)
        {
            var keys = new JsonArray();
            foreach (var k in o.Keys)
                keys.Add((JsonNode)(uint)k.KeyCode);
            arr.Add((JsonNode)new JsonObject
            {
                ["Keys"] = keys,
                ["Label"] = KeyChord.Display(o.Keys),
                ["Command"] = o.Command
            });
        }

        EnsureDirExists(_keybindingsPath);
        _fs.File.WriteAllText(_keybindingsPath, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private List<KeybindingOverride> LoadKeybindings()
    {
        if (!_fs.File.Exists(_keybindingsPath)) return new List<KeybindingOverride>();

        JsonArray? arr;
        try
        {
            arr = JsonNode.Parse(_fs.File.ReadAllText(_keybindingsPath)) as JsonArray;
        }
        catch (JsonException e)
        {
            LogUnreadable(_keybindingsPath, e);
            return new List<KeybindingOverride>();
        }
        if (arr is null) return new List<KeybindingOverride>();

        var result = new List<KeybindingOverride>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not JsonObject obj) continue;
            // Per-entry resilience (#90): a hand-edited entry can hold the wrong JSON type, which makes
            // GetValue throw. Skip just that entry rather than discarding every valid binding with it.
            // Entries from the pre-#89 format (a "Key" display string, no "Keys" array) have no keycode
            // chord, so they fall through to skip here — old custom bindings are dropped on upgrade and
            // re-saved in the new format on the next edit. See #89's notes.
            try
            {
                var keys = ReadChord(obj["Keys"] as JsonArray);
                var command = obj["Command"]?.GetValue<string>();
                if (keys is null || string.IsNullOrEmpty(command)) continue;
                result.Add(new KeybindingOverride(keys, command));
            }
            catch (InvalidOperationException)
            {
            }
        }
        return result;
    }

    /// <summary>
    /// Read a chord from its persisted keycode array, reconstructing each <see cref="Key"/> from its
    /// raw <see cref="KeyCode"/> — a lossless inverse of the keycode we wrote (unlike parsing a display
    /// string). Returns null for a missing/empty array so the caller skips the entry.
    /// </summary>
    private static IReadOnlyList<Key>? ReadChord(JsonArray? keys)
    {
        if (keys is null || keys.Count == 0) return null;
        var chord = new List<Key>(keys.Count);
        foreach (var node in keys)
        {
            if (node is null) return null;
            chord.Add(new Key((KeyCode)node.GetValue<uint>()));
        }
        return chord;
    }

    private void LogUnreadable(string path, JsonException e) =>
        _logger.LogWarning("{File} has an error at line {Line}, column {Column}: {Reason} Defaults are in use for the whole file.",
            path, e.LineNumber + 1, e.BytePositionInLine + 1, Reason(e));

    // .NET's message ends with the position, which the log already gives, and some with advice for the developer.
    private static string Reason(JsonException e) =>
        (e.Message.IndexOf(" LineNumber:", StringComparison.Ordinal) is var at and >= 0 ? e.Message[..at] : e.Message)
            .Replace(" Change the reader options.", "", StringComparison.Ordinal);

    private void EnsureDirExists(string filePath)
    {
        var dir = _fs.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);
    }
}
