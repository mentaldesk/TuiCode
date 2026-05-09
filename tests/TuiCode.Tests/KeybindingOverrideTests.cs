using System.Text.Json;
using TuiCode.Abstractions;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

[Collection("StaticConfiguration")]
public class KeybindingOverrideTests
{
    [Fact]
    public void Save_writes_the_override_array_when_the_list_is_non_empty()
    {
        using var _ = new SettingsFixture(theme: "Default", overrides:
        [
            new KeybindingOverride("Ctrl+Shift+K", "workbench.action.openSettings"),
            new KeybindingOverride("Ctrl+,", "-workbench.action.openSettings")
        ]);

        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);
        svc.Save();

        var json = fs.File.ReadAllText(ConfigPath(fs));
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("AppSettings").GetProperty("TuiCodeSettings.Keybindings");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("Ctrl+Shift+K", arr[0].GetProperty("Key").GetString());
        Assert.Equal("workbench.action.openSettings", arr[0].GetProperty("Command").GetString());
        Assert.Equal("Ctrl+,", arr[1].GetProperty("Key").GetString());
        Assert.Equal("-workbench.action.openSettings", arr[1].GetProperty("Command").GetString());
    }

    [Fact]
    public void Save_omits_the_override_array_when_there_are_no_overrides()
    {
        using var _ = new SettingsFixture(theme: "Default", overrides: []);

        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);
        svc.Save();

        var json = fs.File.ReadAllText(ConfigPath(fs));
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("AppSettings", out var _appSettings));
    }

    [Fact]
    public void SetKeybindingOverrides_replaces_the_list()
    {
        using var _ = new SettingsFixture(theme: "Default", overrides: []);

        var svc = new DefaultSettingsService(new MockFileSystem());
        svc.SetKeybindingOverrides([new KeybindingOverride("Ctrl+K", "save")]);

        Assert.Single(svc.KeybindingOverrides);
        Assert.Equal("Ctrl+K", svc.KeybindingOverrides[0].Key);
    }

    [Fact]
    public void KeybindingOverride_IsRemoval_strips_the_leading_dash()
    {
        var add = new KeybindingOverride("Ctrl+S", "save");
        Assert.False(add.IsRemoval);
        Assert.Equal("save", add.EffectiveCommand);

        var remove = new KeybindingOverride("Ctrl+S", "-save");
        Assert.True(remove.IsRemoval);
        Assert.Equal("save", remove.EffectiveCommand);
    }

    private static string ConfigPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.config.json");
    }

    /// <summary>Snapshot+restore the static <see cref="TuiCodeSettings"/> values for test isolation.</summary>
    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _previousTheme;
        private readonly KeybindingOverride[] _previousOverrides;
        public SettingsFixture(string theme, KeybindingOverride[] overrides)
        {
            _previousTheme = TuiCodeSettings.Theme;
            _previousOverrides = TuiCodeSettings.Keybindings;
            TuiCodeSettings.Theme = theme;
            TuiCodeSettings.Keybindings = overrides;
        }
        public void Dispose()
        {
            TuiCodeSettings.Theme = _previousTheme;
            TuiCodeSettings.Keybindings = _previousOverrides;
        }
    }
}
