using TuiCode.Abstractions;
using TuiCode.Workbench.TerminalIntegration;

namespace TuiCode.Tests;

public class WezTermIntegrationTests
{
    private const string Home = "/Users/test";
    private static readonly string ConfigDir = Path.Combine(Home, ".config", "wezterm");
    private static readonly string ModulePath = Path.Combine(ConfigDir, "tuicode.lua");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "wezterm.lua");

    private static (WezTermIntegration integration, MockFileSystem fs, FakeEnvironment env) Build(
        string? termProgram = "WezTerm")
    {
        var fs = new MockFileSystem();
        var env = new FakeEnvironment().SetFolder(Environment.SpecialFolder.UserProfile, Home);
        if (termProgram is not null)
            env.Set("TERM_PROGRAM", termProgram);
        return (new WezTermIntegration(fs, env), fs, env);
    }

    [Fact]
    public void Id_is_wezterm()
    {
        var (integration, _, _) = Build();
        Assert.Equal("wezterm", integration.Id);
    }

    [Fact]
    public void IsAvailable_returns_true_when_TERM_PROGRAM_is_WezTerm()
    {
        var (integration, _, _) = Build(termProgram: "WezTerm");
        Assert.True(integration.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_when_TERM_PROGRAM_is_iTerm_app()
    {
        var (integration, _, _) = Build(termProgram: "iTerm.app");
        Assert.False(integration.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_when_TERM_PROGRAM_is_unset()
    {
        var (integration, _, _) = Build(termProgram: null);
        Assert.False(integration.IsAvailable());
    }

    [Fact]
    public void GetStatus_returns_NotInstalled_when_nothing_present()
    {
        var (integration, _, _) = Build();
        Assert.Equal(TerminalIntegrationStatus.NotInstalled, integration.GetStatus());
    }

    [Fact]
    public void Install_writes_module_and_creates_wezterm_lua_when_absent()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.True(fs.File.Exists(ModulePath));
        Assert.True(fs.File.Exists(ConfigPath));
        var config = fs.File.ReadAllText(ConfigPath);
        Assert.Contains("-- TuiCode begin", config);
        Assert.Contains("-- TuiCode end", config);
        Assert.Contains("require", config);
    }

    [Fact]
    public void Install_creates_config_directory_if_missing()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.True(fs.Directory.Exists(ConfigDir));
    }

    [Fact]
    public void Install_appends_block_when_wezterm_lua_exists_without_it()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        var preexisting = "local wezterm = require 'wezterm'\nlocal config = wezterm.config_builder()\nreturn config\n";
        fs.File.WriteAllText(ConfigPath, preexisting);

        integration.Install();

        var after = fs.File.ReadAllText(ConfigPath);
        Assert.StartsWith(preexisting, after);
        Assert.Contains("-- TuiCode begin", after);
        Assert.Contains("-- TuiCode end", after);
    }

    [Fact]
    public void Install_replaces_existing_block_in_place_without_duplicating()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(
            ConfigPath,
            "local config = {}\n-- TuiCode begin\n-- old contents\n-- TuiCode end\nreturn config\n");

        integration.Install();

        var after = fs.File.ReadAllText(ConfigPath);
        Assert.Equal(1, CountOccurrences(after, "-- TuiCode begin"));
        Assert.Equal(1, CountOccurrences(after, "-- TuiCode end"));
        Assert.DoesNotContain("-- old contents", after);
        Assert.Contains("local config = {}", after);
        Assert.Contains("return config", after);
    }

    [Fact]
    public void Install_is_idempotent()
    {
        var (integration, fs, _) = Build();

        integration.Install();
        var firstModule = fs.File.ReadAllText(ModulePath);
        var firstConfig = fs.File.ReadAllText(ConfigPath);
        integration.Install();
        var secondModule = fs.File.ReadAllText(ModulePath);
        var secondConfig = fs.File.ReadAllText(ConfigPath);

        Assert.Equal(firstModule, secondModule);
        Assert.Equal(firstConfig, secondConfig);
    }

    [Fact]
    public void GetStatus_returns_Installed_after_Install()
    {
        var (integration, _, _) = Build();

        integration.Install();

        Assert.Equal(TerminalIntegrationStatus.Installed, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Stale_when_block_version_marker_is_older()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ModulePath, "-- TuiCodeIntegrationVersion: 1\n");
        fs.File.WriteAllText(
            ConfigPath,
            "-- TuiCode begin\n-- TuiCodeIntegrationVersion: 0\n-- TuiCode end\n");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Stale_when_version_marker_missing()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ModulePath, "-- module without version\n");
        fs.File.WriteAllText(ConfigPath, "-- TuiCode begin\n-- TuiCode end\n");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_NotInstalled_when_block_missing_from_config()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ModulePath, "-- TuiCodeIntegrationVersion: 1\n");
        fs.File.WriteAllText(ConfigPath, "local config = {}\nreturn config\n");

        Assert.Equal(TerminalIntegrationStatus.NotInstalled, integration.GetStatus());
    }

    [Fact]
    public void Uninstall_removes_module_file_and_strips_block_leaving_rest_intact()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        var preexisting = "local wezterm = require 'wezterm'\nlocal config = wezterm.config_builder()\nreturn config\n";
        fs.File.WriteAllText(ConfigPath, preexisting);
        integration.Install();

        integration.Uninstall();

        Assert.False(fs.File.Exists(ModulePath));
        var after = fs.File.ReadAllText(ConfigPath);
        Assert.DoesNotContain("-- TuiCode begin", after);
        Assert.DoesNotContain("-- TuiCode end", after);
        Assert.Contains("wezterm.config_builder()", after);
        Assert.Contains("return config", after);
    }

    [Fact]
    public void Uninstall_deletes_wezterm_lua_when_only_block_was_present()
    {
        var (integration, fs, _) = Build();
        integration.Install();

        integration.Uninstall();

        Assert.False(fs.File.Exists(ConfigPath));
    }

    [Fact]
    public void Uninstall_is_a_noop_when_nothing_installed()
    {
        var (integration, _, _) = Build();

        var ex = Record.Exception(() => integration.Uninstall());

        Assert.Null(ex);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
