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

    // The Keyboard Shortcuts list is 51 columns wide at 80×24.
    internal const int WidthAt80 = 51;

    [Fact]
    public void Display_puts_the_keys_and_scope_in_columns_two_spaces_apart()
    {
        var row = new KeybindingRow("cut", "Cut file or folder", CommandScope.Explorer, Binding("Ctrl+X", "cut", CommandScope.Explorer));

        Assert.Equal("Cut file or folder          Ctrl+X         Explorer", row.Display(WidthAt80));
        Assert.Equal("Command                     Keys           When", KeybindingRows.Header(WidthAt80));
    }

    [Fact]
    public void Display_keeps_the_longest_default_chord_two_spaces_from_its_neighbours_at_80_columns()
    {
        var row = new KeybindingRow("prev", "Previous editor", CommandScope.Global, Binding("Ctrl+Shift+Tab", "prev", CommandScope.Global));

        Assert.Equal("Previous editor            Ctrl+Shift+Tab  Global", row.Display(WidthAt80));
    }

    [Fact]
    public void Columns_widen_with_the_pane()
    {
        var narrow = Starts(KeybindingRows.Header(WidthAt80));
        var wide = Starts(KeybindingRows.Header(171));

        Assert.Equal(171 - "Explorer".Length, wide.When);
        Assert.True(wide.Keys > 2 * narrow.Keys, $"{wide}");
        Assert.True(wide.When - wide.Keys > 2 * (narrow.When - narrow.Keys), $"{wide}");

        static (int Keys, int When) Starts(string header) =>
            (header.IndexOf("Keys", StringComparison.Ordinal), header.IndexOf("When", StringComparison.Ordinal));
    }

    [Fact]
    public void Display_shortens_the_command_to_keep_long_keys_and_the_scope_in_the_row()
    {
        var chord = "Ctrl+Alt+Shift+G Ctrl+Alt+Shift+H Ctrl+Alt+Shift+J";
        var row = new KeybindingRow("x", "Move or rename file or folder", CommandScope.Explorer, Binding(chord, "x", CommandScope.Explorer));

        var display = row.Display(WidthAt80);

        Assert.True(display.Length <= WidthAt80, display);
        Assert.EndsWith("  Explorer", display);
        Assert.Contains("…  ", display);
    }

    [Fact]
    public void Display_truncates_a_long_command_with_an_ellipsis_at_80_columns()
    {
        var row = new KeybindingRow("x", "Move or rename file or folder", CommandScope.Explorer, Binding("F2", "x", CommandScope.Explorer));

        Assert.StartsWith("Move or rename file or fo…  F2", row.Display(WidthAt80));
    }

    private static KeyBinding Binding(string keys, string command, CommandScope scope) => new(TestKeys.Chord(keys), command, scope);
}
