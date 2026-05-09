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
/// </summary>
public sealed class DefaultSettingsService : ISettingsService
{
    private readonly IFileSystem _fs;
    private readonly string _configPath;

    public DefaultSettingsService(IFileSystem fs)
    {
        _fs = fs;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _configPath = _fs.Path.Combine(home, ".tui", "TuiCode.config.json");
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

    public IReadOnlyList<KeybindingOverride> KeybindingOverrides => TuiCodeSettings.Keybindings;

    public void SetKeybindingOverrides(IEnumerable<KeybindingOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        TuiCodeSettings.Keybindings = overrides.ToArray();
    }

    public void Save()
    {
        var diff = new JsonObject();

        if (!string.Equals(TuiCodeSettings.Theme, TuiCodeSettings.DefaultTheme, StringComparison.Ordinal))
            diff[$"{nameof(TuiCodeSettings)}.{nameof(TuiCodeSettings.Theme)}"] = TuiCodeSettings.Theme;

        if (TuiCodeSettings.Keybindings.Length > 0)
        {
            var arr = new JsonArray();
            foreach (var o in TuiCodeSettings.Keybindings)
                arr.Add(new JsonObject { ["Key"] = o.Key, ["Command"] = o.Command });
            diff[$"{nameof(TuiCodeSettings)}.{nameof(TuiCodeSettings.Keybindings)}"] = arr;
        }

        var root = new JsonObject();
        if (diff.Count > 0)
            root["AppSettings"] = diff;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var dir = _fs.Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);
        _fs.File.WriteAllText(_configPath, json);
    }
}
