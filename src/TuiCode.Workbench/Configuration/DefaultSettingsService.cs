using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terminal.Gui.Configuration;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Configuration;

/// <summary>
/// DI-friendly adapter over TG's static <see cref="ConfigurationManager"/> /
/// <see cref="ThemeManager"/>. Tests substitute an in-memory implementation;
/// production code never touches the static surface directly.
///
/// <para>Theme persists via TG's native <c>ThemeManager.Theme</c>
/// (<c>[ConfigurationProperty(Scope = typeof(SettingsScope))]</c>) written as
/// <c>{"Theme": "Dark"}</c> in <c>~/.tui/TuiCode.config.json</c>.
/// <c>ConfigurationManager.Enable()</c> reads this on startup and sets
/// <c>ThemeManager.Theme</c> automatically; no custom static wrapper is needed.</para>
///
/// <para>Keybindings persist to a sibling file <c>~/.tui/TuiCode.keybindings.json</c>
/// that we read and write directly — TG's source-generated <c>JsonTypeInfo</c> only
/// knows the types its built-in scopes use, so complex types silently fail to deserialize.
/// A dedicated file dodges that and gives us a clean, hand-readable JSON shape.</para>
/// </summary>
public sealed class DefaultSettingsService : ISettingsService
{
    private const string DefaultTheme = "Default";

    private readonly IFileSystem _fs;
    private readonly string _themeConfigPath;
    private readonly string _keybindingsPath;
    private string _theme;
    private List<KeybindingOverride> _keybindings;

    public DefaultSettingsService(IFileSystem fs)
    {
        _fs = fs;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = _fs.Path.Combine(home, ".tui");
        _themeConfigPath = _fs.Path.Combine(dir, "TuiCode.config.json");
        _keybindingsPath = _fs.Path.Combine(dir, "TuiCode.keybindings.json");
        _theme = LoadTheme();
        _keybindings = LoadKeybindings();
    }

    public string Theme
    {
        get => _theme;
        set
        {
            if (string.Equals(_theme, value, StringComparison.Ordinal)) return;
            _theme = value;
            ThemeManager.Theme = value;
            ConfigurationManager.Apply();
        }
    }

    // Allowlist of TG built-in themes we expose to the picker. The other built-ins
    // (TurboPascal 5, Green Phosphor, 8 bit, …) are demo themes that look poor in a
    // code editor; intersecting filters them out without crashing if a future TG
    // version renames or drops one. See issue #11.
    private static readonly string[] AllowedThemes = ["Default", "Dark", "Light"];

    public IReadOnlyCollection<string> AvailableThemes =>
        (ThemeManager.Themes?.Keys ?? Enumerable.Empty<string>())
            .Intersect(AllowedThemes, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<KeybindingOverride> KeybindingOverrides => _keybindings;

    public void SetKeybindingOverrides(IEnumerable<KeybindingOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        _keybindings = overrides.ToList();
    }

    public void Save()
    {
        SaveTheme();
        SaveKeybindings();
    }

    private string LoadTheme()
    {
        if (!_fs.File.Exists(_themeConfigPath)) return DefaultTheme;
        try
        {
            var json = _fs.File.ReadAllText(_themeConfigPath);
            var node = JsonNode.Parse(json);
            var theme = node?["Theme"]?.GetValue<string>();
            return !string.IsNullOrEmpty(theme) ? theme : DefaultTheme;
        }
        catch
        {
            return DefaultTheme;
        }
    }

    private void SaveTheme()
    {
        var root = new JsonObject();
        if (!string.Equals(_theme, DefaultTheme, StringComparison.Ordinal))
            root["Theme"] = _theme;

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

        var arr = new JsonArray();
        foreach (var o in _keybindings)
            arr.Add(new JsonObject { ["Key"] = o.Key, ["Command"] = o.Command });

        EnsureDirExists(_keybindingsPath);
        _fs.File.WriteAllText(_keybindingsPath, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private List<KeybindingOverride> LoadKeybindings()
    {
        if (!_fs.File.Exists(_keybindingsPath)) return new List<KeybindingOverride>();

        try
        {
            var json = _fs.File.ReadAllText(_keybindingsPath);
            var node = JsonNode.Parse(json);
            if (node is not JsonArray arr) return new List<KeybindingOverride>();

            var result = new List<KeybindingOverride>(arr.Count);
            foreach (var item in arr)
            {
                if (item is not JsonObject obj) continue;
                var key = obj["Key"]?.GetValue<string>();
                var command = obj["Command"]?.GetValue<string>();
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(command)) continue;
                result.Add(new KeybindingOverride(key, command));
            }
            return result;
        }
        catch
        {
            return new List<KeybindingOverride>();
        }
    }

    private void EnsureDirExists(string filePath)
    {
        var dir = _fs.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);
    }
}
