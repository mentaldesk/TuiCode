using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

public class KeybindingConflictDialogTests
{
    private readonly View _host = new() { Width = 80, Height = 24, CanFocus = true };
    private readonly List<bool> _choices = [];

    public KeybindingConflictDialogTests()
    {
        _host.BeginInit();
        _host.EndInit();
        _host.SetFocus();
        KeybindingConflictDialog.Confirm(_host, new InputScopeStack(), "Ctrl+X is in use.", "Bind", _choices.Add);
    }

    [Fact]
    public void Confirm_focuses_Cancel_and_makes_it_the_default()
    {
        var buttons = _host.SubViews.OfType<Dialog>().Single().SubViews.OfType<Button>().ToList();

        var @default = Assert.Single(buttons, b => b.IsDefault);
        Assert.Equal("Cancel", @default.Text);
        Assert.True(@default.HasFocus);
    }

    [Fact]
    public void Confirm_calls_back_false_on_Enter_when_opened()
    {
        _host.NewKeyDownEvent(Key.Enter);

        Assert.Equal([false], _choices);
    }

    [Fact]
    public void Confirm_calls_back_true_on_Enter_after_focusing_the_confirm_button()
    {
        _host.SubViews.OfType<Dialog>().Single().SubViews.OfType<Button>().Single(b => b.Text == "Bind").SetFocus();

        _host.NewKeyDownEvent(Key.Enter);

        Assert.Equal([true], _choices);
    }
}
