using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class LayeredScopeTests
{
    private readonly CommandService _commands = new();
    private readonly KeybindingService _below;
    private readonly KeybindingService _own;
    private string _fired = "";

    public LayeredScopeTests()
    {
        _below = new KeybindingService(_commands);
        _own = new KeybindingService(_commands);
        _commands.Register("below.enter", () => _fired = "below.enter");
        _commands.Register("below.save", () => _fired = "below.save");
        _commands.Register("below.chord", () => _fired = "below.chord");
        _commands.Register("own.enter", () => _fired = "own.enter");
        _below.Bind("Enter", "below.enter");
        _below.Bind("Ctrl+S", "below.save");
        _below.Bind("Ctrl+G Enter", "below.chord");
        _own.Bind("Enter", "own.enter");
    }

    [Fact]
    public void Own_binding_wins_while_the_layer_accepts_the_key()
    {
        var scope = new LayeredScope(_own, _below, _ => true);

        Assert.Equal(KeyHandlingResult.Consumed, scope.Handle(Key.Enter));
        Assert.Equal("own.enter", _fired);
    }

    [Fact]
    public void Unbound_keys_fall_through_to_the_scope_below()
    {
        var scope = new LayeredScope(_own, _below, _ => true);

        scope.Handle(Key.S.WithCtrl);

        Assert.Equal("below.save", _fired);
    }

    [Fact]
    public void Keys_the_layer_does_not_accept_go_straight_below()
    {
        var scope = new LayeredScope(_own, _below, _ => false);

        scope.Handle(Key.Enter);

        Assert.Equal("below.enter", _fired);
    }

    [Fact]
    public void A_chord_in_flight_below_finishes_below()
    {
        var scope = new LayeredScope(_own, _below, _ => true);

        Assert.Equal(KeyHandlingResult.ChordInProgress, scope.Handle(Key.G.WithCtrl));
        Assert.Equal("Ctrl+G", scope.CurrentChord);
        scope.Handle(Key.Enter);

        Assert.Equal("below.chord", _fired);
    }
}
