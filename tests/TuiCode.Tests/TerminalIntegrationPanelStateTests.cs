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

    [Theory]
    [InlineData("TERM", "alacritty", "Alacritty", "Alacritty's key bindings apply to every program,")]
    [InlineData("ALACRITTY_WINDOW_ID", "1", "Alacritty", "Alacritty's key bindings apply to every program,")]
    [InlineData("TERM_PROGRAM", "ghostty", "Ghostty", "Ghostty has no key bindings for one program only,")]
    [InlineData("GHOSTTY_RESOURCES_DIR", "/Applications/Ghostty.app", "Ghostty", "Ghostty has no key bindings for one program only,")]
    [InlineData("TERM_PROGRAM", "Apple_Terminal", "Terminal.app", "Terminal.app doesn't pass on the modifiers")]
    public void Build_says_what_wont_work_in_a_terminal_TuiCode_cant_integrate_with(
        string variable, string value, string name, string reason)
    {
        var env = new FakeEnvironment().Set(variable, value);

        var state = TerminalIntegrationPanelState.Build(RealIntegrations(env), env);

        Assert.Null(state.Detected);
        Assert.Empty(state.Actions);
        Assert.Equal($"Detected terminal: {name}", state.Lines[0]);
        Assert.Contains("Cmd+C and Cmd+X can't reach TuiCode here:", state.Lines);
        Assert.Contains(reason, state.Lines);
        Assert.Contains("Inside TuiCode, use Ctrl+C and Ctrl+X instead.", state.Lines);
        Assert.Contains("Cmd+V pastes as usual.", state.Lines);
        Assert.Contains("For Cmd shortcuts, use iTerm2, WezTerm or kitty.", state.Lines);
        Assert.DoesNotContain(state.Lines, l => l.Contains("No integration available"));
    }

    [Fact]
    public void Build_still_says_no_integration_is_available_in_an_unknown_terminal()
    {
        var env = new FakeEnvironment().Set("TERM_PROGRAM", "Hyper");

        var state = TerminalIntegrationPanelState.Build(RealIntegrations(env), env);

        Assert.Empty(state.Actions);
        Assert.Equal("No integration available for this terminal yet.", state.Lines[0]);
        Assert.Contains("Supported so far: iTerm2, WezTerm, kitty.", state.Lines);
    }

    [Fact]
    public void Build_leaves_Linux_terminals_to_the_unknown_terminal_text()
    {
        var env = new FakeEnvironment().SetIsMacOS(false).Set("TERM", "alacritty");

        var state = TerminalIntegrationPanelState.Build(RealIntegrations(env), env);

        Assert.Equal("No integration available for this terminal yet.", state.Lines[0]);
    }

    private static ITerminalIntegration[] RealIntegrations(IEnvironment env)
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        return
        [
            new TuiCode.Workbench.TerminalIntegration.Iterm2Integration(fs, env),
            new TuiCode.Workbench.TerminalIntegration.WezTermIntegration(fs, env),
            new TuiCode.Workbench.TerminalIntegration.KittyIntegration(fs, env),
        ];
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

    [Fact]
    public void Build_says_what_the_terminal_needs_for_copy_over_SSH_whatever_the_status()
    {
        var iterm = new FakeIntegration("iterm2", "iTerm2", available: true)
        {
            ClipboardInstructions = "Tick the clipboard box.\nThen copy.",
        };
        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, new FakeEnvironment());

        Assert.Equal(["", "Tick the clipboard box.", "Then copy."], state.Lines.TakeLast(3));
    }

    [Fact]
    public void Build_names_the_iTerm2_setting_that_lets_copy_reach_the_clipboard()
    {
        var iterm = new TuiCode.Workbench.TerminalIntegration.Iterm2Integration(
            new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new FakeEnvironment().Set("TERM_PROGRAM", "iTerm.app"));

        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, new FakeEnvironment());

        Assert.Contains(state.Lines, l => l.Contains("\"Applications in terminal may access clipboard\""));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_notes_the_tmux_setting_copy_over_SSH_needs_inside_tmux(bool detected)
    {
        var iterm = new FakeIntegration("iterm2", "iTerm2", available: detected);
        var env = new FakeEnvironment().Set("TMUX", "/tmp/tmux-501/default,123,0");

        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, env);

        Assert.Equal("  set -g set-clipboard on", state.Lines[^1]);
    }

    [Fact]
    public void Build_leaves_out_the_tmux_note_outside_tmux()
    {
        var iterm = new FakeIntegration("iterm2", "iTerm2", available: true);

        var state = TerminalIntegrationPanelState.Build(new[] { iterm }, new FakeEnvironment());

        Assert.DoesNotContain(state.Lines, l => l.Contains("tmux"));
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
        public string? ClipboardInstructions { get; init; }
        public bool IsAvailable() => _available;
        public TerminalIntegrationStatus GetStatus() => Status;
        public void Install() { }
        public void Uninstall() { }
    }
}
