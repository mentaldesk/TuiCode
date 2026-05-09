using System.Text.Json;
using TuiCode.Abstractions;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

public class KeybindingOverrideTests
{
    [Fact]
    public void Save_writes_keybindings_to_a_dedicated_json_file()
    {
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);
        svc.SetKeybindingOverrides([
            new KeybindingOverride("Ctrl+Shift+K", "workbench.action.openSettings"),
            new KeybindingOverride("Ctrl+,", "-workbench.action.openSettings")
        ]);

        svc.Save();

        var json = fs.File.ReadAllText(KeybindingsPath(fs));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("Ctrl+Shift+K", doc.RootElement[0].GetProperty("Key").GetString());
        Assert.Equal("workbench.action.openSettings", doc.RootElement[0].GetProperty("Command").GetString());
        Assert.Equal("Ctrl+,", doc.RootElement[1].GetProperty("Key").GetString());
        Assert.Equal("-workbench.action.openSettings", doc.RootElement[1].GetProperty("Command").GetString());
    }

    [Fact]
    public void Save_removes_the_keybindings_file_when_there_are_no_overrides()
    {
        var fs = new MockFileSystem();
        // Pre-existing file with one entry — Save with empty overrides should delete it.
        fs.AddFile(KeybindingsPath(fs), new MockFileData("""[{"Key":"Ctrl+K","Command":"x"}]"""));

        var svc = new DefaultSettingsService(fs);
        svc.SetKeybindingOverrides([]);
        svc.Save();

        Assert.False(fs.File.Exists(KeybindingsPath(fs)));
    }

    [Fact]
    public void SetKeybindingOverrides_replaces_the_in_memory_list()
    {
        var svc = new DefaultSettingsService(new MockFileSystem());
        svc.SetKeybindingOverrides([new KeybindingOverride("Ctrl+K", "save")]);

        Assert.Single(svc.KeybindingOverrides);
        Assert.Equal("Ctrl+K", svc.KeybindingOverrides[0].Key);
    }

    [Fact]
    public void Constructor_loads_keybindings_from_disk()
    {
        // Regression test for "keybindings save but don't load on next launch": when the
        // overrides lived inside TG's ConfigurationManager (as a typed array OR string[]),
        // TG's source-generated JsonTypeInfo silently failed to deserialize them on boot.
        // Persisting to a dedicated file lets us round-trip a clean JSON shape.
        var fs = new MockFileSystem();
        fs.AddFile(KeybindingsPath(fs), new MockFileData("""
        [
          { "Key": "Ctrl+Shift+K", "Command": "workbench.action.openSettings" },
          { "Key": "Ctrl+,", "Command": "-workbench.action.openSettings" }
        ]
        """));

        var svc = new DefaultSettingsService(fs);

        Assert.Equal(2, svc.KeybindingOverrides.Count);
        Assert.Equal(new KeybindingOverride("Ctrl+Shift+K", "workbench.action.openSettings"), svc.KeybindingOverrides[0]);
        Assert.Equal(new KeybindingOverride("Ctrl+,", "-workbench.action.openSettings"), svc.KeybindingOverrides[1]);
        Assert.True(svc.KeybindingOverrides[1].IsRemoval);
    }

    [Fact]
    public void Constructor_falls_back_to_empty_list_when_keybindings_file_is_malformed()
    {
        var fs = new MockFileSystem();
        fs.AddFile(KeybindingsPath(fs), new MockFileData("not json at all"));

        var svc = new DefaultSettingsService(fs);

        Assert.Empty(svc.KeybindingOverrides);
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

    private static string KeybindingsPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.keybindings.json");
    }
}
