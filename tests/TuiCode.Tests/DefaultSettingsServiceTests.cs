using System.Text.Json;
using Terminal.Gui.Configuration;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

// Mutates ThemeManager.Theme (TG static state). The base joins the serialised
// "StaticConfiguration" collection and snapshot/restores the theme (issue #77).
public class DefaultSettingsServiceTests : StaticConfigurationTest
{
    [Fact]
    public void Save_writes_empty_object_when_theme_is_default()
    {
        ThemeManager.Theme = "Default";
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var path = ConfigPath(fs);
        Assert.True(fs.File.Exists(path));
        var json = fs.File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        Assert.Empty(doc.RootElement.EnumerateObject());
    }

    [Fact]
    public void Save_writes_theme_in_TG_native_format_when_non_default()
    {
        ThemeManager.Theme = "Dark";
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var json = fs.File.ReadAllText(ConfigPath(fs));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Dark", doc.RootElement.GetProperty("Theme").GetString());
    }

    [Fact]
    public void Save_creates_parent_directory_if_missing()
    {
        ThemeManager.Theme = "Dark";
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var dir = fs.Path.GetDirectoryName(ConfigPath(fs));
        Assert.True(fs.Directory.Exists(dir));
    }

    // #90: hand-editing the keybindings file into broken JSON must not throw at construction —
    // the service just loads no overrides and the app boots on defaults.
    [Fact]
    public void Malformed_keybindings_json_loads_as_empty_without_throwing()
    {
        var fs = new MockFileSystem();
        fs.AddFile(KeybindingsPath(fs), new MockFileData("[ { \"Key\": \"Ctrl+K\", "));

        var svc = new DefaultSettingsService(fs);

        Assert.Empty(svc.KeybindingOverrides);
    }

    // #90: one bad entry (here a keycode array holding a non-number) shouldn't take the whole file
    // down with it — the valid bindings around it still load.
    [Fact]
    public void A_single_malformed_entry_does_not_discard_the_valid_bindings()
    {
        var fs = new MockFileSystem();
        var ctrlK = TestKeys.Chord("Ctrl+K");
        fs.AddFile(KeybindingsPath(fs), new MockFileData(
            $$"""
            [
              { "Keys": [{{(uint)ctrlK[0].KeyCode}}], "Command": "workbench.action.saveActiveEditor" },
              { "Keys": ["not-a-number"], "Command": "workbench.action.quit" }
            ]
            """));

        var svc = new DefaultSettingsService(fs);

        var only = Assert.Single(svc.KeybindingOverrides);
        Assert.Equal(TuiCode.Abstractions.KeyChord.Canonical(ctrlK), only.CanonicalId);
        Assert.Equal("workbench.action.saveActiveEditor", only.Command);
    }

    // #89: persistence moved from a display "Key" string to a "Keys" keycode array. Pre-#89 files
    // have no "Keys", so their entries are skipped — the app boots on defaults and a later edit
    // re-saves any new bindings in the keycode format. (Old custom bindings are dropped, by design.)
    [Fact]
    public void Pre_keycode_format_entries_are_dropped_and_the_app_boots_on_defaults()
    {
        var fs = new MockFileSystem();
        fs.AddFile(KeybindingsPath(fs), new MockFileData(
            """
            [ { "Key": "Ctrl+Shift+K", "Command": "workbench.action.openSettings" } ]
            """));

        var svc = new DefaultSettingsService(fs);

        Assert.Empty(svc.KeybindingOverrides);
    }

    private static string ConfigPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.config.json");
    }

    private static string KeybindingsPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.keybindings.json");
    }
}
