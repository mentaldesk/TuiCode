using TuiCode.Abstractions;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class KeybindingServiceTests
{
    [Fact]
    public void Single_key_binding_executes_the_command_and_consumes_the_key()
    {
        var (commands, keys, fired) = SetUp();
        keys.Bind("Ctrl+S", "save");
        commands.Register("save", () => fired.Add("save"));

        var result = keys.Handle(ParseKey("Ctrl+S"));

        Assert.Equal(KeyHandlingResult.Consumed, result);
        Assert.Equal(["save"], fired);
    }

    [Fact]
    public void Unbound_key_passes_through()
    {
        var (_, keys, _) = SetUp();
        var result = keys.Handle(ParseKey("Ctrl+J"));
        Assert.Equal(KeyHandlingResult.Pass, result);
    }

    [Fact]
    public void Chord_prefix_consumes_the_first_key_and_waits()
    {
        var (commands, keys, fired) = SetUp();
        keys.Bind("Ctrl+W X", "focus.explorer");
        commands.Register("focus.explorer", () => fired.Add("focus.explorer"));

        var first = keys.Handle(ParseKey("Ctrl+W"));

        Assert.Equal(KeyHandlingResult.ChordInProgress, first);
        Assert.Empty(fired);
        Assert.Equal("Ctrl+W", keys.CurrentChord);
    }

    [Fact]
    public void Chord_completion_fires_the_command_and_clears_the_chord()
    {
        var (commands, keys, fired) = SetUp();
        keys.Bind("Ctrl+W X", "focus.explorer");
        commands.Register("focus.explorer", () => fired.Add("focus.explorer"));

        keys.Handle(ParseKey("Ctrl+W"));
        var second = keys.Handle(ParseKey("X"));

        Assert.Equal(KeyHandlingResult.Consumed, second);
        Assert.Equal(["focus.explorer"], fired);
        Assert.Null(keys.CurrentChord);
    }

    [Fact]
    public void Esc_cancels_an_in_flight_chord_silently()
    {
        var (commands, keys, fired) = SetUp();
        keys.Bind("Ctrl+W X", "focus.explorer");
        commands.Register("focus.explorer", () => fired.Add("focus.explorer"));

        keys.Handle(ParseKey("Ctrl+W"));
        var esc = keys.Handle(ParseKey("Esc"));

        Assert.Equal(KeyHandlingResult.Consumed, esc);
        Assert.Empty(fired);
        Assert.Null(keys.CurrentChord);
    }

    [Fact]
    public void Stray_key_during_chord_aborts_the_chord_and_is_consumed()
    {
        var (commands, keys, fired) = SetUp();
        keys.Bind("Ctrl+W X", "focus.explorer");
        commands.Register("focus.explorer", () => fired.Add("focus.explorer"));

        keys.Handle(ParseKey("Ctrl+W"));
        var stray = keys.Handle(ParseKey("Z"));

        Assert.Equal(KeyHandlingResult.Consumed, stray);
        Assert.Empty(fired);
        Assert.Null(keys.CurrentChord);
    }

    [Fact]
    public void Chord_can_be_completed_after_an_earlier_chord_was_cancelled()
    {
        var (commands, keys, fired) = SetUp();
        keys.Bind("Ctrl+W X", "focus.explorer");
        commands.Register("focus.explorer", () => fired.Add("focus.explorer"));

        keys.Handle(ParseKey("Ctrl+W"));
        keys.Handle(ParseKey("Esc"));
        keys.Handle(ParseKey("Ctrl+W"));
        keys.Handle(ParseKey("X"));

        Assert.Equal(["focus.explorer"], fired);
    }

    [Theory]
    [InlineData("X", "x")] // binding uppercase, type lowercase
    [InlineData("x", "X")] // binding lowercase, type uppercase
    [InlineData("X", "X")] // both upper
    [InlineData("x", "x")] // both lower
    public void Letter_chord_step_is_case_insensitive_without_extra_modifiers(string boundLetter, string typedLetter)
    {
        var (commands, keys, fired) = SetUp();
        keys.Bind($"Ctrl+Alt+Shift+W {boundLetter}", "focus");
        commands.Register("focus", () => fired.Add("focus"));

        keys.Handle(ParseKey("Ctrl+Alt+Shift+W"));
        keys.Handle(ParseKey(typedLetter));

        Assert.Equal(["focus"], fired);
    }

    [Fact]
    public void ChordChanged_event_fires_on_enter_and_clear()
    {
        var (commands, keys, _) = SetUp();
        keys.Bind("Ctrl+W X", "focus.explorer");
        commands.Register("focus.explorer", () => { });
        var transitions = new List<string?>();
        keys.ChordChanged += (_, c) => transitions.Add(c);

        keys.Handle(ParseKey("Ctrl+W"));
        keys.Handle(ParseKey("X"));

        Assert.Equal(["Ctrl+W", null], transitions);
    }

    private static (CommandService commands, KeybindingService keys, List<string> fired) SetUp()
    {
        var commands = new CommandService();
        var keys = new KeybindingService(commands);
        var fired = new List<string>();
        return (commands, keys, fired);
    }

    private static Key ParseKey(string s) =>
        Key.TryParse(s, out var k) ? k : throw new InvalidOperationException($"Bad key: {s}");
}
