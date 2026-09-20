using TuiCode.Abstractions;

namespace TuiCode.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("Alt+CursorDown", "Alt+↓")]
    [InlineData("Alt+CursorUp", "Alt+↑")]
    [InlineData("Shift+CursorLeft", "Shift+←")]
    [InlineData("CursorRight", "→")]
    [InlineData("G", "G")]
    [InlineData("Enter", "Enter")]
    [InlineData("PageUp", "PageUp")]
    public void Display_shows_the_cursor_keys_as_arrows(string sequence, string expected) =>
        Assert.Equal(expected, KeyChord.Display(TestKeys.Chord(sequence)));

    [Fact]
    public void Display_renders_every_step_of_a_chord()
    {
        Assert.Equal("Ctrl+W ↓", KeyChord.Display(TestKeys.Chord("Ctrl+W CursorDown")));
    }
}
