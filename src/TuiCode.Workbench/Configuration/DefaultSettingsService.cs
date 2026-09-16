using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Workbench.Themes;

namespace TuiCode.Workbench.Configuration;

/// <summary>
/// DI-friendly thin wrapper around TG's static <see cref="ConfigurationManager"/> /
/// <see cref="ThemeManager"/>. Tests substitute an in-memory implementation; production
/// code never touches the static surface directly.
///
/// <para>Theme persists via TG's native <c>ThemeManager.Theme</c>
/// (<c>[ConfigurationProperty(Scope = typeof(SettingsScope))]</c>) written as
/// <c>{"Theme": "Daylight"}</c> at the JSON root of <c>~/.tui/TuiCode.config.json</c>.
/// <see cref="Load"/> calls <c>ConfigurationManager.Enable</c> which reads the file and
/// applies the theme — no custom load logic needed. Saving still goes through us because
/// <c>ConfigurationManager</c> exposes no Save API.</para>
///
/// <para>Keybindings persist to a sibling file <c>~/.tui/TuiCode.keybindings.json</c>
/// that we read and write directly — TG's source-generated <c>JsonTypeInfo</c> only
/// knows the types its built-in scopes use, so complex types silently fail to deserialize.
/// A dedicated file dodges that and gives us a clean, hand-readable JSON shape.</para>
/// </summary>
public sealed class DefaultSettingsService : ISettingsService
{
    private readonly IFileSystem _fs;
    private readonly string _themeConfigPath;
    private readonly string _keybindingsPath;
    private readonly string _grammarsPath;
    private List<KeybindingOverride> _keybindings;
    private Dictionary<string, string> _grammarAssociations;

    public DefaultSettingsService(IFileSystem fs)
    {
        _fs = fs;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = _fs.Path.Combine(home, ".tui");
        _themeConfigPath = _fs.Path.Combine(dir, "TuiCode.config.json");
        _keybindingsPath = _fs.Path.Combine(dir, "TuiCode.keybindings.json");
        _grammarsPath = _fs.Path.Combine(dir, "TuiCode.grammars.json");
        _keybindings = LoadKeybindings();
        _grammarAssociations = LoadGrammarAssociations();
    }

    public string Theme
    {
        get => ThemeManager.Theme;
        set
        {
            if (string.Equals(ThemeManager.Theme, value, StringComparison.Ordinal)) return;
            ThemeManager.Theme = value;
            ConfigurationManager.Apply();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ThemeChanged;

    // TG's built-ins don't describe the editor's gutter or cursor, so we only offer our own.
    public IReadOnlyCollection<string> AvailableThemes =>
        BundledThemes.Names.Where(theme => ThemeManager.Themes?.ContainsKey(theme) ?? false).ToArray();

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

    public void Load()
    {
        ConfigurationManager.RuntimeConfig = BundledThemes.Config;
        ConfigurationManager.Enable(ConfigLocations.All);
        Theme = BundledThemes.Migrate(ThemeManager.Theme);
    }

    public void Save()
    {
        SaveTheme();
        SaveKeybindings();
        SaveGrammarAssociations();
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
        catch (JsonException)
        {
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
        catch (JsonException)
        {
            // The whole file is not valid JSON (hand-edited into a broken state) — ignore it.
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

    private void EnsureDirExists(string filePath)
    {
        var dir = _fs.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);
    }
}
