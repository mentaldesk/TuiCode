using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Commands with a fixed scope (#141): the focused scope's bindings are checked before Global's.
public class ScopedKeybindingServiceTests
{
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keys;
    private readonly List<string> _fired = [];
    private CommandScope _focus = CommandScope.Global;

    public ScopedKeybindingServiceTests()
    {
        _keys = new KeybindingService(_commands) { FocusedScope = () => _focus };
    }

    [Fact]
    public void Bind_puts_the_binding_in_its_commands_scope()
    {
        Register("cut", CommandScope.Explorer);
        _keys.Bind("Ctrl+X", "cut");

        var binding = Assert.Single(_keys.Bindings);
        Assert.Equal(CommandScope.Explorer, binding.Scope);
    }

    [Fact]
    public void An_explorer_binding_does_not_fire_when_the_editor_has_focus()
    {
        Register("cut", CommandScope.Explorer);
        _keys.Bind("Ctrl+X", "cut");
        _focus = CommandScope.Editor;

        Assert.Equal(KeyHandlingResult.Pass, _keys.Handle(Key.X.WithCtrl));
        Assert.Empty(_fired);
    }

    [Fact]
    public void The_same_chord_runs_the_focused_scopes_command_and_falls_back_to_Global_elsewhere()
    {
        Register("cut.file", CommandScope.Explorer);
        Register("cut.global");
        _keys.Bind("Ctrl+X", "cut.file");
        _keys.Bind("Ctrl+X", "cut.global");

        _focus = CommandScope.Explorer;
        _keys.Handle(Key.X.WithCtrl);
        _focus = CommandScope.Editor;
        _keys.Handle(Key.X.WithCtrl);

        Assert.Equal(["cut.file", "cut.global"], _fired);
        Assert.Equal(2, _keys.Bindings.Count());
    }

    [Fact]
    public void A_chord_started_in_a_scope_finishes_there_even_if_focus_moves()
    {
        Register("explorer.x", CommandScope.Explorer);
        Register("global.x");
        _keys.Bind("Ctrl+K X", "explorer.x");
        _keys.Bind("Ctrl+K X", "global.x");

        _focus = CommandScope.Explorer;
        Assert.Equal(KeyHandlingResult.ChordInProgress, _keys.Handle(Key.K.WithCtrl));
        _focus = CommandScope.Editor;
        Assert.Equal(KeyHandlingResult.Consumed, _keys.Handle(Key.X));

        Assert.Equal(["explorer.x"], _fired);
    }

    [Fact]
    public void A_chord_started_in_Global_finishes_in_Global_even_if_the_focused_scope_binds_its_second_key()
    {
        Register("goToLine");
        Register("explorer.l", CommandScope.Explorer);
        _keys.Bind("Ctrl+G L", "goToLine");
        _keys.Bind("L", "explorer.l");
        _focus = CommandScope.Explorer;

        _keys.Handle(Key.G.WithCtrl);
        _keys.Handle(Key.L);

        Assert.Equal(["goToLine"], _fired);
    }

    [Fact]
    public void A_disabled_scoped_command_lets_its_key_fall_through_to_Global()
    {
        var cutPending = false;
        Register("cancelCut", CommandScope.Explorer, () => cutPending);
        Register("focusEditor");
        _keys.Bind("Esc", "cancelCut");
        _keys.Bind("Esc", "focusEditor");
        _focus = CommandScope.Explorer;

        _keys.Handle(Key.Esc);
        cutPending = true;
        _keys.Handle(Key.Esc);

        Assert.Equal(["focusEditor", "cancelCut"], _fired);
    }

    [Fact]
    public void Unbind_removes_the_chord_only_in_the_given_scope()
    {
        Register("cancelCut", CommandScope.Explorer);
        Register("focusEditor");
        _keys.Bind("Esc", "cancelCut");
        _keys.Bind("Esc", "focusEditor");

        Assert.True(_keys.Unbind("Esc", CommandScope.Explorer));

        var left = Assert.Single(_keys.Bindings);
        Assert.Equal(("focusEditor", CommandScope.Global), (left.CommandId, left.Scope));
    }

    [Fact]
    public void CheckConflict_only_looks_in_the_given_scope()
    {
        Register("cut", CommandScope.Explorer);
        _keys.Bind("Ctrl+X", "cut");

        Assert.Null(_keys.CheckConflict("Ctrl+X"));
        Assert.Equal(KeybindingConflict.ExactMatch, _keys.CheckConflict("Ctrl+X", CommandScope.Explorer));
    }

    private void Register(string id, CommandScope scope = CommandScope.Global, Func<bool>? isEnabled = null) =>
        _commands.Register(id, id, () => _fired.Add(id), scope, isEnabled);
}
