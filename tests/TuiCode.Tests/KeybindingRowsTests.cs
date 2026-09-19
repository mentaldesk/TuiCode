using TuiCode.Abstractions;
using TuiCode.Workbench.Settings;
using KeyBinding = TuiCode.Abstractions.KeyBinding;

namespace TuiCode.Tests;

// Keyboard Shortcuts rows with a When column (#142).
public class KeybindingRowsTests
{
    private static readonly CommandDescriptor[] Commands =
    [
        new("cut", "Cut file or folder", CommandScope.Explorer),
        new("next", "Next match", CommandScope.Find),
        new("open", "Open file or folder"),
    ];

    [Fact]
    public void Build_lists_every_command_with_its_scope()
    {
        var rows = KeybindingRows.Build(Commands, [Binding("Ctrl+X", "cut", CommandScope.Explorer)], "");

        Assert.Equal(
            [("Cut file or folder", "Ctrl+X", CommandScope.Explorer), ("Next match", KeybindingRows.Unbound, CommandScope.Find),
                ("Open file or folder", KeybindingRows.Unbound, CommandScope.Global)],
            rows.Select(r => (r.Label, r.Keys, r.Scope)));
    }

    [Fact]
    public void Build_filters_by_scope_name()
    {
        var rows = KeybindingRows.Build(Commands, [], "explorer");

        Assert.Equal(["cut"], rows.Select(r => r.CommandId));
    }

    [Fact]
    public void Display_puts_the_keys_and_scope_in_columns()
    {
        var row = new KeybindingRow("cut", "Cut file or folder", CommandScope.Explorer, Binding("Ctrl+X", "cut", CommandScope.Explorer));

        Assert.Equal("Cut file or folder         Ctrl+X        Explorer", row.Display);
        Assert.Equal("Command                    Keys          When", KeybindingRows.Header);
    }

    [Fact]
    public void Display_shortens_the_command_to_keep_long_keys_and_the_scope_in_the_row()
    {
        var chord = "Ctrl+Alt+Shift+G Ctrl+Alt+Shift+H";
        var row = new KeybindingRow("x", "Move or rename file or folder", CommandScope.Explorer, Binding(chord, "x", CommandScope.Explorer));

        Assert.EndsWith("Explorer", row.Display);
        Assert.True(row.Display.Length <= 49, row.Display);
    }

    private static KeyBinding Binding(string keys, string command, CommandScope scope) => new(TestKeys.Chord(keys), command, scope);
}
