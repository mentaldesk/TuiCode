using System.Text.Json;
using Terminal.Gui.Configuration;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Themes;

namespace TuiCode.Tests;

// Mutates ThemeManager.Theme (TG static state). The base joins the serialised
// "StaticConfiguration" collection and snapshot/restores the theme (issue #77).
public class DefaultSettingsServiceTests : StaticConfigurationTest
{
    [Fact]
    public void Save_writes_empty_object_when_theme_is_default()
    {
        ThemeManager.Theme = BundledThemes.Default;
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
        ThemeManager.Theme = BundledThemes.Daylight;
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var json = fs.File.ReadAllText(ConfigPath(fs));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(BundledThemes.Daylight, doc.RootElement.GetProperty("Theme").GetString());
    }

    [Fact]
    public void Save_creates_parent_directory_if_missing()
    {
        ThemeManager.Theme = BundledThemes.Daylight;
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);

        svc.Save();

        var dir = fs.Path.GetDirectoryName(ConfigPath(fs));
        Assert.True(fs.Directory.Exists(dir));
    }

    [Fact]
    public void Only_our_themes_are_offered_and_each_defines_every_scheme()
    {
        ConfigurationManager.Enable(ConfigLocations.None);
        try
        {
            ConfigurationManager.RuntimeConfig = BundledThemes.Config;
            ConfigurationManager.Load(ConfigLocations.LibraryResources | ConfigLocations.Runtime);

            Assert.Equal(
                [BundledThemes.Midnight, BundledThemes.Daylight, BundledThemes.TurboPascal, BundledThemes.ModernBorland],
                new DefaultSettingsService(new MockFileSystem()).AvailableThemes);

            foreach (var theme in BundledThemes.Names)
            {
                ThemeManager.Theme = theme;
                ConfigurationManager.Apply();
                foreach (var scheme in new[] { "Base", "Accent", "Dialog", "Menu", "Error", "Sidebar", "StatusBar" })
                    Assert.True(SchemeManager.TryGetScheme(scheme, out _), $"{theme} has no {scheme} scheme");
            }
        }
        finally
        {
            ThemeManager.Theme = "Default";
            ConfigurationManager.Disable(resetToHardCodedDefaults: true);
        }
    }

    [Theory]
    [InlineData("Default", BundledThemes.Midnight)]
    [InlineData("Dark", BundledThemes.Midnight)]
    [InlineData("Light", BundledThemes.Daylight)]
    [InlineData(BundledThemes.TurboPascal, BundledThemes.TurboPascal)]
    public void A_theme_we_do_not_ship_migrates_to_one_we_do(string saved, string expected)
    {
        Assert.Equal(expected, BundledThemes.Migrate(saved));
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

    [Fact]
    public void Grammar_associations_round_trip_through_their_own_file()
    {
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs);
        svc.SetGrammarAssociations(new Dictionary<string, string> { [".h"] = "cpp", ["Jenkinsfile"] = "groovy" });

        svc.Save();
        var reloaded = new DefaultSettingsService(fs);

        Assert.Equal("cpp", reloaded.GrammarAssociations[".H"]);
        Assert.Equal("groovy", reloaded.GrammarAssociations["Jenkinsfile"]);
    }

    [Fact]
    public void Saving_no_grammar_associations_removes_the_file()
    {
        var fs = new MockFileSystem();
        fs.AddFile(GrammarsPath(fs), new MockFileData("{ \".h\": \"cpp\" }"));
        var svc = new DefaultSettingsService(fs);

        svc.SetGrammarAssociations(new Dictionary<string, string>());
        svc.Save();

        Assert.False(fs.File.Exists(GrammarsPath(fs)));
    }

    [Theory]
    [InlineData("{ \".h\": ")]
    [InlineData("[ \".h\" ]")]
    public void Malformed_grammar_associations_load_as_empty(string json)
    {
        var fs = new MockFileSystem();
        fs.AddFile(GrammarsPath(fs), new MockFileData(json));

        Assert.Empty(new DefaultSettingsService(fs).GrammarAssociations);
    }

    [Fact]
    public void A_grammar_association_of_the_wrong_type_is_skipped_without_losing_the_rest()
    {
        var fs = new MockFileSystem();
        fs.AddFile(GrammarsPath(fs), new MockFileData("{ \".h\": 42, \".tfvars\": \"json\" }"));

        var associations = new DefaultSettingsService(fs).GrammarAssociations;

        Assert.Equal(".tfvars", Assert.Single(associations).Key);
    }

    private static string GrammarsPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.grammars.json");
    }

    private static string KeybindingsPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.keybindings.json");
    }
}
