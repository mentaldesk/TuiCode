using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Workbench.Controls;

namespace TuiCode.Tests;

public class InputViewTests : StaticConfigurationTest
{
    private readonly View _host = new() { Width = 40, Height = 10, CanFocus = true };
    private readonly InputView _input = new() { Width = Dim.Fill(), Height = Dim.Fill(1) };
    private readonly Button _elsewhere = new() { Y = Pos.AnchorEnd(1), Text = "Ok" };

    public InputViewTests() => _host.Add(_input, _elsewhere);

    public override void Dispose()
    {
        _host.Dispose();
        base.Dispose();
    }

    [Fact]
    public void The_box_is_drawn_faintly_until_it_has_focus()
    {
        Assert.Equal(LineStyle.Single, _input.BorderStyle);
        Assert.False(_input.ShowsFocus);
    }

    [Fact]
    public void Focus_thickens_the_box_so_typing_has_somewhere_visible_to_go()
    {
        _input.SetFocus();

        Assert.Equal(LineStyle.Heavy, _input.BorderStyle);
        Assert.True(_input.ShowsFocus);
    }

    [Fact]
    public void The_box_thins_again_when_focus_moves_on()
    {
        _input.SetFocus();
        _elsewhere.SetFocus();

        Assert.Equal(LineStyle.Single, _input.BorderStyle);
        Assert.False(_input.ShowsFocus);
    }

    [Fact]
    public void Tab_moves_focus_on_instead_of_typing_a_tab()
    {
        Assert.False(_input.TabKeyAddsTab);
    }
}
