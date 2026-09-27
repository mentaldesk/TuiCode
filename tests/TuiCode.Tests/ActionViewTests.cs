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
        ("save", "Save active editor", CommandScope.Global),
        ("quit", "Quit", CommandScope.Global),
        ("move", "Move line up", CommandScope.Editor),
        ("delete", "Delete file or folder", CommandScope.Explorer),
        ("results", "Focus find results", CommandScope.Find),
        ("next", "Next change", CommandScope.Diff),
    ];

    [Theory]
    [InlineData(CommandScope.Global, "Quit", "Save active editor")]
    [InlineData(CommandScope.Editor, "Move line up", "Quit", "Save active editor")]
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

    private static string Row(ActionView view, string label) =>
        view.SubViews.OfType<ListView>().Single().Source!.ToList().Cast<string>()
            .Single(r => r.StartsWith(label, StringComparison.Ordinal));
}
