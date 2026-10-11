using System.Text.Json;
using Microsoft.Extensions.Logging;
using Terminal.Gui.Configuration;
using TuiCode.Abstractions;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Themes;

namespace TuiCode.Tests;

// Mutates ThemeManager.Theme (TG static state). The base joins the serialised
// "StaticConfiguration" collection and snapshot/restores the theme (issue #77).
public class DefaultSettingsServiceTests : StaticConfigurationTest
{
    private readonly IDisposable _themes = LoadBundledThemes();

    public override void Dispose()
    {
        _themes.Dispose();
        base.Dispose();
    }

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
        using var themes = LoadBundledThemes();
        Assert.Equal(
            [BundledThemes.Midnight, BundledThemes.Daylight, BundledThemes.TurboPascal, BundledThemes.ModernBorland],
            new DefaultSettingsService(new MockFileSystem()).AvailableThemes);

        foreach (var theme in BundledThemes.Names)
        {
            ThemeManager.Theme = theme;
            foreach (var scheme in new[] { "Base", "Accent", "Dialog", "Menu", "Error", "Warning", "Sidebar", "StatusBar" })
                Assert.True(SchemeManager.TryGetScheme(scheme, out _), $"{theme} has no {scheme} scheme");
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

    [Fact]
    public void File_icons_round_trip_through_the_settings_file_not_TGs()
    {
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs) { FileIcons = FileIconStyle.Emoji };

        svc.Save();

        Assert.Equal(FileIconStyle.Emoji, new DefaultSettingsService(fs).FileIcons);
        // TG ignores the whole config file if it holds a key it doesn't know.
        using var config = JsonDocument.Parse(fs.File.ReadAllText(ConfigPath(fs)));
        Assert.False(config.RootElement.TryGetProperty("FileIcons", out _));
    }

    [Fact]
    public void Saving_auto_file_icons_removes_the_settings_file()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{ \"FileIcons\": \"Off\" }"));
        var svc = new DefaultSettingsService(fs) { FileIcons = FileIconStyle.Auto };

        svc.Save();

        Assert.False(fs.File.Exists(SettingsPath(fs)));
    }

    [Theory]
    [InlineData("{ \"FileIcons\": ")]
    [InlineData("[ \"Off\" ]")]
    [InlineData("{ \"FileIcons\": 2 }")]
    [InlineData("{ \"FileIcons\": \"7\" }")]
    [InlineData("{ \"FileIcons\": \"Sparkly\" }")]
    public void Malformed_file_icons_load_as_auto(string json)
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData(json));

        Assert.Equal(FileIconStyle.Auto, new DefaultSettingsService(fs).FileIcons);
    }

    [Fact]
    public void Editor_settings_round_trip_through_the_settings_file()
    {
        var fs = new MockFileSystem();
        var editor = new EditorSettings { IndentSize = 2, InsertSpaces = false, LineEnding = LineEnding.CRLF, InsertFinalNewline = false, WordWrap = true };
        var svc = new DefaultSettingsService(fs) { Editor = editor };

        svc.Save();

        Assert.Equal(editor, new DefaultSettingsService(fs).Editor);
    }

    [Fact]
    public void Wrap_by_language_round_trips_including_turning_markdown_off()
    {
        var fs = new MockFileSystem();
        var editor = EditorSettings.Default with
        {
            WrapByLanguage = new Dictionary<string, bool> { ["markdown"] = false, ["python"] = true },
        };
        new DefaultSettingsService(fs) { Editor = editor }.Save();

        var loaded = new DefaultSettingsService(fs).Editor;

        Assert.Equal(editor, loaded);
        Assert.False(loaded.WrapsLanguage("markdown"));
    }

    [Fact]
    public void A_bad_wrap_by_language_entry_is_skipped_without_losing_the_others()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{ \"WrapByLanguage\": { \"python\": true, \"go\": \"yes\" } }"));

        var wrap = new DefaultSettingsService(fs).Editor.WrapByLanguage;

        Assert.Equal(new Dictionary<string, bool> { ["python"] = true }, wrap);
    }

    [Fact]
    public void Default_editor_settings_are_not_written()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{ \"IndentSize\": 2 }"));
        var svc = new DefaultSettingsService(fs) { Editor = EditorSettings.Default };

        svc.Save();

        Assert.False(fs.File.Exists(SettingsPath(fs)));
    }

    [Fact]
    public void Word_wrap_is_off_when_the_settings_file_does_not_mention_it()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{ \"IndentSize\": 2 }"));

        Assert.False(new DefaultSettingsService(fs).Editor.WordWrap);
    }

    [Fact]
    public void A_bad_editor_setting_loads_as_its_default_without_losing_the_others()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData(
            "{ \"IndentSize\": 40, \"InsertSpaces\": \"no\", \"LineEnding\": \"LF\", \"InsertFinalNewline\": false, \"FileIcons\": \"Off\" }"));

        var svc = new DefaultSettingsService(fs);

        Assert.Equal(EditorSettings.Default with { LineEnding = LineEnding.LF, InsertFinalNewline = false }, svc.Editor);
        Assert.Equal(FileIconStyle.Off, svc.FileIcons);
    }

    [Fact]
    public void Sidebar_width_round_trips_through_the_settings_file()
    {
        var fs = new MockFileSystem();
        var svc = new DefaultSettingsService(fs) { SidebarWidth = 45 };

        svc.Save();

        Assert.Equal(45, new DefaultSettingsService(fs).SidebarWidth);
    }

    [Fact]
    public void A_default_sidebar_width_is_not_written()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{ \"SidebarWidth\": 45 }"));
        var svc = new DefaultSettingsService(fs) { SidebarWidth = SidebarSizing.Default };

        svc.Save();

        Assert.False(fs.File.Exists(SettingsPath(fs)));
    }

    [Theory]
    [InlineData("{ \"SidebarWidth\": \"wide\" }")]
    [InlineData("{ \"SidebarWidth\": 4 }")]
    [InlineData("{ \"SidebarWidth\": ")]
    public void A_bad_sidebar_width_loads_as_the_default(string json)
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData(json));

        Assert.Equal(SidebarSizing.Default, new DefaultSettingsService(fs).SidebarWidth);
    }

    // A width past the spinner's maximum is legitimate on a wide terminal, so loading keeps it.
    [Fact]
    public void A_sidebar_width_above_the_spinner_maximum_survives()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{ \"SidebarWidth\": 120, \"IndentSize\": 2 }"));

        var svc = new DefaultSettingsService(fs);

        Assert.Equal(120, svc.SidebarWidth);
        Assert.Equal(2, svc.Editor.IndentSize);
    }

    [Fact]
    public void Language_servers_round_trip_through_the_settings_file()
    {
        var fs = new MockFileSystem();
        var chosen = new Dictionary<string, LanguageServerSetting>
        {
            ["go"] = new("gopls", []),
            ["python"] = new("pyright-langserver", ["--stdio"]),
            ["csharp"] = LanguageServerSetting.None,
        };
        new DefaultSettingsService(fs) { LanguageServers = chosen }.Save();

        var loaded = new DefaultSettingsService(fs).LanguageServers;

        Assert.Equal(chosen.OrderBy(c => c.Key), loaded.OrderBy(c => c.Key));
    }

    [Fact]
    public void No_language_servers_file_means_every_language_has_its_default()
    {
        Assert.Empty(new DefaultSettingsService(new MockFileSystem()).LanguageServers);
    }

    [Fact]
    public void A_bad_language_server_entry_is_skipped_and_the_rest_load()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("""
            { "LanguageServers": {
                "go": { "Command": "gopls" },
                "rust": { "Command": 7 },
                "python": { "Command": "pylsp", "Arguments": ["-v", 3] },
                "ruby": "solargraph",
                "lua": { "Command": "lua-language-server", "Arguments": ["--stdio"] } },
              "IndentSize": 2 }
            """));

        var svc = new DefaultSettingsService(fs);

        Assert.Equal(new LanguageServerSetting("gopls", []), svc.LanguageServers["go"]);
        Assert.Equal(new LanguageServerSetting("lua-language-server", ["--stdio"]), svc.LanguageServers["lua"]);
        Assert.Equal(2, svc.LanguageServers.Count);
        Assert.Equal(2, svc.Editor.IndentSize);
    }

    [Fact]
    public void A_settings_file_that_does_not_parse_logs_where_and_says_defaults_are_in_use()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{\n  \"IndentSize\": 2,\n}"));
        var logger = new ListLogger<DefaultSettingsService>();

        var svc = new DefaultSettingsService(fs, logger);

        Assert.True(svc.SettingsFileInvalid);
        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Equal(
            $"{SettingsPath(fs)} has an error at line 3, column 1: The JSON object contains a trailing comma at the end which is not supported in this mode. Defaults are in use for the whole file.",
            message);
    }

    [Fact]
    public void A_keybindings_file_that_does_not_parse_logs_a_warning_naming_it()
    {
        var fs = new MockFileSystem();
        fs.AddFile(KeybindingsPath(fs), new MockFileData("[ { \"Keys\": [1], "));
        var logger = new ListLogger<DefaultSettingsService>();

        var svc = new DefaultSettingsService(fs, logger);

        Assert.False(svc.SettingsFileInvalid);
        Assert.StartsWith($"{KeybindingsPath(fs)} has an error at line 1, column ", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public void A_grammar_associations_file_that_does_not_parse_logs_a_warning_naming_it()
    {
        var fs = new MockFileSystem();
        fs.AddFile(GrammarsPath(fs), new MockFileData("{ \".h\": cpp }"));
        var logger = new ListLogger<DefaultSettingsService>();

        var svc = new DefaultSettingsService(fs, logger);

        Assert.False(svc.SettingsFileInvalid);
        Assert.StartsWith($"{GrammarsPath(fs)} has an error at line 1, column 9: ", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public void Settings_files_that_parse_log_nothing()
    {
        var fs = new MockFileSystem();
        fs.AddFile(SettingsPath(fs), new MockFileData("{ \"IndentSize\": 2 }"));
        fs.AddFile(GrammarsPath(fs), new MockFileData("{ \".h\": \"cpp\" }"));
        var logger = new ListLogger<DefaultSettingsService>();

        var svc = new DefaultSettingsService(fs, logger);

        Assert.False(svc.SettingsFileInvalid);
        Assert.Empty(logger.Entries);
    }

    private static string SettingsPath(MockFileSystem fs)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fs.Path.Combine(home, ".tui", "TuiCode.settings.json");
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
