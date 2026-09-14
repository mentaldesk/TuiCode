using System.Text.Json;
using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Workbench.Configuration;

namespace TuiCode.Tests;

public class KeybindingOverrideTests
{
    [Fact]
    public void Save_writes_keybindings_by_keycode_with_a_decorative_label()
    {
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);
        var add = TestKeys.Chord("Ctrl+Shift+K");
        var remove = TestKeys.Chord("Ctrl+,");
        svc.SetKeybindingOverrides([
            new KeybindingOverride(add, "workbench.action.openSettings"),
            new KeybindingOverride(remove, "-workbench.action.openSettings")
        ]);

        svc.Save();

        var json = fs.File.ReadAllText(KeybindingsPath(fs));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetArrayLength());

        // Identity is the keycode array; "Label" is just for a human reading the file.
        Assert.Equal((uint)add[0].KeyCode, root[0].GetProperty("Keys")[0].GetUInt32());
        Assert.Equal("Ctrl+Shift+K", root[0].GetProperty("Label").GetString());
        Assert.Equal("workbench.action.openSettings", root[0].GetProperty("Command").GetString());

        Assert.Equal((uint)remove[0].KeyCode, root[1].GetProperty("Keys")[0].GetUInt32());
        Assert.Equal("-workbench.action.openSettings", root[1].GetProperty("Command").GetString());
    }

    [Fact]
    public void Save_removes_the_keybindings_file_when_there_are_no_overrides()
    {
        var fs = new MockFileSystem();
        // Pre-existing file with one entry — Save with empty overrides should delete it.
        fs.AddFile(KeybindingsPath(fs), new MockFileData("""[{"Keys":[123],"Command":"x"}]"""));

        var svc = new DefaultSettingsService(fs);
        svc.SetKeybindingOverrides([]);
        svc.Save();

        Assert.False(fs.File.Exists(KeybindingsPath(fs)));
    }

    [Fact]
    public void SetKeybindingOverrides_replaces_the_in_memory_list()
    {
        var svc = new DefaultSettingsService(new MockFileSystem());
        var chord = TestKeys.Chord("Ctrl+K");
        svc.SetKeybindingOverrides([new KeybindingOverride(chord, "save")]);

        Assert.Single(svc.KeybindingOverrides);
        Assert.Equal(KeyChord.Canonical(chord), svc.KeybindingOverrides[0].CanonicalId);
    }

    [Fact]
    public void Constructor_loads_keybindings_from_disk()
    {
        var fs = new MockFileSystem();
        var settings = TestKeys.Chord("Ctrl+Shift+K");
        var comma = TestKeys.Chord("Ctrl+,");
        fs.AddFile(KeybindingsPath(fs), new MockFileData(
            $$"""
            [
              { "Keys": [{{(uint)settings[0].KeyCode}}], "Label": "Ctrl+Shift+K", "Command": "workbench.action.openSettings" },
              { "Keys": [{{(uint)comma[0].KeyCode}}], "Label": "Ctrl+,", "Command": "-workbench.action.openSettings" }
            ]
            """));

        var svc = new DefaultSettingsService(fs);

        Assert.Equal(2, svc.KeybindingOverrides.Count);
        Assert.Equal(new KeybindingOverride(settings, "workbench.action.openSettings"), svc.KeybindingOverrides[0]);
        Assert.Equal(new KeybindingOverride(comma, "-workbench.action.openSettings"), svc.KeybindingOverrides[1]);
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

    // #89 acceptance: Ctrl+Alt+Shift++ stringifies to "Ctrl+Alt+Shift++", which Key.TryParse can't
    // read back (a TG round-trip bug). Persisting the raw keycode instead of the display string means
    // such a chord still saves and reloads intact.
    [Fact]
    public void A_chord_whose_display_string_cannot_be_parsed_round_trips_through_save_and_load()
    {
        var fs = new MockFileSystem();
        var plus = new[] { new Key(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask | (KeyCode)'+') };

        var svc = new DefaultSettingsService(fs);
        svc.SetKeybindingOverrides([new KeybindingOverride(plus, "editor.nextCursorPosition")]);
        svc.Save();

        var reloaded = new DefaultSettingsService(fs);

        var only = Assert.Single(reloaded.KeybindingOverrides);
        Assert.Equal(KeyChord.Canonical(plus), only.CanonicalId);
        Assert.Equal("editor.nextCursorPosition", only.Command);
    }

    [Fact]
    public void KeybindingOverride_IsRemoval_strips_the_leading_dash()
    {
        var chord = TestKeys.Chord("Ctrl+S");
        var add = new KeybindingOverride(chord, "save");
        Assert.False(add.IsRemoval);
        Assert.Equal("save", add.EffectiveCommand);

        var remove = new KeybindingOverride(chord, "-save");
        Assert.True(remove.IsRemoval);
        Assert.Equal("save", remove.EffectiveCommand);
    }

    private static string KeybindingsPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.keybindings.json");
    }
}
