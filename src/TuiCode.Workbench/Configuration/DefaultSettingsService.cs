using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Terminal.Gui.Configuration;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Configuration;

/// <summary>
/// DI-friendly adapter over TG's static <see cref="ConfigurationManager"/> /
/// <see cref="ThemeManager"/> + <see cref="TuiCodeSettings"/>. Tests substitute
/// an in-memory implementation; production code never touches the static surface directly.
///
/// <para>Theme persists via TG's <c>ConfigurationManager</c> in <c>~/.tui/TuiCode.config.json</c>.
/// Keybindings persist to a sibling file <c>~/.tui/TuiCode.keybindings.json</c> that we read and
/// write directly — TG's source-generated <c>JsonTypeInfo</c> only knows the types its built-in
/// scopes use, so any complex type we tried to put through it (records, arrays, even plain
/// <c>string[]</c>) silently failed to deserialize. A dedicated file dodges that and gives us a
/// clean, hand-readable JSON shape.</para>
/// </summary>
public sealed class DefaultSettingsService : ISettingsService
{
    private readonly IFileSystem _fs;
    private readonly string _themeConfigPath;
    private readonly string _keybindingsPath;
    private List<KeybindingOverride> _keybindings;

    public DefaultSettingsService(IFileSystem fs)
    {
        _fs = fs;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = _fs.Path.Combine(home, ".tui");
        _themeConfigPath = _fs.Path.Combine(dir, "TuiCode.config.json");
        _keybindingsPath = _fs.Path.Combine(dir, "TuiCode.keybindings.json");
        _keybindings = LoadKeybindings();
    }

    public string Theme
    {
        get => TuiCodeSettings.Theme;
        set
        {
            if (string.Equals(TuiCodeSettings.Theme, value, StringComparison.Ordinal)) return;
            TuiCodeSettings.Theme = value;
            ThemeManager.Theme = value;
            ConfigurationManager.Apply();
        }
    }

    // Allowlist of TG built-in themes we expose to the picker. The other built-ins
    // (TurboPascal 5, Green Phosphor, 8 bit, …) are demo themes that look poor in a
    // code editor; intersecting filters them out without crashing if a future TG
    // version renames or drops one. See issue #11 — we plan to ship our own themes
    // (including a faithful Turbo Pascal homage) rather than wrap TG's.
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

    private void SaveTheme()
    {
        var diff = new JsonObject();
        if (!string.Equals(TuiCodeSettings.Theme, TuiCodeSettings.DefaultTheme, StringComparison.Ordinal))
            diff[$"{nameof(TuiCodeSettings)}.{nameof(TuiCodeSettings.Theme)}"] = TuiCodeSettings.Theme;

        var root = new JsonObject();
        if (diff.Count > 0)
            root["AppSettings"] = diff;

        EnsureDirExists(_themeConfigPath);
        _fs.File.WriteAllText(_themeConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void SaveKeybindings()
    {
        if (_keybindings.Count == 0)
        {
            // No overrides — remove any stale file so a future load doesn't see deleted ones.
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
            // Malformed file: fall back to no overrides rather than crash on launch.
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
