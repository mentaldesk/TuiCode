using TuiCode.Abstractions;
using TuiCode.Workbench.Help;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class HelpSheetTests
{
    private static readonly (string Title, string[] Ids)[] Menu =
    [
        ("_File", ["file.open", "file.save"]),
        ("_Edit", ["edit.find"]),
        ("_Go", ["go.back"]),
    ];

    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;
    private bool _saveEnabled = true;

    public HelpSheetTests()
    {
        _keybindings = new KeybindingService(_commands);
        _commands.Register("go.back", "Navigate back", () => { });
        _commands.Register("file.open", "Open", () => { });
        _commands.Register("file.save", "Save", () => { }, isEnabled: () => _saveEnabled);
        _commands.Register("edit.find", "Find in file", () => { });
        _commands.Register("palette", "Show all commands", () => { });
        _commands.Register("editor.moveUp", "Move line up", () => { }, CommandScope.Editor);
        _keybindings.Bind("Ctrl+O", "file.open");
        _keybindings.Bind("Ctrl+S", "file.save");
        _keybindings.Bind("Ctrl+G P", "go.back");
        _keybindings.Bind("Ctrl+E", "palette");
        _keybindings.Bind("Alt+CursorUp", "editor.moveUp");
    }

    private KeyColumn Everywhere() => HelpSheet.Everywhere(_commands, _keybindings, Menu);

    [Fact]
    public void Everywhere_groups_bound_commands_under_their_menu_heading_in_menu_order_then_other()
    {
        var everywhere = Everywhere();

        Assert.Equal("Everywhere", everywhere.Heading);
        Assert.Equal(["File", "Go", "Other"], everywhere.Groups.Select(group => group.Heading));
        Assert.Equal([new KeyRow("Ctrl+O", "Open"), new KeyRow("Ctrl+S", "Save")], everywhere.Groups[0].Rows);
        Assert.Equal([new KeyRow("Ctrl+G p", "Navigate back")], everywhere.Groups[1].Rows);
        Assert.Equal([new KeyRow("Ctrl+E", "Show all commands")], everywhere.Groups[2].Rows);
    }

    [Fact]
    public void Everywhere_leaves_out_a_menu_whose_commands_have_no_key()
    {
        Assert.DoesNotContain(Everywhere().Groups, group => group.Heading == "Edit");
    }

    [Fact]
    public void A_newly_bound_command_appears_under_its_menu_heading()
    {
        _keybindings.Bind("F3", "edit.find");

        Assert.Equal([new KeyRow("F3", "Find in file")], Everywhere().Groups.Single(group => group.Heading == "Edit").Rows);
    }

    [Fact]
    public void A_rebind_replaces_the_old_key()
    {
        _keybindings.Unbind("Ctrl+E");
        _keybindings.Bind("F6", "palette");

        var rows = Everywhere().Rows();

        Assert.Contains(new KeyRow("F6", "Show all commands"), rows);
        Assert.DoesNotContain(rows, row => row.Keys == "Ctrl+E");
    }

    [Fact]
    public void An_unbound_command_is_left_out()
    {
        _keybindings.Unbind("Ctrl+O");

        Assert.DoesNotContain(Everywhere().Rows(), row => row.Label == "Open");
    }

    [Fact]
    public void A_command_that_cannot_run_now_is_left_out()
    {
        _saveEnabled = false;

        Assert.DoesNotContain(Everywhere().Rows(), row => row.Label == "Save");
    }

    [Fact]
    public void A_command_with_two_keys_is_one_row()
    {
        _keybindings.Bind("F5", "file.open");

        Assert.Contains(new KeyRow("Ctrl+O, F5", "Open"), Everywhere().Rows());
    }

    [Fact]
    public void Editor_commands_are_left_to_the_editor_column()
    {
        Assert.DoesNotContain(Everywhere().Rows(), row => row.Label == "Move line up");
    }

    [Fact]
    public void The_place_goes_beside_everywhere_and_keeps_its_rows_in_order()
    {
        var sheet = HelpSheet.For(HelpSheet.Place("Explorer", [new("Enter", "Open"), new("F2", "Move or rename")]), _commands, _keybindings);

        Assert.Equal("Explorer", sheet.Here!.Heading);
        Assert.Equal([new KeyRow("Enter", "Open"), new KeyRow("F2", "Move or rename")], sheet.Here.Rows());
        Assert.Equal("Everywhere", sheet.Everywhere!.Heading);
    }
}
