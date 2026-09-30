using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The palette lists Global plus the scope it was opened in (#284). Not hosted in a running app, so the
// rows are whatever the constructor built.
public class ActionViewTests
{
    private static readonly (string Id, string Label, CommandScope Scope)[] Commands =
    [
        (CommandIds.SaveActiveEditor, "Save active editor", CommandScope.Global),
        (CommandIds.SelectAllOccurrences, "Select all occurrences", CommandScope.Editor),
        ("quit", "Quit", CommandScope.Global),
        ("move", "Move line up", CommandScope.Editor),
        ("delete", "Delete file or folder", CommandScope.Explorer),
        ("results", "Focus find results", CommandScope.Find),
        ("next", "Next change", CommandScope.Diff),
    ];

    [Theory]
    [InlineData(CommandScope.Global, "Quit", "Save active editor")]
    [InlineData(CommandScope.Editor, "Move line up", "Quit", "Save active editor", "Select all occurrences")]
    [InlineData(CommandScope.Explorer, "Delete file or folder", "Quit", "Save active editor")]
    [InlineData(CommandScope.Find, "Focus find results", "Quit", "Save active editor")]
    [InlineData(CommandScope.Diff, "Next change", "Quit", "Save active editor")]
    public void The_rows_are_the_Global_commands_plus_that_scopes_in_label_order(CommandScope scope, params string[] expected)
    {
        using var view = Build(scope);

        Assert.Equal(expected, view.Labels);
    }

    [Fact]
    public void A_commands_keys_are_still_shown_beside_its_label()
    {
        var commands = Registered();
        var keybindings = new KeybindingService(commands);
        keybindings.Bind("Alt+CursorUp", "move");
        using var view = new ActionView(commands, keybindings, CommandScope.Editor, _ => { });

        Assert.Contains("Alt+↑", Row(view, "Move line up"));
    }

    [Fact]
    public void Typing_filters_within_the_scope_the_palette_captured()
    {
        using var explorerPalette = Build(CommandScope.Explorer);
        using var editorPalette = Build(CommandScope.Editor);

        Type(explorerPalette, "move line");
        Type(editorPalette, "move line");

        Assert.Empty(explorerPalette.Labels);
        Assert.Equal(["Move line up"], editorPalette.Labels);
    }

    [Fact]
    public void Each_row_ends_with_the_commands_mnemonic_or_nothing_if_it_has_none()
    {
        using var view = Build(CommandScope.Editor);

        Assert.EndsWith($"  {CommandMnemonics.For(CommandIds.SaveActiveEditor)}", view.Row("Save active editor", 72));
        Assert.EndsWith($"  {CommandMnemonics.For(CommandIds.SelectAllOccurrences)}", view.Row("Select all occurrences", 72));
        Assert.Equal("Quit", view.Row("Quit", 72).TrimEnd());
    }

    [Theory]
    [InlineData("sf", "Save active editor")]
    [InlineData("SAO", "Select all occurrences")]
    [InlineData("occurr", "Select all occurrences")]
    [InlineData("Alt+", "Move line up")]
    public void Typing_a_mnemonic_label_or_key_finds_the_command(string query, string expected)
    {
        var commands = Registered();
        var keybindings = new KeybindingService(commands);
        keybindings.Bind("Alt+CursorUp", "move");
        using var view = new ActionView(commands, keybindings, CommandScope.Editor, _ => { });

        Type(view, query);

        Assert.Equal([expected], view.Labels);
    }

    [Fact]
    public void The_mnemonics_line_up_and_a_narrow_list_shortens_the_label_rather_than_the_mnemonic()
    {
        var commands = Registered();
        var keybindings = new KeybindingService(commands);
        keybindings.Bind("Ctrl+Shift+L", CommandIds.SelectAllOccurrences);
        using var view = new ActionView(commands, keybindings, CommandScope.Editor, _ => { });

        var wide = view.Row("Select all occurrences", 72);
        var narrow = view.Row("Select all occurrences", 37);

        Assert.Equal(72, wide.Length);
        Assert.Equal(view.Row("Save active editor", 72).Length, wide.Length);
        Assert.Equal(37, narrow.Length);
        Assert.EndsWith("sao", narrow);
        Assert.Contains("…", narrow);
        Assert.Contains("Ctrl+Shift+L", narrow);
    }

    [Fact]
    public void The_columns_are_headed_Command_Binding_and_Mnemonic_over_the_values_they_name()
    {
        var commands = Registered();
        var keybindings = new KeybindingService(commands);
        keybindings.Bind("Ctrl+S", CommandIds.SaveActiveEditor);
        using var view = new ActionView(commands, keybindings, CommandScope.Editor, _ => { });
        view.Layout(new System.Drawing.Size(76, 22));

        var row = view.Row("Save active editor", view.Header.Length);

        Assert.StartsWith("Command", view.Header);
        Assert.Equal(row.IndexOf("Ctrl+S", StringComparison.Ordinal), view.Header.IndexOf("Binding", StringComparison.Ordinal));
        Assert.EndsWith("Mnemonic", view.Header);
        Assert.EndsWith(CommandMnemonics.For(CommandIds.SaveActiveEditor)!, row);
    }

    [Fact]
    public void The_filter_field_is_labelled()
    {
        using var view = Build(CommandScope.Global);

        Assert.Contains(view.SubViews.OfType<Label>(), l => l.Text == "Filter:");
    }

    private static ActionView Build(CommandScope scope)
    {
        var commands = Registered();
        return new ActionView(commands, new KeybindingService(commands), scope, _ => { });
    }

    private static CommandService Registered()
    {
        var commands = new CommandService();
        foreach (var (id, label, scope) in Commands) commands.Register(id, label, () => { }, scope);
        return commands;
    }

    private static void Type(ActionView view, string query) =>
        view.SubViews.OfType<TextField>().Single().Text = query;

    private static string Row(ActionView view, string label) => view.Row(label, 72);
}
