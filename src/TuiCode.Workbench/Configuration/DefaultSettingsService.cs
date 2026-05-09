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

    public IReadOnlyCollection<string> AvailableThemes =>
        ThemeManager.Themes?.Keys.ToArray() ?? Array.Empty<string>();

    public void Save()
    {
        var diff = new JsonObject();

        if (!string.Equals(TuiCodeSettings.Theme, TuiCodeSettings.DefaultTheme, StringComparison.Ordinal))
            diff[$"{nameof(TuiCodeSettings)}.{nameof(TuiCodeSettings.Theme)}"] = TuiCodeSettings.Theme;

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
