using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Theming;
using TuiCode.Abstractions;
using TuiCode.Workbench.Controls;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

public class KeyedButtonTests
{
    [Fact]
    public void Of_writes_the_bound_key_before_the_label()
    {
        var keys = Keys(("Ctrl+Enter", "dialog.confirm"));

        var button = KeyedButton.Of(ButtonKind.Primary, keys, "dialog.confirm", "Submit");

        Assert.Equal(("Ctrl+Enter Submit", SchemeNames.ButtonPrimary), (button.Text.Trim(), button.SchemeName));
    }

    [Fact]
    public void Of_follows_the_key_the_command_is_bound_to()
    {
        var keys = Keys(("Ctrl+S", "dialog.confirm"));

        Assert.Equal("Ctrl+S Submit", KeyedButton.Of(ButtonKind.Primary, keys, "dialog.confirm", "Submit").Text.Trim());
    }

    [Fact]
    public void Of_names_the_first_key_bound_when_a_command_has_two()
    {
        var keys = Keys(("Esc", "dialog.close"), ("Enter", "dialog.close"));

        Assert.Equal("Esc Close", KeyedButton.Of(ButtonKind.Secondary, keys, "dialog.close", "Close").Text.Trim());
    }

    [Fact]
    public void Of_draws_each_kind_in_its_scheme()
    {
        var keys = Keys(("Ctrl+D", "dialog.delete"));

        Assert.Equal(
            [SchemeNames.ButtonPrimary, SchemeNames.ButtonDanger, SchemeNames.ButtonSecondary],
            new[] { ButtonKind.Primary, ButtonKind.Danger, ButtonKind.Secondary }.Select(k => KeyedButton.Of(k, keys, "dialog.delete", "Delete").SchemeName));
    }

    private static KeybindingService Keys(params (string Key, string CommandId)[] bindings)
    {
        var commands = new CommandService();
        var keys = new KeybindingService(commands);
        foreach (var (key, commandId) in bindings)
        {
            if (!commands.IsRegistered(commandId)) commands.Register(commandId, () => { });
            keys.Bind(key, commandId);
        }
        return keys;
    }
}
