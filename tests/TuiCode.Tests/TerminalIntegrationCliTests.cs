using TuiCode.Abstractions;
using TuiCode.Workbench.TerminalIntegration;

namespace TuiCode.Tests;

public class TerminalIntegrationCliTests
{
    private static (TerminalIntegrationCli cli, StringWriter @out, FakeIntegration[] integrations)
        Build(params FakeIntegration[] integrations)
    {
        var @out = new StringWriter();
        return (new TerminalIntegrationCli(integrations, @out), @out, integrations);
    }

    [Fact]
    public void TryHandle_returns_null_when_no_recognised_flag_is_present()
    {
        var (cli, _, _) = Build(new FakeIntegration("iterm2"));

        Assert.Null(cli.TryHandle(new[] { "--smoke", "some.txt" }));
    }

    [Fact]
    public void TryHandle_returns_null_for_empty_args()
    {
        var (cli, _, _) = Build(new FakeIntegration("iterm2"));

        Assert.Null(cli.TryHandle(Array.Empty<string>()));
    }

    [Fact]
    public void List_prints_a_line_per_integration_and_exits_zero()
    {
        var (cli, @out, _) = Build(
            new FakeIntegration("iterm2", displayName: "iTerm2", available: true),
            new FakeIntegration("wezterm", displayName: "WezTerm", available: false));

        var exit = cli.TryHandle(new[] { "--list-terminal-integrations" });

        Assert.Equal(0, exit);
        var output = @out.ToString();
        Assert.Contains("iterm2", output);
        Assert.Contains("wezterm", output);
        Assert.Contains("available", output);
        Assert.Contains("not detected", output);
    }

    [Fact]
    public void Install_with_explicit_id_calls_install_on_that_integration()
    {
        var iterm = new FakeIntegration("iterm2", available: false);
        var (cli, _, _) = Build(iterm);

        var exit = cli.TryHandle(new[] { "--install-terminal-integration=iterm2" });

        Assert.Equal(0, exit);
        Assert.Equal(1, iterm.InstallCount);
    }

    [Fact]
    public void Install_id_match_is_case_insensitive()
    {
        var iterm = new FakeIntegration("iterm2", available: false);
        var (cli, _, _) = Build(iterm);

        cli.TryHandle(new[] { "--install-terminal-integration=ITERM2" });

        Assert.Equal(1, iterm.InstallCount);
    }

    [Fact]
    public void Install_with_no_id_auto_detects_via_IsAvailable()
    {
        var wez = new FakeIntegration("wezterm", available: false);
        var iterm = new FakeIntegration("iterm2", available: true);
        var (cli, _, _) = Build(wez, iterm);

        cli.TryHandle(new[] { "--install-terminal-integration" });

        Assert.Equal(0, wez.InstallCount);
        Assert.Equal(1, iterm.InstallCount);
    }

    [Fact]
    public void Install_with_no_id_errors_when_no_terminal_is_detected()
    {
        var iterm = new FakeIntegration("iterm2", available: false);
        var (cli, @out, _) = Build(iterm);

        var exit = cli.TryHandle(new[] { "--install-terminal-integration" });

        Assert.Equal(1, exit);
        Assert.Equal(0, iterm.InstallCount);
        Assert.Contains("Could not detect", @out.ToString());
    }

    [Fact]
    public void Install_with_unknown_id_errors_and_lists_known_ids()
    {
        var iterm = new FakeIntegration("iterm2", available: false);
        var (cli, @out, _) = Build(iterm);

        var exit = cli.TryHandle(new[] { "--install-terminal-integration=wezterm" });

        Assert.Equal(1, exit);
        Assert.Equal(0, iterm.InstallCount);
        Assert.Contains("Unknown", @out.ToString());
        Assert.Contains("iterm2", @out.ToString());
    }

    [Fact]
    public void Uninstall_calls_uninstall_on_resolved_integration()
    {
        var iterm = new FakeIntegration("iterm2", available: true);
        var (cli, _, _) = Build(iterm);

        var exit = cli.TryHandle(new[] { "--uninstall-terminal-integration" });

        Assert.Equal(0, exit);
        Assert.Equal(1, iterm.UninstallCount);
    }

    [Fact]
    public void Check_returns_0_when_installed()
    {
        var iterm = new FakeIntegration("iterm2", available: true)
        {
            Status = TerminalIntegrationStatus.Installed,
        };
        var (cli, _, _) = Build(iterm);

        Assert.Equal(0, cli.TryHandle(new[] { "--check-terminal-integration" }));
    }

    [Fact]
    public void Check_returns_1_when_stale()
    {
        var iterm = new FakeIntegration("iterm2", available: true)
        {
            Status = TerminalIntegrationStatus.Stale,
        };
        var (cli, _, _) = Build(iterm);

        Assert.Equal(1, cli.TryHandle(new[] { "--check-terminal-integration" }));
    }

    [Fact]
    public void Check_returns_2_when_not_installed()
    {
        var iterm = new FakeIntegration("iterm2", available: true)
        {
            Status = TerminalIntegrationStatus.NotInstalled,
        };
        var (cli, _, _) = Build(iterm);

        Assert.Equal(2, cli.TryHandle(new[] { "--check-terminal-integration" }));
    }

    private sealed class FakeIntegration : ITerminalIntegration
    {
        private readonly bool _available;
        public FakeIntegration(string id, string? displayName = null, bool available = false)
        {
            Id = id;
            DisplayName = displayName ?? id;
            _available = available;
        }
        public string Id { get; }
        public string DisplayName { get; }
        public TerminalIntegrationStatus Status { get; set; } = TerminalIntegrationStatus.NotInstalled;
        public int InstallCount { get; private set; }
        public int UninstallCount { get; private set; }
        public bool IsAvailable() => _available;
        public TerminalIntegrationStatus GetStatus() => Status;
        public void Install() => InstallCount++;
        public void Uninstall() => UninstallCount++;
    }
}
