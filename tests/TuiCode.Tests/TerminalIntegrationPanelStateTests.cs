using TuiCode.Abstractions;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

public class TerminalIntegrationPanelStateTests
{
    [Fact]
    public void Build_shows_detected_terminal_and_install_button_when_not_installed()
    {
        var iterm = new FakeIntegration("iterm2", "iTerm2", available: true)
        {
            Status = TerminalIntegrationStatus.NotInstalled,
        };
        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, new FakeEnvironment());

        Assert.Same(iterm, state.Detected);
        Assert.Equal(TerminalIntegrationStatus.NotInstalled, state.Status);
        Assert.Contains(state.Lines, l => l.Contains("Detected terminal: iTerm2"));
        Assert.Contains(state.Lines, l => l.Contains("Not installed"));
        Assert.Equal(new[] { TerminalIntegrationAction.Install }, state.Actions);
    }

    [Fact]
    public void Build_shows_reinstall_and_remove_when_installed()
    {
        var iterm = new FakeIntegration("iterm2", "iTerm2", available: true)
        {
            Status = TerminalIntegrationStatus.Installed,
        };
        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, new FakeEnvironment());

        Assert.Contains(state.Lines, l => l.Contains("Status: Installed"));
        Assert.Equal(
            new[] { TerminalIntegrationAction.Reinstall, TerminalIntegrationAction.Remove },
            state.Actions);
    }

    [Fact]
    public void Build_shows_update_and_remove_when_stale()
    {
        var iterm = new FakeIntegration("iterm2", "iTerm2", available: true)
        {
            Status = TerminalIntegrationStatus.Stale,
        };
        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, new FakeEnvironment());

        Assert.Contains(state.Lines, l => l.Contains("older version"));
        Assert.Equal(
            new[] { TerminalIntegrationAction.Update, TerminalIntegrationAction.Remove },
            state.Actions);
    }

    [Fact]
    public void Build_lists_env_vars_when_no_terminal_detected()
    {
        var wez = new FakeIntegration("iterm2", "iTerm2", available: false);
        var env = new FakeEnvironment()
            .Set("TERM_PROGRAM", "WezTerm")
            .Set("TERM", "xterm-256color");

        var state = TerminalIntegrationPanelState.Build(new[] { wez }, env);

        Assert.Null(state.Detected);
        Assert.Null(state.Status);
        Assert.Empty(state.Actions);
        Assert.Contains(state.Lines, l => l.Contains("No integration available"));
        Assert.Contains(state.Lines, l => l.Contains("TERM_PROGRAM = WezTerm"));
        Assert.Contains(state.Lines, l => l.Contains("TERM         = xterm-256color"));
        Assert.Contains(state.Lines, l => l.Contains("Supported so far: iTerm2"));
    }

    [Fact]
    public void Build_shows_env_vars_as_unset_when_missing()
    {
        var iterm = new FakeIntegration("iterm2", "iTerm2", available: false);
        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, new FakeEnvironment());

        Assert.Contains(state.Lines, l => l.Contains("TERM_PROGRAM = (unset)"));
        Assert.Contains(state.Lines, l => l.Contains("TERM         = (unset)"));
    }

    [Fact]
    public void Build_picks_first_available_integration()
    {
        var a = new FakeIntegration("a", "A", available: false);
        var b = new FakeIntegration("b", "B", available: true);
        var c = new FakeIntegration("c", "C", available: true);

        var state = TerminalIntegrationPanelState.Build(new[] { a, b, c }, new FakeEnvironment());

        Assert.Same(b, state.Detected);
    }

    private sealed class FakeIntegration : ITerminalIntegration
    {
        private readonly bool _available;
        public FakeIntegration(string id, string displayName, bool available)
        {
            Id = id;
            DisplayName = displayName;
            _available = available;
        }
        public string Id { get; }
        public string DisplayName { get; }
        public TerminalIntegrationStatus Status { get; set; } = TerminalIntegrationStatus.NotInstalled;
        public bool IsAvailable() => _available;
        public TerminalIntegrationStatus GetStatus() => Status;
        public void Install() { }
        public void Uninstall() { }
    }
}
