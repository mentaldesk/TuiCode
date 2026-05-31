using TuiCode.Abstractions;
using TuiCode.Workbench.TerminalIntegration;

namespace TuiCode.Tests;

public class WezTermIntegrationTests
{
    private const string Home = "/Users/test";
    private static readonly string ConfigDir = Path.Combine(Home, ".config", "wezterm");
    private static readonly string ModulePath = Path.Combine(ConfigDir, "tuicode.lua");

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
    public void GetStatus_returns_NotInstalled_when_module_missing()
    {
        var (integration, _, _) = Build();
        Assert.Equal(TerminalIntegrationStatus.NotInstalled, integration.GetStatus());
    }

    [Fact]
    public void Install_writes_module_into_wezterm_config_dir()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.True(fs.File.Exists(ModulePath));
    }

    [Fact]
    public void Install_creates_config_directory_if_missing()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.True(fs.Directory.Exists(ConfigDir));
    }

    [Fact]
    public void Install_does_not_touch_wezterm_lua()
    {
        // The user's wezterm.lua is treated as personal config. The activation snippet is
        // surfaced via PostInstallInstructions for the user to paste themselves.
        var (integration, fs, _) = Build();
        var existingConfig = "local wezterm = require 'wezterm'\nreturn {}\n";
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(Path.Combine(ConfigDir, "wezterm.lua"), existingConfig);

        integration.Install();

        Assert.Equal(existingConfig, fs.File.ReadAllText(Path.Combine(ConfigDir, "wezterm.lua")));
    }

    [Fact]
    public void Install_is_idempotent()
    {
        var (integration, fs, _) = Build();

        integration.Install();
        var first = fs.File.ReadAllText(ModulePath);
        integration.Install();
        var second = fs.File.ReadAllText(ModulePath);

        Assert.Equal(first, second);
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
        fs.File.WriteAllText(ModulePath, "-- TuiCodeIntegrationVersion: 0\nreturn {}\n");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Stale_when_version_marker_is_missing()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(ModulePath, "-- no marker here\nreturn {}\n");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void Uninstall_removes_module_file()
    {
        var (integration, fs, _) = Build();
        integration.Install();

        integration.Uninstall();

        Assert.False(fs.File.Exists(ModulePath));
    }

    [Fact]
    public void Uninstall_does_not_touch_wezterm_lua()
    {
        var (integration, fs, _) = Build();
        var existingConfig = "local wezterm = require 'wezterm'\nreturn {}\n";
        fs.Directory.CreateDirectory(ConfigDir);
        fs.File.WriteAllText(Path.Combine(ConfigDir, "wezterm.lua"), existingConfig);
        integration.Install();

        integration.Uninstall();

        Assert.Equal(existingConfig, fs.File.ReadAllText(Path.Combine(ConfigDir, "wezterm.lua")));
    }

    [Fact]
    public void Uninstall_is_a_noop_when_nothing_installed()
    {
        var (integration, _, _) = Build();

        var ex = Record.Exception(() => integration.Uninstall());

        Assert.Null(ex);
    }

    [Fact]
    public void PostInstallInstructions_contains_activation_snippet()
    {
        var (integration, _, _) = Build();
        Assert.NotNull(integration.PostInstallInstructions);
        Assert.Contains(WezTermIntegration.ActivationSnippet, integration.PostInstallInstructions);
        Assert.Contains("wezterm.lua", integration.PostInstallInstructions);
    }
}
