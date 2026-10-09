using System.Text.RegularExpressions;
using TuiCode.Abstractions;
using TuiCode.Workbench.Settings;
using TuiCode.Workbench.TerminalIntegration;

namespace TuiCode.Tests;

public class KittyIntegrationTests
{
    private const string Home = "/Users/test";
    private static readonly string ConfigDir = Path.Combine(Home, ".config", "kitty");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "tuicode.conf");

    private static (KittyIntegration integration, MockFileSystem fs, FakeEnvironment env) Build()
    {
        var fs = new MockFileSystem();
        var env = new FakeEnvironment().SetFolder(Environment.SpecialFolder.UserProfile, Home);
        return (new KittyIntegration(fs, env), fs, env);
    }

    [Fact]
    public void Id_is_kitty()
    {
        var (integration, _, _) = Build();
        Assert.Equal("kitty", integration.Id);
    }

    [Fact]
    public void IsAvailable_returns_true_when_TERM_is_xterm_kitty()
    {
        var (integration, _, env) = Build();
        env.Set("TERM", "xterm-kitty");
        Assert.True(integration.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_true_when_KITTY_WINDOW_ID_is_set()
    {
        var (integration, _, env) = Build();
        env.Set("TERM", "tmux-256color").Set("KITTY_WINDOW_ID", "1");
        Assert.True(integration.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_outside_kitty()
    {
        var (integration, _, env) = Build();
        env.Set("TERM_PROGRAM", "WezTerm").Set("TERM", "xterm-256color");
        Assert.False(integration.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_on_non_macOS_even_under_kitty()
    {
        var (integration, _, env) = Build();
        env.Set("TERM", "xterm-kitty").Set("KITTY_WINDOW_ID", "1");
        env.SetIsMacOS(false);
        Assert.False(integration.IsAvailable());
    }

    [Fact]
    public void GetStatus_returns_NotInstalled_when_config_missing()
    {
        var (integration, _, _) = Build();
        Assert.Equal(TerminalIntegrationStatus.NotInstalled, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Installed_after_Install()
    {
        var (integration, _, _) = Build();

        integration.Install();

        Assert.Equal(TerminalIntegrationStatus.Installed, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Stale_when_version_marker_is_older()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ConfigPath, "# TuiCodeIntegrationVersion: 0\n");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Stale_for_the_previous_version()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ConfigPath, $"# TuiCodeIntegrationVersion: {KittyIntegration.CurrentVersion - 1}\n");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void Config_leaves_Cmd_V_to_kitty_so_it_pastes_the_local_clipboard_over_SSH()
    {
        Assert.DoesNotContain(" cmd+v ", KittyIntegration.Config);
    }

    [Fact]
    public void GetStatus_returns_Stale_when_version_marker_is_missing()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ConfigPath, "map cmd+c send_text all \\x03\n");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void Install_writes_config_into_kitty_config_dir()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.Equal(KittyIntegration.Config, fs.File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Install_writes_no_byte_order_mark()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.Equal((byte)'#', fs.File.ReadAllBytes(ConfigPath)[0]);
    }

    [Fact]
    public void Install_does_not_touch_kitty_conf()
    {
        var (integration, fs, _) = Build();
        var existing = "font_size 14\n";
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(Path.Combine(ConfigDir, "kitty.conf"), existing);

        integration.Install();
        integration.Uninstall();

        Assert.Equal(existing, fs.File.ReadAllText(Path.Combine(ConfigDir, "kitty.conf")));
    }

    [Fact]
    public void Reinstall_refreshes_a_stale_config()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ConfigPath, "# TuiCodeIntegrationVersion: 0\n");

        integration.Install();

        Assert.Equal(KittyIntegration.Config, fs.File.ReadAllText(ConfigPath));
        Assert.Equal(TerminalIntegrationStatus.Installed, integration.GetStatus());
    }

    [Fact]
    public void Uninstall_removes_config_file()
    {
        var (integration, fs, _) = Build();
        integration.Install();

        integration.Uninstall();

        Assert.False(fs.File.Exists(ConfigPath));
        Assert.Equal(TerminalIntegrationStatus.NotInstalled, integration.GetStatus());
    }

    [Fact]
    public void Uninstall_is_a_noop_when_nothing_installed()
    {
        var (integration, _, _) = Build();
        Assert.Null(Record.Exception(() => integration.Uninstall()));
    }

    [Fact]
    public void PostInstallInstructions_name_the_include_line_and_kitty_conf()
    {
        var (integration, _, _) = Build();
        Assert.Contains("include tuicode.conf", integration.PostInstallInstructions);
        Assert.Contains("~/.config/kitty/kitty.conf", integration.PostInstallInstructions);
        Assert.Contains("reload", integration.PostInstallInstructions);
    }

    [Fact]
    public void Config_gates_every_mapping_on_the_TuiCode_user_var()
    {
        var maps = KittyIntegration.Config.Split('\n').Where(l => l.StartsWith("map ")).ToArray();

        Assert.NotEmpty(maps);
        Assert.All(maps, l => Assert.StartsWith("map --when-focus-on var:TUICODE_ACTIVE=1 ", l));
        Assert.All(maps, l => Assert.Contains(" send_text all ", l));
    }

    [Fact]
    public void Config_maps_the_same_keys_to_the_same_bytes_as_WezTerm()
    {
        var wezterm = Regex.Matches(
                WezTermIntegration.ModuleLua,
                @"key = '(?<key>[^']+)',\s*mods = '(?<mods>[^']+)',\s*action = wezterm\.action\.SendString '(?<text>[^']+)'")
            .Select(m => (Key: KittyKey(m.Groups["mods"].Value, m.Groups["key"].Value), Text: m.Groups["text"].Value))
            .OrderBy(m => m.Key)
            .ToArray();
        var kitty = Regex.Matches(KittyIntegration.Config, @"^map \S+ \S+ (?<key>\S+) send_text all (?<text>\S+)$", RegexOptions.Multiline)
            .Select(m => (Key: m.Groups["key"].Value, Text: m.Groups["text"].Value))
            .OrderBy(m => m.Key)
            .ToArray();

        Assert.NotEmpty(wezterm);
        Assert.Equal(wezterm, kitty);
    }

    [Fact]
    public void Settings_panel_in_kitty_offers_install_then_shows_the_include_line()
    {
        var (kitty, fs, env) = InKitty();
        ITerminalIntegration[] all = [new Iterm2Integration(fs, env), new WezTermIntegration(fs, env), kitty];

        var before = TerminalIntegrationPanelState.Build(all, env);
        kitty.Install();
        var after = TerminalIntegrationPanelState.Build(all, env);

        Assert.Same(kitty, before.Detected);
        Assert.Contains("Detected terminal: kitty", before.Lines);
        Assert.Contains("Status: Not installed", before.Lines);
        Assert.Equal([TerminalIntegrationAction.Install], before.Actions);
        Assert.Contains("Status: Installed", after.Lines);
        Assert.Contains(after.Lines, l => l.Trim() == "include tuicode.conf");
        Assert.Contains(after.Lines, l => l.Contains("~/.config/kitty/kitty.conf"));
        Assert.Equal([TerminalIntegrationAction.Reinstall, TerminalIntegrationAction.Remove], after.Actions);
    }

    [Fact]
    public void Settings_panel_in_kitty_offers_update_for_an_older_config()
    {
        var (kitty, fs, env) = InKitty();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ConfigPath, "# TuiCodeIntegrationVersion: 0\n");

        var state = TerminalIntegrationPanelState.Build([kitty], env);

        Assert.Contains(state.Lines, l => l.Contains("older version"));
        Assert.Equal([TerminalIntegrationAction.Update, TerminalIntegrationAction.Remove], state.Actions);
    }

    [Fact]
    public void Install_flag_in_kitty_installs_the_kitty_integration()
    {
        var (kitty, fs, env) = InKitty();
        var cli = new TerminalIntegrationCli(
            [new Iterm2Integration(fs, env), new WezTermIntegration(fs, env), kitty], new StringWriter());

        var exit = cli.TryHandle(["--install-terminal-integration"]);

        Assert.Equal(0, exit);
        Assert.Equal(TerminalIntegrationStatus.Installed, kitty.GetStatus());
    }

    private static (KittyIntegration integration, MockFileSystem fs, FakeEnvironment env) InKitty()
    {
        var built = Build();
        built.env.Set("TERM", "xterm-kitty").Set("KITTY_WINDOW_ID", "1");
        return built;
    }

    private static string KittyKey(string mods, string key)
    {
        var name = key switch
        {
            "LeftArrow" => "left",
            "RightArrow" => "right",
            "UpArrow" => "up",
            "DownArrow" => "down",
            "PageUp" => "page_up",
            "PageDown" => "page_down",
            _ => key.ToLowerInvariant(),
        };
        var prefix = mods == "NONE" ? "" : string.Concat(mods.Split('|').Select(m => m.ToLowerInvariant() + "+"));
        return prefix + name;
    }
}
