using System.Text.Json;
using TuiCode.Abstractions;
using TuiCode.Workbench.TerminalIntegration;

namespace TuiCode.Tests;

public class Iterm2IntegrationTests
{
    private const string Home = "/Users/test";
    private static readonly string ProfileDir =
        Path.Combine(Home, "Library", "Application Support", "iTerm2", "DynamicProfiles");
    private static readonly string ProfilePath = Path.Combine(ProfileDir, "tuicode.json");

    private static (Iterm2Integration integration, MockFileSystem fs, FakeEnvironment env) Build(
        string? termProgram = "iTerm.app")
    {
        var fs = new MockFileSystem();
        var env = new FakeEnvironment().SetFolder(Environment.SpecialFolder.UserProfile, Home);
        if (termProgram is not null)
            env.Set("TERM_PROGRAM", termProgram);
        return (new Iterm2Integration(fs, env), fs, env);
    }

    [Fact]
    public void IsAvailable_returns_true_when_TERM_PROGRAM_is_iTerm_app()
    {
        var (integration, _, _) = Build(termProgram: "iTerm.app");
        Assert.True(integration.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_when_TERM_PROGRAM_is_something_else()
    {
        var (integration, _, _) = Build(termProgram: "WezTerm");
        Assert.False(integration.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_when_TERM_PROGRAM_is_unset()
    {
        var (integration, _, _) = Build(termProgram: null);
        Assert.False(integration.IsAvailable());
    }

    [Fact]
    public void GetStatus_returns_NotInstalled_when_profile_file_missing()
    {
        var (integration, _, _) = Build();
        Assert.Equal(TerminalIntegrationStatus.NotInstalled, integration.GetStatus());
    }

    [Fact]
    public void Install_writes_profile_to_iTerm2_DynamicProfiles_directory()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.True(fs.File.Exists(ProfilePath));
    }

    [Fact]
    public void Install_creates_the_DynamicProfiles_directory_if_missing()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        Assert.True(fs.Directory.Exists(ProfileDir));
    }

    [Fact]
    public void Install_writes_stable_GUID_so_reinstalls_overwrite_in_place()
    {
        var (integration, fs, _) = Build();

        integration.Install();

        using var doc = JsonDocument.Parse(fs.File.ReadAllText(ProfilePath));
        var guid = doc.RootElement.GetProperty("Profiles")[0].GetProperty("Guid").GetString();
        Assert.Equal(Iterm2Integration.ProfileGuid, guid);
    }

    [Fact]
    public void Install_binds_both_TuiCode_and_tuicode_so_Homebrew_lowercase_binary_matches()
    {
        // homebrew-tap#2: iTerm2's Bound Hosts matcher is case-sensitive, and Homebrew renames
        // the binary to lowercase. Profile must list both patterns to activate for both names.
        var (integration, fs, _) = Build();

        integration.Install();

        using var doc = JsonDocument.Parse(fs.File.ReadAllText(ProfilePath));
        var bound = doc.RootElement.GetProperty("Profiles")[0].GetProperty("Bound Hosts");
        var patterns = bound.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("&TuiCode*", patterns);
        Assert.Contains("&tuicode*", patterns);
    }

    [Fact]
    public void Install_is_idempotent()
    {
        var (integration, fs, _) = Build();

        integration.Install();
        var first = fs.File.ReadAllText(ProfilePath);
        integration.Install();
        var second = fs.File.ReadAllText(ProfilePath);

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
        fs.Directory.CreateDirectory(ProfileDir);
        fs.File.WriteAllText(ProfilePath, """
            { "Profiles": [ { "TuiCodeIntegrationVersion": 0, "Guid": "x" } ] }
            """);

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Stale_when_version_marker_is_missing()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ProfileDir);
        fs.File.WriteAllText(ProfilePath, """{ "Profiles": [ { "Guid": "x" } ] }""");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void GetStatus_returns_Stale_when_file_is_malformed_json()
    {
        var (integration, fs, _) = Build();
        fs.Directory.CreateDirectory(ProfileDir);
        fs.File.WriteAllText(ProfilePath, "not json at all");

        Assert.Equal(TerminalIntegrationStatus.Stale, integration.GetStatus());
    }

    [Fact]
    public void Uninstall_removes_the_profile_file()
    {
        var (integration, fs, _) = Build();
        integration.Install();

        integration.Uninstall();

        Assert.False(fs.File.Exists(ProfilePath));
    }

    [Fact]
    public void Uninstall_is_a_noop_when_nothing_installed()
    {
        var (integration, _, _) = Build();

        var ex = Record.Exception(() => integration.Uninstall());

        Assert.Null(ex);
    }

    [Fact]
    public void Id_is_iterm2()
    {
        var (integration, _, _) = Build();
        Assert.Equal("iterm2", integration.Id);
    }
}
